using System;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.ScheduleExcel.Models;

namespace HCB.RevitAddin.Features.ScheduleExcel;

public sealed class ScheduleExcelApplyResult
{
    public string TransactionName { get; init; } = string.Empty;

    /// <summary>Edits written to every target element; the caller moves its baseline to their new values.</summary>
    public List<ScheduleExcelEdit> Applied { get; } = [];

    public List<ScheduleExcelSkip> Skipped { get; } = [];

    public int ParameterWrites { get; set; }

    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// Writes user edits to Revit on the Revit thread: validates the document, checks for conflicts
/// (value changed in Revit since the baseline), checks out elements once (worksharing / ACC), then one
/// transaction for the whole sync so a single Ctrl+Z undoes it. Errors roll back; warnings are dismissed.
/// </summary>
internal static class ScheduleExcelApplier
{
    /// <summary>Null when the file belongs to this document and schedule, else a Polish reason.</summary>
    public static string? Validate(Document document, ScheduleExcelWorkbookContent content)
    {
        if (!document.IsValidObject)
        {
            return "Model został zamknięty.";
        }

        if (document.IsReadOnly)
        {
            return "Model jest tylko do odczytu.";
        }

        if (document.IsModifiable)
        {
            return "W Revit trwa inna operacja (otwarta transakcja). Zapisz plik ponownie za chwilę.";
        }

        string fileDocumentId = content.Get("DocumentId");
        string documentId = ScheduleExcelReader.GetDocumentId(document);
        if (fileDocumentId.Length > 0 && documentId.Length > 0 && !string.Equals(fileDocumentId, documentId, StringComparison.OrdinalIgnoreCase))
        {
            return $"Plik pochodzi z innego modelu ('{content.Get("DocumentTitle")}').";
        }

        if (document.GetElement(content.Get("ScheduleUniqueId")) is not ViewSchedule)
        {
            return $"W modelu nie ma już zestawienia '{content.Get("ScheduleName")}'.";
        }

        return null;
    }

    public static ScheduleExcelApplyResult Apply(
        Document document,
        ScheduleExcelWorkbookContent content,
        IReadOnlyList<ScheduleExcelEdit> edits)
    {
        var result = new ScheduleExcelApplyResult { TransactionName = $"HCB Excel Sync {DateTime.Now:HH:mm:ss}" };
        var lookups = new Dictionary<int, ScheduleExcelParameterLookup?>();
        var operations = new List<Operation>();
        var claimed = new Dictionary<(long TargetId, int Position), string>();

        foreach (ScheduleExcelEdit edit in edits)
        {
            ScheduleExcelFieldInfo field = content.Fields[edit.FieldPosition];
            string column = ScheduleExcelChangeSet.Label(field);
            if (!lookups.TryGetValue(edit.FieldPosition, out ScheduleExcelParameterLookup? lookup))
            {
                lookups[edit.FieldPosition] = lookup = ScheduleExcelParameterLookup.Create(document, new ElementId(field.ParameterId));
            }

            if (lookup == null)
            {
                result.Skipped.Add(new ScheduleExcelSkip(edit.RowKey, column, "Nie znaleziono parametru w modelu."));
                continue;
            }

            ForgeTypeId? unit = field.UnitTypeId.Length > 0 ? new ForgeTypeId(field.UnitTypeId) : null;
            var targets = new Dictionary<long, (Element Target, Parameter Parameter)>();
            string? problem = null;
            int missing = 0;
            foreach (string uniqueId in content.RowElements.TryGetValue(edit.RowKey, out List<string>? ids) ? ids : [])
            {
                Element? element = document.GetElement(uniqueId);
                Element? target = element == null ? null : ScheduleExcelParameterLookup.GetTarget(document, element, field.IsTypeParameter);
                Parameter? parameter = lookup.Get(target);
                if (target == null || parameter == null)
                {
                    missing++;
                    continue;
                }

                if (targets.ContainsKey(target.Id.Value))
                {
                    continue;
                }

                if (parameter.IsReadOnly)
                {
                    problem = "Parametr jest tylko do odczytu (np. sterowany formułą w rodzinie).";
                    break;
                }

                // Conflict: someone changed the value in Revit since the export / last sync.
                string current = ScheduleExcelReader.ToComparableText(ScheduleExcelParameterLookup.ReadValue(parameter, field.ValueKind, unit).Value);
                if (edit.BaselineText != ScheduleExcelFormat.MixedMarker
                    && !ScheduleExcelChangeSet.AreEqual(current, edit.BaselineText, field.ValueKind)
                    && !ScheduleExcelChangeSet.AreEqual(current, edit.NewText, field.ValueKind))
                {
                    problem = $"Konflikt: w Revit zmieniono wartość na '{current}' po eksporcie.";
                    break;
                }

                if (claimed.TryGetValue((target.Id.Value, edit.FieldPosition), out string? other) && other != edit.NewText)
                {
                    problem = "Inny wiersz ustawia temu samemu typowi inną wartość.";
                    break;
                }

                targets[target.Id.Value] = (target, parameter);
            }

            if (problem == null && targets.Count == 0)
            {
                problem = missing > 0 ? "Elementy wiersza nie istnieją już w modelu." : "Brak elementów dla wiersza.";
            }

            if (problem != null)
            {
                result.Skipped.Add(new ScheduleExcelSkip(edit.RowKey, column, problem));
                continue;
            }

            foreach ((long targetId, (Element target, Parameter parameter)) in targets)
            {
                claimed[(targetId, edit.FieldPosition)] = edit.NewText;
                operations.Add(new Operation(edit, column, target, parameter, unit, field.ValueKind));
            }
        }

        if (operations.Count == 0)
        {
            return result;
        }

        ExcludeElementsOwnedByOthers(document, operations, result);
        if (operations.Count == 0)
        {
            return result;
        }

        var failedEdits = new HashSet<ScheduleExcelEdit>();
        using (var transaction = new Transaction(document, result.TransactionName))
        {
            FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new RollbackOnErrors());
            options.SetClearAfterRollback(true);
            transaction.SetFailureHandlingOptions(options);
            transaction.Start();

            foreach (Operation operation in operations)
            {
                string? error = TrySet(operation);
                if (error == null)
                {
                    result.ParameterWrites++;
                    continue;
                }

                failedEdits.Add(operation.Edit);
                result.Skipped.Add(new ScheduleExcelSkip(operation.Edit.RowKey, operation.Column, error));
            }

            if (result.ParameterWrites == 0)
            {
                transaction.RollBack();
                return result;
            }

            if (transaction.Commit() != TransactionStatus.Committed)
            {
                result.Error = "Revit odrzucił zmiany (błąd modelu) - nic nie zapisano.";
                result.ParameterWrites = 0;
                return result;
            }
        }

