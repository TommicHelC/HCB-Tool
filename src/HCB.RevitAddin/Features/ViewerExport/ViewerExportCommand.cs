using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ViewerExport.Models;
using HCB.RevitAddin.Infrastructure.WithoutOpen;
using HCB.RevitAddin.Infrastructure.WithoutOpen.Models;
using HCB.RevitAddin.UI.Controls;

namespace HCB.RevitAddin.Features.ViewerExport;

[Transaction(TransactionMode.Manual)]
public sealed class ViewerExportCommand : IExternalCommand
{
    private const string Title = "WithoutOpen - Viewer Export";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Autodesk.Revit.ApplicationServices.Application application = commandData.Application.Application;

        CloudModelCacheScanner scanner = new();
        IReadOnlyList<CloudModelCandidate> candidates = scanner.Scan(application.VersionNumber);
        if (candidates.Count == 0)
        {
            TaskDialog.Show(Title, "Nie znaleziono modeli ACC w lokalnym cache Revita.\nOtworz model z ACC przynajmniej raz w tej wersji Revita, aby pojawil sie na liscie.");
            return Result.Cancelled;
        }

        SelectionListWindow selectionWindow = new(
            "Viewer Export",
            "Modele ACC (z lokalnego cache Revita)",
            candidates.Select(candidate => new SelectionListItem(candidate, candidate.DisplayName, candidate.ProjectName)),
            [],
            "Dalej",
            $"Modele: {candidates.Count}. Wybierz modele glowne - wszystkie zaladowane linki RVT zostana dolaczone automatycznie.",
            primaryFilterLabel: "Projekt");

        if (selectionWindow.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        List<CloudModelCandidate> selectedModels = selectionWindow.SelectedValues.Cast<CloudModelCandidate>().ToList();
        if (selectedModels.Count == 0)
        {
            return Result.Cancelled;
        }

        WithoutOpenDialogService dialogService = new();
        string? outputFolderPath = dialogService.PickFolderPath("Wybierz folder docelowy (np. OneDrive) dla kopii do Autodesk Viewer.");
        if (string.IsNullOrWhiteSpace(outputFolderPath))
        {
            return Result.Cancelled;
        }

        ViewerExportService service = new();
        ViewerExportResult result;
        try
        {
            result = service.Export(commandData.Application, selectedModels, outputFolderPath);
        }
        catch (System.Exception exception)
        {
            TaskDialog.Show(Title, "Eksport przerwany: " + exception.Message);
            return Result.Failed;
        }

        List<ReportPreviewColumn> columns =
        [
            new() { Key = "Model", Header = "Model" },
            new() { Key = "Status", Header = "Status" },
            new() { Key = "Message", Header = "Komunikat" },
            new() { Key = "OutputPath", Header = "Plik wynikowy" },
            new() { Key = "Duration", Header = "Czas [s]" }
        ];

        IReadOnlyList<IReadOnlyDictionary<string, string>> rows = result.Entries
            .Select(ToRow)
            .ToList();

        string summary =
            $"Operacje: {result.Entries.Count} | Sukces: {result.SuccessCount} | Pominiete: {result.SkippedCount} | Bledy: {result.FailedCount}";

        ReportPreviewWindow previewWindow = new(
            Title,
            summary,
            columns,
            rows,
            "withoutopen-viewer-export.csv",
            outputPath =>
            {
                WithoutOpenBatchLogService logService = new();
                logService.ExportOperationsToCsv(result.Entries, outputPath);
            },
            "Otworz folder",
            OpenOutputFolder);

        previewWindow.ShowDialog();
        return Result.Succeeded;
    }

    private static IReadOnlyDictionary<string, string> ToRow(WithoutOpenOperationLogEntry entry)
    {
        return new Dictionary<string, string>
        {
            ["Model"] = entry.FilePath,
            ["Status"] = entry.Status.ToString(),
            ["Message"] = entry.Message,
            ["OutputPath"] = entry.OutputPath,
            ["Duration"] = entry.Duration.TotalSeconds.ToString("0.#")
        };
    }

    private static void OpenOutputFolder(IReadOnlyDictionary<string, string> row)
    {
        string outputPath = row.TryGetValue("OutputPath", out string? value) ? value ?? string.Empty : string.Empty;
        string? directoryPath = string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            TaskDialog.Show(Title, "Nie udalo sie otworzyc folderu dla wybranego wiersza.");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = directoryPath,
            UseShellExecute = true
        });
    }
}
