using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ParameterCopy.Models;
using HCB.RevitAddin.Features.ParameterCopy.UI;

namespace HCB.RevitAddin.Features.ParameterCopy;

[Transaction(TransactionMode.Manual)]
public sealed class ParameterCopyCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null)
        {
            TaskDialog.Show("Copy Parameter", "To narzedzie wymaga otwartego projektu.");
            return Result.Succeeded;
        }

        try
        {
            Document document = uiDocument.Document;
            ParameterCopyService service = new();
            IReadOnlyList<Element> allElements = service.GetAllSupportedElements(document);
            if (allElements.Count == 0)
            {
                TaskDialog.Show("Copy Parameter", "Nie znaleziono obslugiwanych elementow instalacji w modelu.");
                return Result.Succeeded;
            }

            ParameterCopyCatalog allCatalog = service.BuildCatalog(allElements);
            if (allCatalog.SourceParameterNames.Count == 0)
            {
                TaskDialog.Show("Copy Parameter", "Nie znaleziono zgodnych parametrow do kopiowania na obslugiwanych elementach instalacji.");
                return Result.Succeeded;
            }

            IReadOnlyList<Element> selectedElements = service.GetSelectedSupportedElements(document, uiDocument.Selection.GetElementIds());
            ParameterCopyCatalog? selectedCatalog = null;
            if (selectedElements.Count > 0)
            {
                ParameterCopyCatalog candidateCatalog = service.BuildCatalog(selectedElements);
                if (candidateCatalog.SourceParameterNames.Count > 0)
                {
                    selectedCatalog = candidateCatalog;
                }
            }

            ParameterCopyWindow window = new(allCatalog, selectedCatalog);
            if (window.ShowDialog() != true)
            {
                return Result.Cancelled;
            }

            ParameterCopyOptions options = window.Options;
            IReadOnlyList<Element> scopeElements = options.Scope == ParameterCopyScope.SelectedEquipment && selectedCatalog != null
                ? selectedElements
                : allElements;

            ParameterCopyResult preview = service.Preview(scopeElements, options);
            if (preview.CopiedCount == 0)
            {
                TaskDialog.Show("Copy Parameter", "Nie znaleziono elementow, na ktorych mozna skopiowac wartosc dla wybranych parametrow.");
                return Result.Succeeded;
            }

            if (!ConfirmCopy(options, preview))
            {
                return Result.Cancelled;
            }

            ParameterCopyResult result = service.Apply(document, scopeElements, options);
            TaskDialog.Show(
                "Copy Parameter",
                $"Tryb: {GetScopeLabel(options)}\n" +
                $"Elementy w zakresie: {result.ScopedElementCount}\n" +
                $"Skopiowane: {result.CopiedCount}\n" +
                $"Brak parametru zrodlowego: {result.MissingSourceCount}\n" +
                $"Brak parametru docelowego: {result.MissingTargetCount}\n" +
                $"Docelowy tylko do odczytu: {result.ReadOnlyTargetCount}\n" +
                $"Puste wartosci zrodlowe: {result.EmptySourceCount}\n" +
                $"Niezgodny typ danych: {result.IncompatibleTypeCount}\n" +
                $"Nieudany zapis: {result.FailedSetCount}\n" +
                $"Pominiete lacznie: {result.SkippedCount}");

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Copy Parameter", $"Nie udalo sie skopiowac wartosci parametrow.\n\n{ex}");
            return Result.Failed;
        }
    }

    private static bool ConfirmCopy(ParameterCopyOptions options, ParameterCopyResult preview)
    {
        TaskDialog dialog = new("Copy Parameter")
        {
            MainInstruction = "Potwierdz kopiowanie wartosci parametrow",
            MainContent =
                $"Parametr zrodlowy: {options.SourceParameterName}\n" +
                $"Parametr docelowy: {options.TargetParameterName}\n" +
                $"Tryb: {GetScopeLabel(options)}\n" +
                $"Elementy w zakresie: {preview.ScopedElementCount}\n" +
                $"Mozliwe do skopiowania: {preview.CopiedCount}\n" +
                $"Puste wartosci zrodlowe: {preview.EmptySourceCount}\n" +
                $"Brak parametru docelowego: {preview.MissingTargetCount}\n" +
                $"Niezgodny typ danych: {preview.IncompatibleTypeCount}",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No,
            VerificationText = "Rozumiem, ze operacja nadpisze wartosci parametru docelowego."
        };

        return dialog.Show() == TaskDialogResult.Yes;
    }

    private static string GetScopeLabel(ParameterCopyOptions options)
    {
        return options.Scope == ParameterCopyScope.SelectedEquipment
            ? "Zaznaczone elementy instalacji"
            : "Wszystkie elementy instalacji";
    }
}
