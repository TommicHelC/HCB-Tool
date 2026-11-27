using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ScheduleExcel.Models;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>
/// Exports a schedule, opens it in Excel and starts a live session: every Ctrl+S in Excel writes the
/// user's edits back to Revit (see <see cref="ScheduleExcelSyncSession"/>).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ScheduleExcelSyncCommand : IExternalCommand
{
    private const string Title = "Synchronizacja zestawienia z Excel";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication application = commandData.Application;
        UIDocument? uiDocument = application.ActiveUIDocument;
        if (uiDocument?.Document == null || uiDocument.Document.IsFamilyDocument)
        {
            TaskDialog.Show(Title, "Otwórz projekt z zestawieniami.");
            return Result.Cancelled;
        }

        Document document = uiDocument.Document;
        if (document.IsReadOnly)
        {
            TaskDialog.Show(Title, "Model jest tylko do odczytu - zmian z Excel nie da się zapisać.");
            return Result.Cancelled;
        }

        var picked = ScheduleExcelWorkflow.PickSchedule(
            uiDocument,
            Title,
            "Otwiera zestawienie w Excel. Każde zapisanie pliku (Ctrl+S) przenosi zmiany do Revit.",
            "Start");
        if (picked == null)
        {
            return Result.Cancelled;
        }

        (ViewSchedule schedule, ScheduleExcelExportOptions options) = picked.Value;

        // The file is rewritten on start, so an old session on it must end first.
        ScheduleExcelSyncManager.Find(options.FilePath)?.Stop("nowa sesja dla tego pliku");

        ScheduleExcelData? data = ScheduleExcelWorkflow.Export(document, schedule, options, Title);
        if (data == null)
        {
            return Result.Failed;
        }

        ScheduleExcelWorkbookContent? content = ScheduleExcelWorkflow.ReadWorkbook(options.FilePath, Title);
        if (content == null)
        {
            return Result.Failed;
        }

        ScheduleExcelSyncSession session;
        try
        {
            session = ScheduleExcelSyncSession.Start(document, options.FilePath, schedule.Name, content);
            ScheduleExcelSyncManager.Add(application, session);
        }
        catch (Exception exception)
        {
            ScheduleExcelLog.Error("Start sesji synchronizacji nie powiódł się.", exception);
            TaskDialog.Show(Title, $"Nie udało się uruchomić synchronizacji.\n\n{exception.Message}");
            return Result.Failed;
        }

        try
        {
            if (document.ActiveView?.Id != schedule.Id)
            {
                uiDocument.RequestViewChange(schedule);
            }
        }
        catch (Exception exception)
        {
            ScheduleExcelLog.Error("Aktywacja widoku zestawienia nie powiodła się.", exception);
        }

        ScheduleExcelWorkflow.OpenFile(options.FilePath, Title);
        return Result.Succeeded;
    }
}
