using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ScheduleExcel.Models;

namespace HCB.RevitAddin.Features.ScheduleExcel;

[Transaction(TransactionMode.Manual)] // GetTableData() throws ModificationForbidden in ReadOnly mode.
public sealed class ScheduleExcelExportCommand : IExternalCommand
{
    private const string Title = "Eksport zestawień do Excel";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null || uiDocument.Document.IsFamilyDocument)
        {
            TaskDialog.Show(Title, "Otwórz projekt z zestawieniami.");
            return Result.Cancelled;
        }

        var picked = ScheduleExcelWorkflow.PickSchedules(
            uiDocument,
            UI.ScheduleExcelWindowMode.Export,
            Title,
            "Zapisuje zestawienia do Excel (.xlsx): do jednego pliku z arkuszem na zestawienie albo do osobnych plików.",
            "Eksportuj");
        if (picked == null)
        {
            return Result.Cancelled;
        }

        (IReadOnlyList<ViewSchedule> schedules, ScheduleExcelExportOptions options, IReadOnlyList<string> paths) = picked.Value;

        // One workbook with a sheet per schedule, or one workbook per schedule.
        var files = new List<(string Path, IReadOnlyList<ScheduleExcelData> Data)>();
        IReadOnlyList<IReadOnlyList<ViewSchedule>> batches = options.SeparateFiles
            ? schedules.Select(schedule => (IReadOnlyList<ViewSchedule>)new[] { schedule }).ToList()
            : [schedules];
        for (int index = 0; index < batches.Count; index++)
        {
            options.FilePath = paths[index];
            IReadOnlyList<ScheduleExcelData>? data = ScheduleExcelWorkflow.Export(uiDocument.Document, batches[index], options, Title);
            if (data == null)
            {
                return files.Count > 0 ? Result.Succeeded : Result.Failed;
            }

            files.Add((paths[index], data));
        }

        if (options.OpenAfterExport && files.Count <= 3)
        {
            foreach ((string path, _) in files)
            {
                ScheduleExcelWorkflow.OpenFile(path, Title);
            }
        }
        else if (options.OpenAfterExport)
        {
            // Many files: open the folder instead of a stack of Excel windows.
            ScheduleExcelWorkflow.OpenFile(Path.GetDirectoryName(files[0].Path)!, Title);
        }
        else
        {
            TaskDialog.Show(Title, string.Join("\n\n", files.Select(file => ScheduleExcelWorkflow.Summarize(file.Data, file.Path))));
        }

        return Result.Succeeded;
    }
}
