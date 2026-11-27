using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>
/// One-off import of an HCB workbook edited earlier (no live session): shows what changed, then writes it
/// in one transaction.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class ScheduleExcelImportCommand : IExternalCommand
{
    private const string Title = "Import zmian z Excel";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document? document = commandData.Application.ActiveUIDocument?.Document;
        if (document == null || document.IsFamilyDocument)
        {
            TaskDialog.Show(Title, "Otwórz projekt, z którego wyeksportowano plik.");
            return Result.Cancelled;
        }

        ScheduleExcelSettings settings = ScheduleExcelSettings.Load();
        var dialog = new OpenFileDialog
        {
            Title = Title,
            Filter = "Plik Excel (*.xlsx)|*.xlsx",
            InitialDirectory = Directory.Exists(settings.LastFolder) ? settings.LastFolder : string.Empty
        };
        if (dialog.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        if (ScheduleExcelSyncManager.Find(dialog.FileName) != null)
        {
            TaskDialog.Show(Title, "Dla tego pliku działa synchronizacja na żywo - zapisz go w Excel (Ctrl+S).");
            return Result.Cancelled;
        }

        ScheduleExcelWorkbookContent? content = ScheduleExcelWorkflow.ReadWorkbook(dialog.FileName, Title);
        if (content == null)
        {
            return Result.Failed;
        }

        string? invalid = ScheduleExcelApplier.Validate(document, content);
        if (invalid != null)
        {
            TaskDialog.Show(Title, invalid);
            return Result.Cancelled;
        }

        ScheduleExcelChangeSet changes = ScheduleExcelChangeSet.Compute(content, content.Snapshot);
        if (changes.Edits.Count == 0)
        {
            TaskDialog.Show(Title, "W pliku nie ma zmian do zapisania w Revit."
                + (changes.Skipped.Count > 0 ? "\n\nPominięte:\n" + ScheduleExcelWorkflow.DescribeSkipped(changes.Skipped) : string.Empty));
            return Result.Cancelled;
        }

        var confirm = new TaskDialog(Title)
        {
            MainInstruction = $"Zapisać {changes.Edits.Count} zmian do Revit?",
            MainContent = $"Zestawienie: {content.Get("ScheduleName")}\nPlik: {Path.GetFileName(dialog.FileName)}"
                + (changes.Skipped.Count > 0 ? $"\n\nPominięte ({changes.Skipped.Count}):\n" + ScheduleExcelWorkflow.DescribeSkipped(changes.Skipped, 5) : string.Empty),
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.Yes
        };
        if (confirm.Show() != TaskDialogResult.Yes)
        {
            return Result.Cancelled;
        }

        ScheduleExcelApplyResult result = ScheduleExcelApplier.Apply(document, content, changes.Edits);
        List<ScheduleExcelSkip> skipped = [.. changes.Skipped, .. result.Skipped];
        ScheduleExcelLog.Info($"Import {dialog.FileName}: zmian {result.Applied.Count}, zapisów parametrów {result.ParameterWrites}, pominięto {skipped.Count}.");
        foreach (ScheduleExcelSkip skip in skipped)
        {
            ScheduleExcelLog.Info($"  pominięto {skip.RowKey} / {skip.Column}: {skip.Reason}");
        }

        string report = result.Error.Length > 0
            ? result.Error
            : $"Zapisano {result.Applied.Count} zmian ({result.ParameterWrites} parametrów). Cofniesz je jednym Ctrl+Z.";
        if (skipped.Count > 0)
        {
            report += $"\n\nPominięte ({skipped.Count}):\n" + ScheduleExcelWorkflow.DescribeSkipped(skipped);
        }

        TaskDialog.Show(Title, report);
        return result.Applied.Count > 0 ? Result.Succeeded : Result.Cancelled;
    }
}
