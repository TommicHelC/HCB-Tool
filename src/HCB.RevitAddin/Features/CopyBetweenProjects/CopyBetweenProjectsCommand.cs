using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.CopyBetweenProjects.UI;

namespace HCB.RevitAddin.Features.CopyBetweenProjects;

[Transaction(TransactionMode.Manual)]
public sealed class CopyBetweenProjectsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            CopyBetweenProjectsService service = new();
            IReadOnlyList<Document> documents = service.GetAvailableProjectDocuments(commandData.Application.Application);

            if (documents.Count < 2)
            {
                TaskDialog.Show("Copy Beetwen Projects", "Potrzebne sa co najmniej dwa otwarte projekty.");
                return Result.Succeeded;
            }

            CopyBetweenProjectsWindow window = new(documents, service);
            if (window.ShowDialog() != true || window.SourceDocument == null || window.TargetDocument == null)
            {
                return Result.Cancelled;
            }

            var result = service.Copy(window.SourceDocument, window.TargetDocument, window.SelectedItems);
            string details = string.Join("\n", result.Messages.Take(20));
            TaskDialog.Show(
                "Copy Beetwen Projects",
                $"Projekt zrodlowy: {result.SourceDocumentTitle}\nProjekt docelowy: {result.TargetDocumentTitle}\nWybrane pozycje: {result.RequestedItemsCount}\nSkopiowane identyfikatory: {result.CopiedElementIdsCount}\n\n{details}");

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Copy Beetwen Projects", $"Nie udalo sie skopiowac elementow.\n\n{ex}");
            return Result.Failed;
        }
    }
}
