using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HCB.RevitAddin.Features.ConnectorExtend.Models;

namespace HCB.RevitAddin.Features.ConnectorExtend;

[Transaction(TransactionMode.Manual)]
public sealed class ConnectorExtendCommand : IExternalCommand
{
    private const string ToolName = "Extend From Connector";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null || uiDocument.Document.IsFamilyDocument)
        {
            TaskDialog.Show(ToolName, "To narzedzie wymaga otwartego projektu.");
            return Result.Succeeded;
        }

        Document document = uiDocument.Document;
        ConnectorExtendService service = new();
        List<string> errors = [];
        List<ElementId> createdIds = [];

        while (true)
        {
            Reference reference;
            try
            {
                reference = uiDocument.Selection.PickObject(
                    ObjectType.PointOnElement,
                    new FreeConnectorFilter(service),
                    "Kliknij element blisko wolnego konektora - wstawi sie odcinek 500 mm. ESC konczy.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }

            Element? element = document.GetElement(reference);
            if (element == null)
            {
                continue;
            }

            using Transaction transaction = new(document, ToolName);
            transaction.Start();
            ConnectorExtendResult result;
            try
            {
                result = service.Extend(document, uiDocument.ActiveView, element, reference.GlobalPoint, ConnectorExtendService.DefaultLengthInternal);
            }
            catch (Exception ex)
            {
                result = new ConnectorExtendResult
                {
                    HasChanges = false,
                    Message = $"{MepConnectorHelper.Describe(element)}: blad wstawiania. {ex.Message}"
                };
            }

            if (result.HasChanges && result.CreatedId != null)
            {
                transaction.Commit();
                createdIds.Add(result.CreatedId);
            }
            else
            {
                transaction.RollBack();
                errors.Add(result.Message);
            }
        }

        if (createdIds.Count > 0)
        {
            uiDocument.Selection.SetElementIds(createdIds);
        }

        // Quick tool: stay silent on success, report only what failed.
        if (errors.Count > 0)
        {
            TaskDialog.Show(ToolName, $"Nie wstawiono odcinkow: {errors.Count}\n\n{string.Join("\n", errors.Take(12))}");
        }

        return createdIds.Count > 0 || errors.Count > 0 ? Result.Succeeded : Result.Cancelled;
    }

    private sealed class FreeConnectorFilter(ConnectorExtendService service) : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return service.IsSupported(element);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
