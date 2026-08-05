using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ParameterClear.Models;
using HCB.RevitAddin.Features.ParameterClear.UI;

namespace HCB.RevitAddin.Features.ParameterClear;

[Transaction(TransactionMode.Manual)]
public sealed class ParameterClearCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null)
        {
            TaskDialog.Show("Clear Parameter", "To narzedzie wymaga otwartego projektu.");
            return Result.Succeeded;
        }

        try
        {
            Document document = uiDocument.Document;
            ICollection<ElementId> selectedElementIds = uiDocument.Selection.GetElementIds();
            ParameterClearService service = new();

            ParameterClearWindow window;
            if (selectedElementIds.Count > 0)
            {
                IReadOnlyList<Element> selectedElements = service.GetSelectedSupportedElements(document, selectedElementIds);
                IReadOnlyList<string> parameterNames = service.GetWritableParameterNames(selectedElements);
                if (parameterNames.Count == 0)
                {
                    TaskDialog.Show("Clear Parameter", "Zaznaczone elementy nie maja zapisywalnych parametrow.");
                    return Result.Succeeded;
                }

                window = new ParameterClearWindow(parameterNames);
            }
            else
            {
                ParameterClearCatalog catalog = service.BuildCatalog(document);
                if (catalog.ModelParameterNames.Count == 0)
                {
                    TaskDialog.Show("Clear Parameter", "Nie znaleziono w modelu zapisywalnych parametrow.");
                    return Result.Succeeded;
                }

                window = new ParameterClearWindow(catalog);
            }

            if (window.ShowDialog() != true)
            {
                return Result.Cancelled;
            }

            ParameterClearOptions options = window.Options;
            ParameterClearResult preview = service.Preview(document, selectedElementIds, options);
            if (preview.TargetCount == 0)
            {
                TaskDialog.Show("Clear Parameter", "Nie znaleziono elementow z wybranym parametrem w zadanym zakresie.");
                return Result.Succeeded;
            }

            if (!ConfirmClear(options, preview))
            {
                return Result.Cancelled;
            }

            ParameterClearResult result = service.Apply(document, selectedElementIds, options);
            TaskDialog.Show(
                "Clear Parameter",
                $"Tryb: {GetScopeLabel(options)}\nElementy w zakresie: {result.ScopedElementCount}\nZnalezione z parametrem: {result.TargetCount}\nWyczyszczone: {result.ClearedCount}\nJuz puste: {result.AlreadyEmptyCount}\nPominiete: {result.SkippedCount}");

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Clear Parameter", $"Nie udalo sie wyczyscic parametrow.\n\n{ex}");
            return Result.Failed;
        }
    }

    private static bool ConfirmClear(ParameterClearOptions options, ParameterClearResult preview)
    {
        TaskDialog dialog = new("Clear Parameter")
        {
            MainInstruction = "Potwierdz wyczyszczenie parametrow",
            MainContent =
                $"Parametr: {options.ParameterName}\n" +
                $"Tryb: {GetScopeLabel(options)}\n" +
                $"Elementy w zakresie: {preview.ScopedElementCount}\n" +
                $"Elementy z parametrem: {preview.TargetCount}\n" +
                $"Wartosci do wyczyszczenia: {preview.ClearedCount}\n" +
                $"Juz puste: {preview.AlreadyEmptyCount}",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No,
            VerificationText = "Rozumiem, ze ta operacja wyczysci wartosci wybranego parametru (tekst -> puste, liczby -> 0)."
        };

        return dialog.Show() == TaskDialogResult.Yes;
    }

    private static string GetScopeLabel(ParameterClearOptions options)
    {
        return options.Scope switch
        {
            ParameterClearScope.Selection => "Zaznaczone elementy",
            ParameterClearScope.Category => $"Kategoria: {options.CategoryName}",
            ParameterClearScope.Model => "Caly model",
            _ => "Nieznany zakres"
        };
    }
}
