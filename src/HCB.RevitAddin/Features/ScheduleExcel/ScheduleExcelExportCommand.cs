using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ScheduleExcel.Models;
using HCB.RevitAddin.Features.ScheduleExcel.UI;

namespace HCB.RevitAddin.Features.ScheduleExcel;

[Transaction(TransactionMode.ReadOnly)]
public sealed class ScheduleExcelExportCommand : IExternalCommand
{
    private const string Title = "Eksport zestawienia do Excel";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        Document? document = uiDocument?.Document;
        if (document == null || document.IsFamilyDocument)
        {
            TaskDialog.Show(Title, "Otwórz projekt z zestawieniami.");
            return Result.Cancelled;
        }

        IReadOnlyList<ViewSchedule> schedules = ScheduleExcelReader.GetExportableSchedules(document);
        if (schedules.Count == 0)
        {
            TaskDialog.Show(Title, "W modelu nie ma zestawień, które można wyeksportować.");
            return Result.Cancelled;
        }

        ScheduleExcelSettings settings = ScheduleExcelSettings.Load();
        List<ScheduleExcelListItem> items = schedules
            .Select(schedule => new ScheduleExcelListItem(schedule.Id.Value, schedule.Name, Describe(schedule)))
            .ToList();
        long? activeId = GetPreselectedScheduleId(uiDocument!, document);

        var window = new ScheduleExcelExportWindow(
            items,
            activeId,
            GetDefaultFolder(settings, document),
            settings.OpenAfterExport,
            settings.ProtectReadOnlyCells);
        if (window.ShowDialog() != true || window.SelectedSchedule == null)
        {
            return Result.Cancelled;
        }

        settings.LastFolder = window.Folder;
        settings.OpenAfterExport = window.OpenAfterExport;
        settings.ProtectReadOnlyCells = window.ProtectReadOnlyCells;
        settings.Save();

        var schedule = (ViewSchedule)document.GetElement(new ElementId(window.SelectedSchedule.Id));
        var options = new ScheduleExcelExportOptions
        {
            FilePath = Path.Combine(window.Folder, ToFileName(schedule.Name) + ".xlsx"),
            OpenAfterExport = window.OpenAfterExport,
            ProtectReadOnlyCells = window.ProtectReadOnlyCells
        };

        if (IsFileLocked(options.FilePath))
        {
            TaskDialog.Show(Title, $"Plik jest otwarty w innym programie (np. Excel). Zamknij go i spróbuj ponownie.\n\n{options.FilePath}");
            return Result.Cancelled;
        }

        ScheduleExcelData data;
        try
        {
            data = new ScheduleExcelReader(document).Read(schedule);
            WriteWorkbook(data, options);
        }
        catch (Exception exception) when (exception is FileLoadException or FileNotFoundException or TypeLoadException or MissingMethodException)
        {
            TaskDialog.Show(Title,
                "Nie udało się załadować biblioteki Excel (ClosedXML). Inny dodatek mógł załadować jej niezgodną wersję.\n\n"
                + exception.Message);
            return Result.Failed;
        }
        catch (Exception exception)
        {
            message = exception.Message;
            TaskDialog.Show(Title, $"Eksport nie powiódł się.\n\n{exception.Message}");
            return Result.Failed;
        }

        if (options.OpenAfterExport)
        {
            OpenFile(options.FilePath);
        }
        else
        {
            TaskDialog.Show(Title, Summarize(data, options.FilePath));
        }

        return Result.Succeeded;
    }

    /// <summary>Separate method so a missing ClosedXML assembly fails here, inside the try block, not at JIT of Execute.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteWorkbook(ScheduleExcelData data, ScheduleExcelExportOptions options)
    {
        ScheduleExcelWorkbookWriter.Write(data, options);
    }

    /// <summary>
    /// Schedule picked in the Project Browser or placed on the active sheet, else the open schedule view.
    /// </summary>
    private static long? GetPreselectedScheduleId(UIDocument uiDocument, Document document)
    {
        foreach (ElementId id in uiDocument.Selection.GetElementIds())
        {
            switch (document.GetElement(id))
            {
                case ViewSchedule schedule:
                    return schedule.Id.Value;
                case ScheduleSheetInstance instance:
                    return instance.ScheduleId.Value;
            }
        }

        return document.ActiveView is ViewSchedule active ? active.Id.Value : null;
    }

    private static string Describe(ViewSchedule schedule)
    {
        ScheduleDefinition definition = schedule.Definition;
        string mode = definition.IsItemized ? "każdy element w osobnym wierszu" : "wiersze grupowane";
        return $"Pola: {definition.GetFieldCount()}, {mode}.";
    }

    private static string Summarize(ScheduleExcelData data, string filePath)
    {
        int readOnly = data.Columns.Count(column => column.IsReadOnly);
        return $"Zapisano: {filePath}\n\n"
            + $"Wiersze: {data.Rows.Count}, elementy: {data.ElementCount}.\n"
            + $"Kolumny: {data.Columns.Count}, w tym tylko do odczytu: {readOnly}.";
    }

    private static string GetDefaultFolder(ScheduleExcelSettings settings, Document document)
    {
        if (Directory.Exists(settings.LastFolder))
        {
            return settings.LastFolder;
        }

        string? modelFolder = string.IsNullOrEmpty(document.PathName) || document.IsModelInCloud
            ? null
            : Path.GetDirectoryName(document.PathName);
        return modelFolder != null && Directory.Exists(modelFolder)
            ? modelFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string ToFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return clean.Length == 0 ? "Zestawienie" : clean;
    }

    private static bool IsFileLocked(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            TaskDialog.Show(Title, $"Plik zapisano, ale nie udało się go otworzyć.\n\n{path}\n\n{exception.Message}");
        }
    }
}
