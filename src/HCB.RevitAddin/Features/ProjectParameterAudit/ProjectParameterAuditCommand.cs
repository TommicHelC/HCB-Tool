using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.ProjectParameterAudit.Models;
using HCB.RevitAddin.UI.Controls;

namespace HCB.RevitAddin.Features.ProjectParameterAudit;

[Transaction(TransactionMode.Manual)]
public sealed class ProjectParameterAuditCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document document = commandData.Application.ActiveUIDocument.Document;
        ProjectParameterAuditService service = new();
        IReadOnlyList<ProjectParameterAuditRow> result = service.BuildDefaultReport(document);

        List<ReportPreviewColumn> columns =
        [
            new() { Key = "ParameterName", Header = "Parametr" },
            new() { Key = "Status", Header = "Status" },
            new() { Key = "ExpectedSpec", Header = "Oczekiwany typ" },
            new() { Key = "FoundSpec", Header = "Typ w projekcie" },
            new() { Key = "ElementsWithValue", Header = "Wartosci" },
            new() { Key = "ScheduleCount", Header = "Zestawienia" },
            new() { Key = "FilterCount", Header = "Filtry" },
            new() { Key = "BindingKind", Header = "Binding" },
            new() { Key = "FoundGuids", Header = "GUID w projekcie" },
            new() { Key = "ExpectedGuid", Header = "GUID z rodziny" },
            new() { Key = "StorageTypes", Header = "Storage" },
            new() { Key = "Categories", Header = "Kategorie" },
            new() { Key = "Schedules", Header = "Nazwy zestawien" },
            new() { Key = "Filters", Header = "Nazwy filtrow" },
            new() { Key = "Recommendation", Header = "Rekomendacja" }
        ];

        IReadOnlyList<IReadOnlyDictionary<string, string>> rows = result
            .Select(ToRow)
            .ToList();

        int conflictCount = result.Count(row => !string.Equals(row.Status, "OK", StringComparison.OrdinalIgnoreCase));
        string summary =
            $"Sprawdzone parametry BimObject: {result.Count} | Konflikty/uwagi: {conflictCount} | Raport jest tylko diagnostyczny, bez zmian w modelu.";

        ReportPreviewWindow previewWindow = new(
            "Project Parameters - BimObject Audit",
            summary,
            columns,
            rows,
            "project-parameter-bimobject-audit.csv",
            outputPath => service.ExportCsv(result, outputPath),
            footerSummary: "Wiersze z wartosciami, zestawieniami albo filtrami wymagaja migracji lub recznej decyzji przed usunieciem parametru projektu.");

        previewWindow.ShowDialog();
        return Result.Succeeded;
    }

    private static IReadOnlyDictionary<string, string> ToRow(ProjectParameterAuditRow row)
    {
        return new Dictionary<string, string>
        {
            ["ParameterName"] = row.ParameterName,
            ["Status"] = row.Status,
            ["ExpectedSpec"] = row.ExpectedSpec,
            ["FoundSpec"] = row.FoundSpec,
            ["ExpectedGuid"] = row.ExpectedGuid,
            ["FoundGuids"] = row.FoundGuids,
            ["BindingKind"] = row.BindingKind,
            ["GroupTypeId"] = row.GroupTypeId,
            ["Categories"] = row.Categories,
            ["BoundCategoryCount"] = row.BoundCategoryCount.ToString(CultureInfo.InvariantCulture),
            ["ElementsWithParameter"] = row.ElementsWithParameter.ToString(CultureInfo.InvariantCulture),
            ["ElementsWithValue"] = row.ElementsWithValue.ToString(CultureInfo.InvariantCulture),
            ["StorageTypes"] = row.StorageTypes,
            ["ScheduleCount"] = row.ScheduleCount.ToString(CultureInfo.InvariantCulture),
            ["Schedules"] = row.Schedules,
            ["FilterCount"] = row.FilterCount.ToString(CultureInfo.InvariantCulture),
            ["Filters"] = row.Filters,
            ["Recommendation"] = row.Recommendation
        };
    }
}
