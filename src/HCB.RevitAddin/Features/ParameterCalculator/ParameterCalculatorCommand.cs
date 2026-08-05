using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ParameterCalculator.Models;
using HCB.RevitAddin.Features.ParameterCalculator.UI;

namespace HCB.RevitAddin.Features.ParameterCalculator;

[Transaction(TransactionMode.Manual)]
public sealed class ParameterCalculatorCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null)
        {
            TaskDialog.Show("Parameter Calculator", "To narzedzie wymaga otwartego projektu.");
            return Result.Succeeded;
        }

        try
        {
            Document document = uiDocument.Document;
            ParameterCalculatorService service = new();
            IReadOnlyList<Element> baseElements = service.GetBaseElements(document, uiDocument.Selection.GetElementIds());
            if (baseElements.Count == 0)
            {
                TaskDialog.Show("Parameter Calculator", "Nie znaleziono zadnych obslugiwanych elementow do obliczen.");
                return Result.Succeeded;
            }

            IReadOnlyList<string> availableParameters = service.GetNumericParameterNames(baseElements);
            IReadOnlyList<string> writableTargetParameters = service.GetWritableNumericTargetParameterNames(baseElements);
            if (availableParameters.Count < 2 || writableTargetParameters.Count == 0)
            {
                TaskDialog.Show("Parameter Calculator", "Nie znaleziono wystarczajacej liczby parametrow liczbowych dla wybranego zakresu.");
                return Result.Succeeded;
            }

            ParameterCalculatorWindow window = new(availableParameters, writableTargetParameters, uiDocument.Selection.GetElementIds().Count > 0);
            if (window.ShowDialog() != true)
            {
                return Result.Cancelled;
            }

            ParameterCalculatorOptions options = window.Options;
            ParameterCalculatorResult result = service.Apply(document, baseElements, options);
            string scopeText = uiDocument.Selection.GetElementIds().Count > 0 ? "Zaznaczenie" : "Caly model";
            TaskDialog.Show(
                "Parameter Calculator",
                $"Tryb: {scopeText}\nZaktualizowane: {result.UpdatedCount}\nPominiete: {result.SkippedCount}");

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Parameter Calculator", $"Nie udalo sie wykonac obliczen.\n\n{ex}");
            return Result.Failed;
        }
    }
}
