using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using HCB.RevitAddin.Features.RotateFittings.Models;
using HCB.RevitAddin.Features.RotateFittings.UI;

namespace HCB.RevitAddin.Features.RotateFittings;

/// <summary>
/// Runs the click-to-rotate loop inside the Revit API context, raised from the modeless window.
/// </summary>
public sealed class RotateFittingsPickHandler : IExternalEventHandler
{
    private readonly RotateFittingsService service = new();

    public RotateFittingsWindow? Window { get; set; }

    public bool IsPicking { get; private set; }

    public void Execute(UIApplication app)
    {
        RotateFittingsWindow? window = Window;
        if (window == null || window.IsClosed || IsPicking)
        {
            return;
        }

        UIDocument? uiDocument = app.ActiveUIDocument;
        if (uiDocument?.Document == null || uiDocument.Document.IsFamilyDocument)
        {
            window.SetStatus("Otworz widok projektu, aby obracac ksztaltki.");
            return;
        }

        Document document = uiDocument.Document;
        IsPicking = true;
        try
        {
            while (!window.IsClosed)
            {
                if (!window.TryBuildOptions(out RotateFittingsOptions? options, out string error))
                {
                    window.SetStatus(error);
                    return;
                }

                Reference reference;
                try
                {
                    reference = uiDocument.Selection.PickObject(
                        ObjectType.PointOnElement,
                        new FittingSelectionFilter(service, options),
                        $"Rotate Fittings: klik = obrot o {options.AngleDegrees:0.##} st. ESC wstrzymuje.");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    window.SetStatus("Wstrzymano. Kliknij kat albo 'Wskaz ksztaltki', aby kontynuowac.");
                    return;
                }

                // Settings may have changed in the window while Revit waited for the click.
                if (window.IsClosed || !window.TryBuildOptions(out options, out error))
                {
                    return;
                }

                Element? element = document.GetElement(reference);
                if (element == null)
                {
                    continue;
                }

                using Transaction transaction = new(document, "Rotate Fittings");
                transaction.Start();
                RotateFittingsResult result;
                try
                {
                    result = service.Rotate(document, element, reference.GlobalPoint, options);
                }
                catch (Exception ex)
                {
                    result = new RotateFittingsResult
                    {
                        HasChanges = false,
                        ElementId = element.Id.Value,
                        Message = $"Element {element.Id.Value}: blad obrotu. {ex.Message}"
                    };
                }

                if (result.HasChanges)
                {
                    transaction.Commit();
                }
                else
                {
                    transaction.RollBack();
                }

                window.SetStatus(result.Message);
            }
        }
        catch (Exception ex)
        {
            if (!window.IsClosed)
            {
                window.SetStatus($"Przerwano: {ex.Message}");
            }
        }
        finally
        {
            IsPicking = false;
        }
    }

    public string GetName()
    {
        return "HCB Rotate Fittings";
    }

    private sealed class FittingSelectionFilter(RotateFittingsService service, RotateFittingsOptions options) : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return service.IsSupported(element, options);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
