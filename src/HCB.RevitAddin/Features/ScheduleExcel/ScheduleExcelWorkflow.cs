using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ScheduleExcel.Models;
using HCB.RevitAddin.Features.ScheduleExcel.UI;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>Steps shared by the export, sync and import commands.</summary>
internal static class ScheduleExcelWorkflow
{
    /// <summary>Shows the schedule picker. Null when the user cancels or there is nothing to export.</summary>
    public static (ViewSchedule Schedule, ScheduleExcelExportOptions Options)? PickSchedule(
        UIDocument uiDocument,
        string title,
        string description,
        string primaryButton)
    {
        Document document = uiDocument.Document;
        IReadOnlyList<ViewSchedule> schedules = ScheduleExcelReader.GetExportableSchedules(document);
        if (schedules.Count == 0)
        {
            TaskDialog.Show(title, "W modelu nie ma zestawień, które można wyeksportować.");
            return null;
        }

        ScheduleExcelSettings settings = ScheduleExcelSettings.Load();
        List<ScheduleExcelListItem> items = schedules
            .Select(schedule => new ScheduleExcelListItem(schedule.Id.Value, schedule.Name, Describe(schedule)))
            .ToList();

        var window = new ScheduleExcelExportWindow(
            title,
            description,
            primaryButton,
            items,
            GetPreselectedScheduleId(uiDocument),
            GetDefaultFolder(settings, document),
            settings.OpenAfterExport,
            settings.ProtectReadOnlyCells,
            settings.ExpandGroups);
        if (window.ShowDialog() != true || window.SelectedSchedule == null)
        {
            return null;
        }

        settings.LastFolder = window.Folder;
        settings.OpenAfterExport = window.OpenAfterExport;
        settings.ProtectReadOnlyCells = window.ProtectReadOnlyCells;
        settings.ExpandGroups = window.ExpandGroups;
        settings.Save();

        var schedule = (ViewSchedule)document.GetElement(new ElementId(window.SelectedSchedule.Id));
        var options = new ScheduleExcelExportOptions
        {
            FilePath = Path.Combine(window.Folder, ToFileName(schedule.Name) + ".xlsx"),
            OpenAfterExport = window.OpenAfterExport,
            ProtectReadOnlyCells = window.ProtectReadOnlyCells,
            ExpandGroups = window.ExpandGroups
        };

        string? path = ConfirmOverwrite(options.FilePath, title);
        if (path == null)
        {
            return null;
        }

        options.FilePath = path;
        return (schedule, options);
    }

    /// <summary>
    /// Asks before replacing an existing file: overwrite, save under a free name ("Name (2).xlsx") or cancel.
    /// Returns the path to write, or null when the user cancels.
    /// </summary>
    private static string? ConfirmOverwrite(string path, string title)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string alternative = GetFreePath(path);
        var dialog = new TaskDialog(title)
        {
            MainInstruction = "Plik o tej nazwie już istnieje.",
            MainContent = $"{path}\n\nZmiany wprowadzone w tym pliku, a niezapisane jeszcze do Revit, zostaną utracone po nadpisaniu.",
            CommonButtons = TaskDialogCommonButtons.Cancel
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Nadpisz istniejący plik");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"Zapisz jako nowy plik: {Path.GetFileName(alternative)}");

        // Revit validates DefaultButton against existing buttons, so it can only be set after AddCommandLink.
        dialog.DefaultButton = TaskDialogResult.CommandLink2;

        return dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => path,
            TaskDialogResult.CommandLink2 => alternative,
            _ => null
        };
    }

    private static string GetFreePath(string path)
    {
        string folder = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int number = 2; ; number++)
        {
            string candidate = Path.Combine(folder, $"{name} ({number}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Reads the schedule and writes the workbook. Returns null after showing the error to the user.</summary>
    public static ScheduleExcelData? Export(Document document, ViewSchedule schedule, ScheduleExcelExportOptions options, string title)
    {
        if (IsFileLocked(options.FilePath))
        {
            TaskDialog.Show(title, $"Plik jest otwarty w innym programie (np. Excel). Zamknij go i spróbuj ponownie.\n\n{options.FilePath}");
            return null;
        }

        try
        {
            ScheduleExcelData data = new ScheduleExcelReader(document).Read(schedule, options.ExpandGroups);
            WriteWorkbook(data, options);
            ScheduleExcelLog.Info($"Eksport '{schedule.Name}' -> {options.FilePath}: wiersze {data.Rows.Count}, elementy {data.ElementCount}.");
            return data;
        }
        catch (Exception exception) when (exception is FileLoadException or FileNotFoundException or TypeLoadException or MissingMethodException)
        {
            ScheduleExcelLog.Error("Biblioteka Excel nie załadowała się.", exception);
            TaskDialog.Show(title,
                "Nie udało się załadować biblioteki Excel (ClosedXML). Inny dodatek mógł załadować jej niezgodną wersję.\n\n"
                + exception.Message);
            return null;
        }
        catch (Exception exception)
        {
            ScheduleExcelLog.Error($"Eksport '{schedule.Name}' nie powiódł się.", exception);
            TaskDialog.Show(title, $"Eksport nie powiódł się.\n\n{exception.Message}");
            return null;
        }
    }

    /// <summary>Separate method so a missing ClosedXML assembly fails inside the caller's try block, not at JIT.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteWorkbook(ScheduleExcelData data, ScheduleExcelExportOptions options)
    {
        ScheduleExcelWorkbookWriter.Write(data, options);
    }

    /// <summary>Reads an HCB workbook; null after showing the error to the user.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ScheduleExcelWorkbookContent? ReadWorkbook(string path, string title)
    {
        try
        {
            return ScheduleExcelWorkbookReader.Read(ScheduleExcelWorkbookReader.ReadBytes(path));
        }
        catch (Exception exception)
        {
            ScheduleExcelLog.Error($"Odczyt pliku {path} nie powiódł się.", exception);
            TaskDialog.Show(title, $"Nie udało się odczytać pliku.\n\n{path}\n\n{exception.Message}");
            return null;
        }
    }

    public static string Summarize(ScheduleExcelData data, string filePath)
    {
        int readOnly = data.Columns.Count(column => column.IsReadOnly);
        return $"Zapisano: {filePath}\n\n"
            + $"Wiersze: {data.Rows.Count}, elementy: {data.ElementCount}.\n"
            + $"Kolumny: {data.Columns.Count}, w tym tylko do odczytu: {readOnly}.";
    }

    public static string DescribeSkipped(IReadOnlyList<ScheduleExcelSkip> skipped, int max = 10)
    {
        if (skipped.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> lines = skipped.Take(max).Select(skip => $"- {skip.Column}: {skip.Reason}");
        return string.Join("\n", lines) + (skipped.Count > max ? $"\n... i {skipped.Count - max} więcej (szczegóły w logu)" : string.Empty);
    }

    public static void OpenFile(string path, string title)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            TaskDialog.Show(title, $"Plik zapisano, ale nie udało się go otworzyć.\n\n{path}\n\n{exception.Message}");
        }
    }

    public static bool IsFileLocked(string path)
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

    /// <summary>Schedule picked in the Project Browser or placed on the active sheet, else the open schedule view.</summary>
    private static long? GetPreselectedScheduleId(UIDocument uiDocument)
    {
        Document document = uiDocument.Document;
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
}
