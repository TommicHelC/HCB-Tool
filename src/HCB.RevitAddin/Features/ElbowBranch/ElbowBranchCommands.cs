using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HCB.RevitAddin.Features.ElbowBranch.Models;

namespace HCB.RevitAddin.Features.ElbowBranch;

[Transaction(TransactionMode.Manual)]
public sealed class ElbowUpCommand : ElbowBranchCommandBase
{
    protected override ElbowBranchDirection Direction => ElbowBranchDirection.Up;

    protected override string ToolName => "Elbow Up";

    protected override string Prompt => "Kliknij element blisko wolnego konektora - kolano + 500 mm w gore (Z). ESC konczy.";
}

[Transaction(TransactionMode.Manual)]
public sealed class Elbow45Command : ElbowBranchCommandBase
{
    protected override ElbowBranchDirection Direction => ElbowBranchDirection.Side45;

    protected override string ToolName => "Elbow 45";

    protected override string Prompt => "Kliknij element blisko wolnego konektora - kolano 45 st. + 500 mm w prawo. ESC konczy.";
}

public abstract class ElbowBranchCommandBase : IExternalCommand
{
    protected abstract ElbowBranchDirection Direction { get; }

    protected abstract string ToolName { get; }

    protected abstract string Prompt { get; }

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null || uiDocument.Document.IsFamilyDocument)
        {
            TaskDialog.Show(ToolName, "To narzedzie wymaga otwartego projektu.");
            return Result.Succeeded;
        }

        Document document = uiDocument.Document;
        ElbowBranchService service = new();
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
                    Prompt);
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
            WarningSwallower.Attach(transaction);
            ElbowBranchResult result;
            try
            {
                result = service.Add(document, uiDocument.ActiveView, element, reference.GlobalPoint, Direction);
            }
            catch (Exception ex)
            {
                result = new ElbowBranchResult
                {
                    HasChanges = false,
                    Message = $"{MepConnectorHelper.Describe(element)}: blad wstawiania kolana. {ex.Message}"
                };
            }

            if (result.HasChanges && transaction.Commit() == TransactionStatus.Committed)
            {
                createdIds.AddRange(result.CreatedIds);
                continue;
            }

            if (transaction.HasStarted() && !transaction.HasEnded())
            {
                transaction.RollBack();
            }

            errors.Add(result.HasChanges
                ? $"{MepConnectorHelper.Describe(element)}: Revit odrzucil zmiane."
                : result.Message);
        }

        if (createdIds.Count > 0)
        {
            uiDocument.Selection.SetElementIds(createdIds.Where(id => document.GetElement(id) != null).ToList());
        }

        // Quick tool: stay silent on success, report only what failed.
        if (errors.Count > 0)
        {
            TaskDialog.Show(ToolName, $"Nie dodano kolan: {errors.Count}\n\n{string.Join("\n", errors.Take(12))}");
        }

        return Result.Succeeded;
    }

    private sealed class FreeConnectorFilter(ElbowBranchService service) : ISelectionFilter
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
