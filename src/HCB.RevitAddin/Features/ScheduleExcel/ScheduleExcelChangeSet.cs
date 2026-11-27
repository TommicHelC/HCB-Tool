using System;
using HCB.RevitAddin.Features.ScheduleExcel.Models;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>One cell the user changed: new value already converted to the column's kind.</summary>
public sealed record ScheduleExcelEdit(string RowKey, int FieldPosition, object? NewValue, string NewText, string BaselineText);

/// <summary>A change that will not be written, with a short Polish reason.</summary>
public sealed record ScheduleExcelSkip(string RowKey, string Column, string Reason);

/// <summary>
/// Compares the current Excel cells with the baseline (export snapshot, then updated after each applied sync)
/// and returns only real user edits. Pure logic - no Revit API, no file access.
/// </summary>
internal sealed class ScheduleExcelChangeSet
{
    /// <summary>Marker for an Excel error cell (#N/A, #DIV/0!...).</summary>
    public static readonly object ErrorValue = new();

    private const double RelativeTolerance = 1e-9;

    public List<ScheduleExcelEdit> Edits { get; } = [];

    public List<ScheduleExcelSkip> Skipped { get; } = [];

    public static ScheduleExcelChangeSet Compute(ScheduleExcelWorkbookContent content, IReadOnlyDictionary<string, string[]> baseline)
    {
        var result = new ScheduleExcelChangeSet();

        // A column that could not be found reads as empty cells - never treat that as "user cleared the values".
        var missing = new HashSet<int>(content.MissingFields.Select(field => content.Fields.IndexOf(field)));
        foreach (ScheduleExcelFieldInfo field in content.MissingFields.Where(field => !field.IsReadOnly))
        {
            result.Skipped.Add(new ScheduleExcelSkip(string.Empty, Label(field), "Nie znaleziono kolumny (zmieniony lub usunięty nagłówek) - pominięto całą kolumnę."));
        }

        foreach ((string rowKey, object?[] cells) in content.Current)
        {
            if (!baseline.TryGetValue(rowKey, out string[]? baseRow))
            {
                continue;
            }

            for (int position = 0; position < content.Fields.Count && position < cells.Length && position < baseRow.Length; position++)
            {
                if (missing.Contains(position))
                {
                    continue;
                }

                ScheduleExcelFieldInfo field = content.Fields[position];
                string baseText = baseRow[position];
                object? cell = cells[position];

                if (baseText == ScheduleExcelFormat.MixedMarker && cell == null)
                {
                    continue;
                }

                if (!TryNormalize(cell, field.ValueKind, out object? value, out string text, out string? error))
                {
                    if (!field.IsReadOnly)
                    {
                        result.Skipped.Add(new ScheduleExcelSkip(rowKey, Label(field), error ?? "Nieprawidłowa wartość."));
                    }

                    continue;
                }

                if (AreEqual(baseText, text, field.ValueKind))
                {
                    continue;
                }

                if (field.IsReadOnly)
                {
                    result.Skipped.Add(new ScheduleExcelSkip(rowKey, Label(field), "Kolumna tylko do odczytu - zmiana nie wraca do Revit."));
                    continue;
                }

                if (value == null && field.ValueKind != ScheduleExcelValueKind.Text)
                {
                    result.Skipped.Add(new ScheduleExcelSkip(rowKey, Label(field), "Pustej komórki nie da się zapisać do parametru liczbowego."));
                    continue;
                }

                result.Edits.Add(new ScheduleExcelEdit(rowKey, position, value, text, baseText));
            }
        }

        return result;
    }

    public static string Label(ScheduleExcelFieldInfo field) => field.Heading.Length > 0 ? field.Heading : field.Header;

    /// <summary>Converts an Excel cell into the column's kind and the comparable text used by the snapshot.</summary>
    public static bool TryNormalize(object? cell, ScheduleExcelValueKind kind, out object? value, out string text, out string? error)
    {
        value = null;
        text = string.Empty;
        error = null;
        if (ReferenceEquals(cell, ErrorValue))
        {
            error = "Komórka zawiera błąd Excela.";
            return false;
        }

        if (cell == null)
        {
            return true;
        }

        switch (kind)
        {
            case ScheduleExcelValueKind.Number:
            case ScheduleExcelValueKind.Integer:
                double number;
                if (cell is double direct)
                {
                    number = direct;
                }
                else if (cell is string raw && ScheduleExcelReader.TryParseDisplayNumber(raw, out double parsed, out _))
                {
                    number = parsed;
                }
                else
                {
                    error = "Oczekiwano liczby.";
                    return false;
                }

                if (kind == ScheduleExcelValueKind.Integer)
                {
                    if (Math.Abs(number - Math.Round(number)) > 1e-9)
                    {
                        error = "Oczekiwano liczby całkowitej.";
                        return false;
                    }

                    number = Math.Round(number);
                }

                value = number;
                text = number.ToString("R", CultureInfo.InvariantCulture);
                return true;

            case ScheduleExcelValueKind.YesNo:
                bool? flag = cell switch
                {
                    bool b => b,
                    double d when d is 0 or 1 => d == 1,
                    string s => ParseYesNo(s),
                    _ => null
                };
                if (flag == null)
                {
                    error = "Oczekiwano Tak/Nie (PRAWDA/FAŁSZ).";
                    return false;
                }

                value = flag.Value;
                text = flag.Value ? "1" : "0";
                return true;

            default:
                string textValue = cell switch
                {
                    double d => d.ToString("G15", CultureInfo.CurrentCulture),
                    bool b => b ? "1" : "0",
                    _ => Convert.ToString(cell, CultureInfo.CurrentCulture) ?? string.Empty
                };
                value = textValue;
                text = textValue;
                return true;
        }
    }

    private static bool? ParseYesNo(string text)
    {
        return text.Trim().ToLowerInvariant() switch
        {
            "1" or "tak" or "t" or "yes" or "y" or "true" or "prawda" or "x" => true,
            "0" or "nie" or "n" or "no" or "false" or "fałsz" or "falsz" or "" => false,
            _ => null
        };
    }

    public static bool AreEqual(string left, string right, ScheduleExcelValueKind kind)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (kind is ScheduleExcelValueKind.Number or ScheduleExcelValueKind.Integer
            && double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
            && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
        {
            return Math.Abs(a - b) <= RelativeTolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
        }

        return false;
    }
}
