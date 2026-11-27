using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ScheduleExcel.Models;

namespace HCB.RevitAddin.Features.ScheduleExcel;

[Transaction(TransactionMode.Manual)] // GetTableData() throws ModificationForbidden in ReadOnly mode.
public sealed class ScheduleExcelExportCommand : IExternalCommand
{
    private const string Title = "Eksport zestawienia do Excel";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null || uiDocument.Document.IsFamilyDocument)
        {
            TaskDialog.Show(Title, "Otwórz projekt z zestawieniami.");
            return Result.Cancelled;
        }

        var picked = ScheduleExcelWorkflow.PickSchedule(
            uiDocument,
            Title,
            "Zapisuje dane zestawienia do pliku .xlsx razem z informacją, które elementy tworzą każdy wiersz.",
            "Eksportuj");
        if (picked == null)
        {
            return Result.Cancelled;
        }

        (ViewSchedule schedule, ScheduleExcelExportOptions options) = picked.Value;
        ScheduleExcelData? data = ScheduleExcelWorkflow.Export(uiDocument.Document, schedule, options, Title);
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
