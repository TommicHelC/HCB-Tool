using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.VelocitySizer.Models;
using HCB.RevitAddin.Features.VelocitySizer.UI;

namespace HCB.RevitAddin.Features.VelocitySizer;

[Transaction(TransactionMode.Manual)]
public sealed class VelocitySizerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null)
        {
            TaskDialog.Show("Velocity Sizer", "To narzedzie wymaga otwartego projektu.");
            return Result.Succeeded;
        }

        if (uiDocument.Selection.GetElementIds().Count == 0)
        {
            TaskDialog.Show("Velocity Sizer", "Zaznacz okragle kanaly lub rury, ktore chcesz przeliczyc.");
            return Result.Succeeded;
        }

        try
        {
            Document document = uiDocument.Document;
            VelocitySizerService service = new();
            IReadOnlyList<VelocitySizerSelectionItem> items = service.BuildSelectionItems(document, uiDocument.Selection.GetElementIds(), out List<string> messages);
            if (items.Count == 0)
            {
                string details = messages.Count == 0
                    ? "Nie znaleziono obslugiwanych elementow."
                    : string.Join("\n", messages.Take(12));
                TaskDialog.Show("Velocity Sizer", details);
                return Result.Succeeded;
            }

            VelocitySizerWindow window = new(items, messages, elementId =>
            {
                ElementId id = new(elementId);
                if (document.GetElement(id) == null)
                {
                    return;
                }

                uiDocument.Selection.SetElementIds(new List<ElementId> { id });
                uiDocument.ShowElements(id);
            });
            if (window.ShowDialog() != true)
            {
                return Result.Cancelled;
            }

            // zapisujemy tylko pozycje gotowe (przeplyw + wybrany rozmiar); reszte pomijamy
            List<VelocitySizerSelectionItem> itemsToApply = window.SelectionItems
                .Where(item => item.SelectedSize != null && !string.IsNullOrWhiteSpace(item.FlowText))
                .ToList();
            if (itemsToApply.Count == 0)
            {
                return Result.Cancelled;
            }

            bool overwriteExisting = CanSkipOverwritePrompt(itemsToApply);
            if (!overwriteExisting && service.HasAnyExistingTargetValues(itemsToApply))
            {
                TaskDialogResult dialogResult = ShowSkipExistingDialog();
                if (dialogResult == TaskDialogResult.Cancel)
                {
                    return Result.Cancelled;
                }

                overwriteExisting = dialogResult == TaskDialogResult.CommandLink1;
            }

            bool changeGeometry = window.CommitMode == VelocitySizerCommitMode.ApplyWithGeometry;
            VelocitySizerResult result = service.Apply(document, itemsToApply, overwriteExisting, changeGeometry);

            int notReady = window.SelectionItems.Count - itemsToApply.Count;
            ShowSummary(result, window.SelectionItems.Count, notReady, changeGeometry);

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Velocity Sizer", $"Nie udalo sie wykonac doboru predkosci.\n\n{ex}");
            return Result.Failed;
        }
    }

    private static void ShowSummary(VelocitySizerResult result, int selectedCount, int notReadyCount, bool changeGeometry)
    {
        string mode = changeGeometry ? "Rozmiar + parametry" : "Tylko parametry";
        TaskDialog.Show(
            "Velocity Sizer",
            $"Tryb: {mode}\n" +
            $"Zaznaczone: {selectedCount}\n" +
            $"Zaktualizowane: {result.UpdatedCount}\n" +
            $"Brak rozmiaru/przeplywu: {notReadyCount}\n" +
            $"Brak parametrow docelowych: {result.MissingParameterCount}\n" +
            $"Pominiete (istniejace wartosci): {result.SkippedExistingValuesCount}\n" +
            $"Nieudane: {result.FailedCount}");
    }

    private static bool CanSkipOverwritePrompt(IReadOnlyList<VelocitySizerSelectionItem> items)
    {
        return !items.Any(item => item.HasExistingFlowValue || item.HasExistingVelocityValue);
    }

    private static TaskDialogResult ShowSkipExistingDialog()
    {
        TaskDialog dialog = new("Velocity Sizer")
        {
            MainInstruction = "Czesc elementow ma juz wartosci w LIN_VE_VELOCITY lub LIN_VE_FLOWRATE_SUM.",
            MainContent = "Wybierz sposob postepowania dla istniejacych wartosci.",
            CommonButtons = TaskDialogCommonButtons.Cancel
        };

        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Nadpisz istniejace wartosci");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Pomin elementy z istniejacymi wartosciami");
        return dialog.Show();
    }
}
