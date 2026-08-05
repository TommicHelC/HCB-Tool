using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;
using HCB.RevitAddin.Features.AiFamilyBuilder.UI;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3;

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

            Application application = commandData.Application.Application;
            AiFamilyBuilderResult result = AiFamilyBuilderSchemaDetector.IsV3(window.Options.JsonText)
                ? new AiFamilyBuilderV3Service().Apply(document, application, window.Options)
                : new AiFamilyBuilderService().Apply(document, application, window.Options);

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
            $"Reference planes: {result.CreatedReferencePlanesCount}",
            $"Pominiete reference planes: {result.SkippedReferencePlanesCount}",
            $"Constraints: {result.CreatedDimensionsCount}",
            $"Pominiete constraints: {result.SkippedDimensionsCount}",
            $"Operacje geometrii: {result.ExecutedGeometryOperationsCount}",
            $"Pominiete operacje geometrii: {result.SkippedGeometryOperationsCount}"
        ];

        if (result.Messages.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Szczegoly:");
            lines.AddRange(result.Messages);
        }

        return string.Join("\n", lines);
    }
}
