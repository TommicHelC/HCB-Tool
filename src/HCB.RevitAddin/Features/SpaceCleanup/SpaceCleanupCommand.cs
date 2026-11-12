using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.SpaceCleanup.Models;
using HCB.RevitAddin.UI.Controls;

namespace HCB.RevitAddin.Features.SpaceCleanup;

[Transaction(TransactionMode.Manual)]
public sealed class SpaceCleanupCommand : IExternalCommand
{
    private const string Title = "Space Cleanup";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document document = commandData.Application.ActiveUIDocument.Document;
        SpaceCleanupService service = new();
        IReadOnlyList<SpaceCleanupItem> spaces = service.GetSpaces(document);

        if (spaces.Count == 0)
        {
            TaskDialog.Show(Title, "Brak przestrzeni MEP (Spaces) w projekcie.");
            return Result.Succeeded;
        }

        string statusSummary = string.Join(
            ", ",
            spaces.GroupBy(item => item.Status)
                .OrderBy(group => group.Key)
                .Select(group => $"{SpaceCleanupService.GetStatusLabel(group.Key)}: {group.Count()}"));

        SelectionListWindow window = new(
            Title,
            "Przestrzenie MEP",
            spaces.Select(item => new SelectionListItem(
                item,
                SpaceCleanupService.GetDisplayLabel(item),
                SpaceCleanupService.GetStatusLabel(item.Status),
                item.LevelName)),
            [],
            "Usun wybrane",
            $"Przestrzeni: {spaces.Count} ({statusSummary}). Wpisz fraze z nazwy, ustaw Status i kliknij Zaznacz wszystkie.",
            primaryFilterLabel: "Status",
            secondaryFilterLabel: "Poziom");

        if (window.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        List<SpaceCleanupItem> selected = window.SelectedValues.Cast<SpaceCleanupItem>().ToList();
        int placedCount = selected.Count(item => item.Status == SpacePlacementStatus.Placed);

        TaskDialog confirm = new(Title)
        {
            MainInstruction = $"Usunac {selected.Count} przestrzeni?",
            MainContent = placedCount > 0
                ? $"Uwaga: {placedCount} z nich to przestrzenie umieszczone (z powierzchnia). Usuniete zostana tez ich znaczniki."
                : "Usuniete zostana tez ich znaczniki. Operacje mozna cofnac przez Ctrl+Z.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };

        if (confirm.Show() != TaskDialogResult.Yes)
        {
            return Result.Cancelled;
        }

        SpaceCleanupResult result = service.DeleteSpaces(document, selected);
        TaskDialog.Show(
            Title,
            $"Usuniete przestrzenie: {result.DeletedCount}\n" +
            $"Usuniete elementy zalezne (np. znaczniki): {result.DependentDeletedCount}\n" +
            $"Bledy: {result.FailedCount}\n\n" +
            string.Join("\n", result.Messages.Where(line => line.StartsWith("Nie")).Concat(result.Messages).Distinct().Take(12)));

        return Result.Succeeded;
    }
}
