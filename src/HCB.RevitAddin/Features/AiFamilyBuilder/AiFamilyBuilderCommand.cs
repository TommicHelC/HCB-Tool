using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;
using HCB.RevitAddin.Features.AiFamilyBuilder.UI;

namespace HCB.RevitAddin.Features.AiFamilyBuilder;

[Transaction(TransactionMode.Manual)]
public sealed class AiFamilyBuilderCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        Document? document = uiDocument?.Document;
        if (document == null || !document.IsFamilyDocument)
        {
            TaskDialog.Show("AI Family Builder", "To narzedzie dziala tylko w otwartym dokumencie rodziny.");
            return Result.Succeeded;
        }

        try
        {
            AiFamilyBuilderWindow window = new();
            if (window.ShowDialog() != true)
            {
                return Result.Cancelled;
            }

            AiFamilyBuilderService service = new();
            AiFamilyBuilderResult result = service.Apply(document, commandData.Application.Application, window.Options);

            TaskDialog.Show("AI Family Builder", BuildSummary(result));
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("AI Family Builder", $"Nie udalo sie przetworzyc JSON.\n\n{ex.Message}");
            return Result.Failed;
        }
    }

    private static string BuildSummary(AiFamilyBuilderResult result)
    {
        List<string> lines =
        [
            $"Dodane parametry: {result.AddedParametersCount}",
            $"Pominiete parametry: {result.SkippedParametersCount}",
            $"Dodane typy: {result.AddedTypesCount}",
            $"Zaktualizowane wartosci: {result.UpdatedValuesCount}",
            $"Operacje geometrii: {result.ExecutedGeometryOperationsCount}",
            $"Pominiete operacje geometrii: {result.SkippedGeometryOperationsCount}"
        ];

        if (result.Messages.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Szczegoly:");
            lines.AddRange(result.Messages.Take(16));
        }

        return string.Join("\n", lines);
    }
}
