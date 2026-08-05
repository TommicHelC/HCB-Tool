using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.AccessoryTerminalNumbering.Models;
using HCB.RevitAddin.Features.AccessoryTerminalNumbering.UI;

namespace HCB.RevitAddin.Features.AccessoryTerminalNumbering;

[Transaction(TransactionMode.Manual)]
public sealed class AccessoryTerminalNumberingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null)
        {
            TaskDialog.Show("Duct Accessory Numbering", "To narzedzie wymaga otwartego projektu.");
            return Result.Succeeded;
        }

        try
        {
            Document document = uiDocument.Document;
            bool hasSelection = uiDocument.Selection.GetElementIds().Count > 0;
            string scopeLabel = hasSelection ? "Zaznaczenie" : "Caly model";
            AccessoryTerminalNumberingService service = new();

            var targets = service.CollectTargets(document, uiDocument.Selection.GetElementIds());
            if (targets.Count == 0)
            {
                TaskDialog.Show("Duct Accessory Numbering", "Brak Duct Accessory do numeracji.");
                return Result.Succeeded;
            }

            var availableTargetParameters = service.GetWritableStringTargetParameters(targets);
            if (availableTargetParameters.Count == 0)
            {
                TaskDialog.Show("Duct Accessory Numbering", "Brak zapisywalnych parametrow tekstowych dla wybranych elementow.");
                return Result.Succeeded;
            }

            AccessoryTerminalNumberingWindow window = new(availableTargetParameters);

            if (window.ShowDialog() != true)
            {
                return Result.Cancelled;
            }

            AccessoryTerminalNumberingResult result = service.Apply(document, targets, window.Options);
            TaskDialog.Show(
                "Duct Accessory Numbering",
                $"Tryb: {scopeLabel}\nPonumerowano: {result.TotalCount}\nDuct Accessory: {result.DuctAccessoryCount}\nWspoldzielony numer: {result.SharedNumberCount}\nHC_System: {result.SystemsCount}\nParametr docelowy: {result.TargetParameterName}");

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Duct Accessory Numbering", $"Nie udalo sie wykonac numeracji.\n\n{ex}");
            return Result.Failed;
        }
    }
}

