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
            Title,
            "Zapisuje zestawienia do pliku .xlsx (każde w osobnym arkuszu) razem z informacją, które elementy tworzą każdy wiersz.",
            "Eksportuj");
        if (picked == null)
        {
            return Result.Cancelled;
        }

        (IReadOnlyList<ViewSchedule> schedules, ScheduleExcelExportOptions options) = picked.Value;
        IReadOnlyList<ScheduleExcelData>? data = ScheduleExcelWorkflow.Export(uiDocument.Document, schedules, options, Title);
        if (data == null)
        {
            return Result.Failed;
        }

        if (options.OpenAfterExport)
        {
            ScheduleExcelWorkflow.OpenFile(options.FilePath, Title);
        }
        else
        {
            TaskDialog.Show(Title, ScheduleExcelWorkflow.Summarize(data, options.FilePath));
        }

        return Result.Succeeded;
    }
}
