using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HCB.RevitAddin.Features.ConnectElements.Models;

namespace HCB.RevitAddin.Features.ConnectElements;

[Transaction(TransactionMode.Manual)]
public sealed class ConnectElementsCommand : IExternalCommand
{
    private const string ToolName = "Connect Elements";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null || uiDocument.Document.IsFamilyDocument)
        {
            TaskDialog.Show(ToolName, "To narzedzie wymaga otwartego projektu.");
            return Result.Succeeded;
        }

        Document document = uiDocument.Document;
        ConnectElementsService service = new();
        ConnectorElementFilter filter = new(service);
        List<string> messages = [];
        int connectedCount = 0;

        while (true)
        {
            Reference targetReference;
            Reference movingReference;
            try
            {
                targetReference = uiDocument.Selection.PickObject(
                    ObjectType.PointOnElement,
                    filter,
                    "1/2: kliknij element, DO KTOREGO chcesz podlaczyc (blisko konektora). ESC konczy.");
                movingReference = uiDocument.Selection.PickObject(
                    ObjectType.PointOnElement,
                    filter,
                    "2/2: kliknij element, KTORY ma zostac dosuniety i podlaczony (blisko konektora). ESC konczy.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }

            Element? target = document.GetElement(targetReference);
            Element? moving = document.GetElement(movingReference);
            if (target == null || moving == null)
            {
                continue;
            }

            using Transaction transaction = new(document, ToolName);
            transaction.Start();
            WarningSwallower.Attach(transaction);
            ConnectElementsResult result;
            try
            {
                result = service.Connect(document, target, targetReference.GlobalPoint, moving, movingReference.GlobalPoint);
            }
            catch (Exception ex)
            {
                result = new ConnectElementsResult
                {
                    HasChanges = false,
                    Message = $"{MepConnectorHelper.Describe(moving)}: blad laczenia. {ex.Message}"
                };
            }

            if (result.HasChanges)
            {
                transaction.Commit();
                connectedCount++;
            }
            else
            {
                transaction.RollBack();
            }

            messages.Add(result.Message);
        }

        if (messages.Count == 0)
        {
            return Result.Cancelled;
        }

        string summary = $"Polaczone pary: {connectedCount} z {messages.Count}\n\nSzczegoly:\n{string.Join("\n", messages.Take(12))}";
        TaskDialog.Show(ToolName, summary);
        return Result.Succeeded;
    }

    private sealed class ConnectorElementFilter(ConnectElementsService service) : ISelectionFilter
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
