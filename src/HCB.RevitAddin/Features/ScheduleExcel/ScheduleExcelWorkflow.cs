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
    /// <summary>
    /// Shows the schedule picker (one or many). Returns the schedules, the options and the files to write:
    /// one file, or one per schedule when the export asks for separate files. Null when cancelled.
    /// </summary>
    public static (IReadOnlyList<ViewSchedule> Schedules, ScheduleExcelExportOptions Options, IReadOnlyList<string> Paths)? PickSchedules(
        UIDocument uiDocument,
        ScheduleExcelWindowMode mode,
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
            mode,
            title,
            description,
            primaryButton,
            items,
            GetPreselectedScheduleIds(uiDocument),
            GetDefaultFolder(settings, document),
            settings.OpenAfterExport,
            settings.ProtectReadOnlyCells,
            settings.ExpandGroups,
            settings.AddIdColumn,
            settings.SeparateFiles);
        if (window.ShowDialog() != true || window.SelectedSchedules.Count == 0)
        {
            return null;
        }

        bool sync = mode == ScheduleExcelWindowMode.Sync;
        settings.LastFolder = window.Folder;
        settings.AddIdColumn = window.AddIdColumn;
        if (sync)
        {
            settings.ProtectReadOnlyCells = window.ProtectReadOnlyCells;
            settings.ExpandGroups = window.ExpandGroups;
        }
        else
        {
            settings.OpenAfterExport = window.OpenAfterExport;
            settings.SeparateFiles = window.SeparateFiles;
        }

        settings.Save();

        List<ViewSchedule> picked = window.SelectedSchedules
            .Select(item => (ViewSchedule)document.GetElement(new ElementId(item.Id)))
            .ToList();

        // Editing options belong to Sync; a plain export always writes the schedule as Revit shows it.
        var options = new ScheduleExcelExportOptions
        {
            OpenAfterExport = sync || window.OpenAfterExport,
            ProtectReadOnlyCells = sync && window.ProtectReadOnlyCells,
            ExpandGroups = sync && window.ExpandGroups,
            AddIdColumn = window.AddIdColumn,
            SeparateFiles = window.SeparateFiles && picked.Count > 1
        };

        List<string> paths = options.SeparateFiles
            ? picked.Select(schedule => Path.Combine(window.Folder, ToFileName(schedule.Name) + ".xlsx")).ToList()
            : [Path.Combine(window.Folder, ToFileName(picked.Count == 1 ? picked[0].Name : $"{document.Title} - zestawienia") + ".xlsx")];

        IReadOnlyList<string>? confirmed = ConfirmOverwrite(paths, title);
        if (confirmed == null)
        {
            return null;
        }

        options.FilePath = confirmed[0];
        return (picked, options, confirmed);
    }

    /// <summary>
    /// Asks once before replacing existing files: overwrite them, save the clashing ones under a free name
    /// ("Name (2).xlsx") or cancel. Returns the paths to write, or null when the user cancels.
    /// </summary>
    private static IReadOnlyList<string>? ConfirmOverwrite(IReadOnlyList<string> paths, string title)
    {
        List<string> existing = paths.Where(File.Exists).ToList();
        if (existing.Count == 0)
        {
            return paths;
        }

        string listed = string.Join("\n", existing.Take(8).Select(Path.GetFileName))
            + (existing.Count > 8 ? $"\n... i {existing.Count - 8} więcej" : string.Empty);
        var dialog = new TaskDialog(title)
        {
            MainInstruction = existing.Count == 1 ? "Plik o tej nazwie już istnieje." : $"Istnieje już {existing.Count} plików o tych nazwach.",
            MainContent = $"{Path.GetDirectoryName(existing[0])}\n{listed}\n\nZmiany wprowadzone w tych plikach, a niezapisane jeszcze do Revit, zostaną utracone po nadpisaniu.",
            CommonButtons = TaskDialogCommonButtons.Cancel
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, existing.Count == 1 ? "Nadpisz istniejący plik" : "Nadpisz istniejące pliki");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, existing.Count == 1
            ? $"Zapisz jako nowy plik: {Path.GetFileName(GetFreePath(existing[0]))}"
            : "Zapisz jako nowe pliki z numerem, np. \"Nazwa (2).xlsx\"");

        // Revit validates DefaultButton against existing buttons, so it can only be set after AddCommandLink.
        dialog.DefaultButton = TaskDialogResult.CommandLink2;

        return dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => paths,
            TaskDialogResult.CommandLink2 => paths.Select(path => File.Exists(path) ? GetFreePath(path) : path).ToList(),
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

    /// <summary>Reads the schedules and writes one workbook. Returns null after showing the error to the user.</summary>
    public static IReadOnlyList<ScheduleExcelData>? Export(Document document, IReadOnlyList<ViewSchedule> schedules, ScheduleExcelExportOptions options, string title)
    {
        if (IsFileLocked(options.FilePath))
        {
            TaskDialog.Show(title, $"Plik jest otwarty w innym programie (np. Excel). Zamknij go i spróbuj ponownie.\n\n{options.FilePath}");
            return null;
        }

        try
        {
            var reader = new ScheduleExcelReader(document);
            List<ScheduleExcelData> data = schedules.Select(schedule => reader.Read(schedule, options.ExpandGroups, options.AddIdColumn)).ToList();
            WriteWorkbook(data, options);
            ScheduleExcelLog.Info($"Eksport {data.Count} zestawień -> {options.FilePath}: "
                + string.Join("; ", data.Select(item => $"'{item.ScheduleName}' wiersze {item.Rows.Count}, elementy {item.ElementCount}")));
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
            ScheduleExcelLog.Error($"Eksport do {options.FilePath} nie powiódł się.", exception);
            TaskDialog.Show(title, $"Eksport nie powiódł się.\n\n{exception.Message}");
            return null;
        }
    }

    /// <summary>Separate method so a missing ClosedXML assembly fails inside the caller's try block, not at JIT.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteWorkbook(IReadOnlyList<ScheduleExcelData> data, ScheduleExcelExportOptions options)
    {
        ScheduleExcelWorkbookWriter.Write(data, options);
    }

    /// <summary>Reads an HCB workbook; null after showing the error to the user.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ScheduleExcelWorkbook? ReadWorkbook(string path, string title)
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

    public static string Summarize(IReadOnlyList<ScheduleExcelData> schedules, string filePath)
    {
        return $"Zapisano: {filePath}\n\n" + string.Join("\n", schedules.Select(data =>
            $"{data.ScheduleName}: wiersze {data.Rows.Count}, elementy {data.ElementCount}, "
            + $"kolumny {data.Columns.Count} (tylko do odczytu: {data.Columns.Count(column => column.IsReadOnly)})."));
    }

    public static string DescribeSkipped(IReadOnlyList<ScheduleExcelSkip> skipped, int max = 10)
    {
        if (skipped.Count == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> lines = skipped.Take(max).Select(skip =>
            $"- {(skip.Schedule.Length > 0 ? skip.Schedule + " / " : string.Empty)}{skip.Column}: {skip.Reason}");
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

    /// <summary>Schedules picked in the Project Browser or placed on the active sheet, else the open schedule view.</summary>
    private static IReadOnlyCollection<long> GetPreselectedScheduleIds(UIDocument uiDocument)
    {
        Document document = uiDocument.Document;
        var ids = new HashSet<long>();
        foreach (ElementId id in uiDocument.Selection.GetElementIds())
        {
            switch (document.GetElement(id))
            {
                case ViewSchedule schedule:
                    ids.Add(schedule.Id.Value);
                    break;
                case ScheduleSheetInstance instance:
                    ids.Add(instance.ScheduleId.Value);
                    break;
            }
        }

        if (ids.Count == 0 && document.ActiveView is ViewSchedule active)
        {
            ids.Add(active.Id.Value);
        }

        return ids;
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