        result.Applied.AddRange(operations.Select(operation => operation.Edit).Distinct().Where(edit => !failedEdits.Contains(edit)));
        return result;
    }

    private static void ExcludeElementsOwnedByOthers(Document document, List<Operation> operations, ScheduleExcelApplyResult result)
    {
        if (!document.IsWorkshared)
        {
            return;
        }

        var ids = operations.Select(operation => operation.Target.Id).Distinct().ToList();
        ICollection<ElementId> checkedOut;
        try
        {
            // One batched call: in cloud models every checkout is a server round trip.
            checkedOut = WorksharingUtils.CheckoutElements(document, ids);
        }
        catch (Exception exception)
        {
            ScheduleExcelLog.Error("CheckoutElements nie powiodło się.", exception);
            checkedOut = [];
        }

        var owned = new HashSet<long>(checkedOut.Select(id => id.Value));
        foreach (Operation operation in operations.Where(operation => !owned.Contains(operation.Target.Id.Value)).ToList())
        {
            result.Skipped.Add(new ScheduleExcelSkip(operation.Edit.RowKey, operation.Column, "Element zajęty przez innego użytkownika (worksharing)."));
            operations.Remove(operation);
        }
    }

    private static string? TrySet(Operation operation)
    {
        Parameter parameter = operation.Parameter;
        try
        {
            bool ok = operation.Kind switch
            {
                ScheduleExcelValueKind.Number when parameter.StorageType == StorageType.Double =>
                    parameter.Set(operation.Unit == null
                        ? (double)operation.Edit.NewValue!
                        : UnitUtils.ConvertToInternalUnits((double)operation.Edit.NewValue!, operation.Unit)),
                ScheduleExcelValueKind.Integer when parameter.StorageType == StorageType.Integer =>
                    parameter.Set((int)Math.Round((double)operation.Edit.NewValue!)),
                ScheduleExcelValueKind.YesNo when parameter.StorageType == StorageType.Integer =>
                    parameter.Set((bool)operation.Edit.NewValue! ? 1 : 0),
                ScheduleExcelValueKind.Text when parameter.StorageType == StorageType.String =>
                    parameter.Set((string?)operation.Edit.NewValue ?? string.Empty),
                _ => false
            };
            return ok ? null : "Revit nie przyjął wartości.";
        }
        catch (Exception exception)
        {
            return $"Revit nie przyjął wartości: {exception.Message}";
        }
    }

    private sealed record Operation(
        ScheduleExcelEdit Edit,
        string Column,
        Element Target,
        Parameter Parameter,
        ForgeTypeId? Unit,
        ScheduleExcelValueKind Kind);

    /// <summary>Warnings are dismissed; any error rolls the whole sync back instead of showing a Revit dialog.</summary>
    private sealed class RollbackOnErrors : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hasErrors = false;
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else
                {
                    hasErrors = true;
                    ScheduleExcelLog.Info($"Błąd Revit przy synchronizacji: {failure.GetDescriptionText()}");
                }
            }

            return hasErrors ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
