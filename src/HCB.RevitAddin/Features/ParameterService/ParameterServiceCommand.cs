using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ParameterService.Models;

namespace HCB.RevitAddin.Features.ParameterService;

[Transaction(TransactionMode.Manual)]
public sealed class ParameterServiceCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document document = commandData.Application.ActiveUIDocument.Document;
        if (document.IsFamilyDocument)
        {
            TaskDialog.Show("Parameter Service", "Ta wersja narzedzia obsluguje tylko dokumenty projektowe.");
            return Result.Cancelled;
        }

        ParameterServiceService service = new();
        if (!service.HasConfiguredParameters)
        {
            TaskDialog.Show(
                "Parameter Service",
                "Brak predefiniowanych parametrow Parameter Service w kodzie.\n\nUzupelnij liste PredefinedParameters w ParameterServiceService.");
            return Result.Succeeded;
        }

        if (!service.HasAccountIdConfigured)
        {
            string configuredParameters = string.Join("\n", service.GetConfiguredParameterNames().Take(12));
            TaskDialog.Show(
                "Parameter Service",
                $"Brak wspolnego accountId dla Parameter Service.\n\nLista parametrow jest juz przygotowana, ale trzeba uzupelnic ParameterServiceAccountId w ParameterServiceService.\nWersja ustawiona: 1.0.0\n\nPrzygotowane parametry:\n{configuredParameters}");
            return Result.Succeeded;
        }

        ParameterServiceResolveResult resolveResult = service.ResolveConfigured(document);
        if (resolveResult.Parameters.Count == 0)
        {
            TaskDialog.Show("Parameter Service", BuildResolveSummary(resolveResult));
            return Result.Succeeded;
        }

        List<ParameterServiceDescriptor> importableParameters = resolveResult.Parameters
            .Where(parameter => !parameter.ExistsInDocument)
            .ToList();

        if (importableParameters.Count == 0)
        {
            TaskDialog.Show("Parameter Service", BuildAlreadyLoadedSummary(resolveResult));
            return Result.Succeeded;
        }

        ParameterServiceImportResult importResult = service.Import(
            document,
            importableParameters.Select(parameter => parameter.ParameterTypeId));

        TaskDialog.Show("Parameter Service", BuildImportSummary(resolveResult, importResult));
        return Result.Succeeded;
    }

    private static string BuildResolveSummary(ParameterServiceResolveResult resolveResult)
    {
        List<string> lines =
        [
            "Nie udalo sie pobrac zadnych parametrow z Parameter Service.",
            string.Empty
        ];

        lines.AddRange(resolveResult.Errors.Take(12));
        return string.Join("\n", lines);
    }

    private static string BuildAlreadyLoadedSummary(ParameterServiceResolveResult resolveResult)
    {
        int existingCount = resolveResult.Parameters.Count(parameter => parameter.ExistsInDocument);
        List<string> lines =
        [
            $"Wszystkie predefiniowane parametry sa juz w projekcie: {existingCount}.",
            $"Bledy odczytu: {resolveResult.Errors.Count}"
        ];

        if (resolveResult.Errors.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(resolveResult.Errors.Take(10));
        }

        return string.Join("\n", lines);
    }

    private static string BuildImportSummary(ParameterServiceResolveResult resolveResult, ParameterServiceImportResult importResult)
    {
        int existingCount = resolveResult.Parameters.Count(parameter => parameter.ExistsInDocument);
        List<string> lines =
        [
            $"Predefiniowane: {resolveResult.Parameters.Count}",
            $"Juz w projekcie: {existingCount}",
            $"Dodane: {importResult.ImportedCount}",
            $"Pominiete: {importResult.SkippedCount}",
            $"Bledy importu: {importResult.FailedCount}",
            $"Bledy odczytu: {resolveResult.Errors.Count}"
        ];

        IEnumerable<string> details = importResult.Messages
            .Concat(resolveResult.Errors.Select(error => $"Odczyt: {error}"))
            .Take(12);

        if (details.Any())
        {
            lines.Add(string.Empty);
            lines.AddRange(details);
        }

        return string.Join("\n", lines);
    }
}
