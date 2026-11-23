using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HCB.RevitAddin.Features.AlignToAxis.Models;

namespace HCB.RevitAddin.Features.AlignToAxis;

[Transaction(TransactionMode.Manual)]
public sealed class AlignToAxisCommand : IExternalCommand
{
    private const string ToolName = "Align To Axis";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null || uiDocument.Document.IsFamilyDocument)
        {
            TaskDialog.Show(ToolName, "To narzedzie wymaga otwartego projektu.");
            return Result.Succeeded;
        }

        Document document = uiDocument.Document;
        AlignToAxisService service = new();

        Reference referencePick;
        try
        {
            referencePick = uiDocument.Selection.PickObject(
                ObjectType.Element,
                new DelegateFilter(service.IsReference),
                "1: kliknij prosty odcinek rury lub kanalu - os odniesienia. ESC konczy.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        Element? referenceElement = document.GetElement(referencePick);
        if (referenceElement == null || !service.TryGetLine(referenceElement, out Line? referenceLine))
        {
            return Result.Cancelled;
        }

        List<string> errors = [];
        while (true)
        {
            Reference targetPick;
            try
            {
                targetPick = uiDocument.Selection.PickObject(
                    ObjectType.PointOnElement,
                    new DelegateFilter(element => element.Id != referenceElement.Id && service.IsAlignable(element)),
                    "2: klikaj elementy do wyrownania do osi odniesienia. ESC konczy.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }

            Element? target = document.GetElement(targetPick);
            if (target == null)
            {
                continue;
            }

            using Transaction transaction = new(document, ToolName);
            transaction.Start();
            WarningSwallower.Attach(transaction);
            AlignToAxisResult result;
            try
            {
                result = service.Align(document, referenceLine, target, targetPick.GlobalPoint);
            }
            catch (Exception ex)
            {
                result = new AlignToAxisResult
                {
                    IsError = true,
                    Message = $"{MepConnectorHelper.Describe(target)}: blad wyrownania. {ex.Message}"
                };
            }

            if (!result.HasChanges)
            {
                transaction.RollBack();
            }
            else if (transaction.Commit() != TransactionStatus.Committed)
            {
                result = new AlignToAxisResult
                {
                    IsError = true,
                    Message = $"{MepConnectorHelper.Describe(target)}: Revit odrzucil przesuniecie (polaczenia nie pozwalaja)."
                };
            }

            if (result.IsError)
            {
                errors.Add(result.Message);
            }
        }

        // Quick tool: stay silent on success, report only what failed.
        if (errors.Count > 0)
        {
            TaskDialog.Show(ToolName, $"Nie wyrownano: {errors.Count}\n\n{string.Join("\n", errors.Take(12))}");
        }

        return Result.Succeeded;
    }

    private sealed class DelegateFilter(Func<Element, bool> allow) : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return allow(element);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
