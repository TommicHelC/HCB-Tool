using System;
using System.Security.Cryptography;
using System.Threading;
using ClosedXML.Excel;
using HCB.RevitAddin.Features.ScheduleExcel.Models;

namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>Column metadata read back from the _hcb_fields sheet.</summary>
public sealed class ScheduleExcelFieldInfo
{
    public int Column { get; init; }

    public string Heading { get; init; } = string.Empty;

    public string Header { get; init; } = string.Empty;

    public int FieldIndex { get; init; }

    public long ParameterId { get; init; }

    public bool IsTypeParameter { get; init; }

    public ScheduleExcelValueKind ValueKind { get; init; }

    public string UnitTypeId { get; init; } = string.Empty;

    public bool IsReadOnly { get; init; }
}

/// <summary>A whole workbook: one part per exported schedule.</summary>
public sealed class ScheduleExcelWorkbook
{
    public string Hash { get; init; } = string.Empty;

    public Dictionary<string, string> Meta { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ScheduleExcelWorkbookContent> Parts { get; } = [];

    public string Get(string key) => Meta.TryGetValue(key, out string? value) ? value : string.Empty;
}

/// <summary>
/// One schedule of the workbook - everything a sync needs: metadata (workbook + schedule), element map,
/// export snapshot and current cells.
/// </summary>
public sealed class ScheduleExcelWorkbookContent
{
    public string Hash { get; init; } = string.Empty;

    public Dictionary<string, string> Meta { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ScheduleExcelFieldInfo> Fields { get; } = [];

    public Dictionary<string, List<string>> RowElements { get; } = new(StringComparer.Ordinal);

    /// <summary>Row key -> comparable text per field (same order as <see cref="Fields"/>).</summary>
    public Dictionary<string, string[]> Snapshot { get; } = new(StringComparer.Ordinal);

    /// <summary>Row key -> current cell value per field (null = empty cell).</summary>
    public Dictionary<string, object?[]> Current { get; } = new(StringComparer.Ordinal);

    /// <summary>Fields whose column was not found (heading renamed or deleted); their cells are null.</summary>
    public List<ScheduleExcelFieldInfo> MissingFields { get; } = [];

    public string Get(string key) => Meta.TryGetValue(key, out string? value) ? value : string.Empty;
}

/// <summary>
/// Reads an HCB schedule workbook while Excel may hold it open: shared read of a copy, retries on IOException,
/// never a long-lived handle. No Revit API - safe on a background thread.
/// </summary>
internal static class ScheduleExcelWorkbookReader
{
    /// <summary>Reads the file bytes with FileShare.ReadWrite|Delete; retries while Excel is mid-save.</summary>
    public static byte[] ReadBytes(string path, int attempts = 5)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                return copy.ToArray();
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(Math.Min(1000, 200 * attempt));
            }
        }
    }

    public static string ComputeHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Reads every schedule in the workbook. v2 files hold one schedule in un-numbered sheets.</summary>
    public static ScheduleExcelWorkbook Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var workbook = new XLWorkbook(stream);
        string hash = ComputeHash(bytes);

        if (!workbook.TryGetWorksheet(ScheduleExcelFormat.MetaSheet, out IXLWorksheet metaSheet))
        {
            throw new InvalidDataException("To nie jest plik zestawienia HCB (brak arkusza metadanych).");
        }

        Dictionary<string, string> meta = ReadKeyValues(metaSheet);
        string Get(string key) => meta.TryGetValue(key, out string? value) ? value : string.Empty;
        if (Get("Tool") != ScheduleExcelFormat.ToolId)
        {
            throw new InvalidDataException("To nie jest plik zestawienia HCB.");
        }

        if (!int.TryParse(Get("FormatVersion"), out int version) || version > ScheduleExcelFormat.FormatVersion)
        {
            throw new InvalidDataException("Plik pochodzi z nowszej wersji dodatku HCB. Zaktualizuj dodatek.");
        }

        if (version < ScheduleExcelFormat.MinFormatVersion)
        {
            throw new InvalidDataException("Plik pochodzi ze starszej wersji dodatku HCB. Wyeksportuj zestawienie ponownie.");
        }

        var result = new ScheduleExcelWorkbook { Hash = hash };
        foreach ((string key, string value) in meta)
        {
            result.Meta[key] = value;
        }

        if (version < 3)
        {
            result.Parts.Add(ReadPart(workbook, hash, meta, null, ScheduleExcelFormat.MetaSheet, ScheduleExcelFormat.FieldsSheet,
                ScheduleExcelFormat.RowsSheet, ScheduleExcelFormat.SnapshotSheet));
            return result;
        }

        int count = int.TryParse(Get("ScheduleCount"), out int parsed) ? parsed : 0;
        for (int index = 1; index <= count; index++)
        {
            string partMeta = ScheduleExcelFormat.PartSheet(ScheduleExcelFormat.MetaSheet, index);
            result.Parts.Add(ReadPart(workbook, hash, meta, partMeta,
                partMeta,
                ScheduleExcelFormat.PartSheet(ScheduleExcelFormat.FieldsSheet, index),
                ScheduleExcelFormat.PartSheet(ScheduleExcelFormat.RowsSheet, index),
                ScheduleExcelFormat.PartSheet(ScheduleExcelFormat.SnapshotSheet, index)));
        }

        return result;
    }

    private static ScheduleExcelWorkbookContent ReadPart(
        XLWorkbook workbook,
        string hash,
        Dictionary<string, string> globalMeta,
        string? partMetaSheet,
        string metaSheet,
        string fieldsSheet,
        string rowsSheet,
        string snapshotSheet)
    {
        var content = new ScheduleExcelWorkbookContent { Hash = hash };
        foreach ((string key, string value) in globalMeta)
        {
            content.Meta[key] = value;
        }

        if (partMetaSheet != null)
        {
            foreach ((string key, string value) in ReadKeyValues(workbook.Worksheet(partMetaSheet)))
            {
                content.Meta[key] = value;
            }
        }

        ReadFields(workbook.Worksheet(fieldsSheet), content);
        ReadRowElements(workbook.Worksheet(rowsSheet), content);
        ReadSnapshot(workbook.Worksheet(snapshotSheet), content);

        string dataSheetName = content.Get("DataSheet");
        if (!workbook.TryGetWorksheet(dataSheetName, out IXLWorksheet data))
        {
            throw new InvalidDataException($"Brak arkusza danych '{dataSheetName}'. Nie zmieniaj nazw arkuszy.");
        }

        ReadCurrent(data, content);
        return content;
    }

    private static Dictionary<string, string> ReadKeyValues(IXLWorksheet sheet)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IXLRow row in sheet.RowsUsed())
        {
            values[row.Cell(1).GetString()] = row.Cell(2).GetString();
        }

        return values;
    }

    private static void ReadFields(IXLWorksheet sheet, ScheduleExcelWorkbookContent content)
    {
        IXLRow header = sheet.Row(1);
        var columns = header.CellsUsed().ToDictionary(cell => cell.GetString(), cell => cell.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        string Text(IXLRow row, string name) => columns.TryGetValue(name, out int column) ? row.Cell(column).GetString() : string.Empty;

        foreach (IXLRow row in sheet.RowsUsed().Skip(1))
        {
            content.Fields.Add(new ScheduleExcelFieldInfo
            {
                Column = int.TryParse(Text(row, "Column"), out int column) ? column : 0,
                Heading = Text(row, "Heading"),
                Header = Text(row, "Header"),
                FieldIndex = int.TryParse(Text(row, "FieldIndex"), out int fieldIndex) ? fieldIndex : -1,
                ParameterId = long.TryParse(Text(row, "ParameterId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parameterId) ? parameterId : 0,
                IsTypeParameter = Text(row, "IsTypeParameter") == "1",
                ValueKind = Enum.TryParse(Text(row, "ValueKind"), out ScheduleExcelValueKind kind) ? kind : ScheduleExcelValueKind.Text,
                UnitTypeId = Text(row, "UnitTypeId"),
                IsReadOnly = Text(row, "ReadOnly") == "1"
            });
        }
    }

    private static void ReadRowElements(IXLWorksheet sheet, ScheduleExcelWorkbookContent content)
    {
        foreach (IXLRow row in sheet.RowsUsed().Skip(1))
        {
            string key = row.Cell(1).GetString();
            if (!content.RowElements.TryGetValue(key, out List<string>? ids))
            {
                content.RowElements[key] = ids = [];
            }

            ids.Add(row.Cell(2).GetString());
        }
    }

    private static void ReadSnapshot(IXLWorksheet sheet, ScheduleExcelWorkbookContent content)
    {
        foreach (IXLRow row in sheet.RowsUsed().Skip(1))
        {
            content.Snapshot[row.Cell(ScheduleExcelFormat.KeyColumn).GetString()] = content.Fields
                .Select((_, index) => row.Cell(ScheduleExcelFormat.FirstValueColumn + index).GetString())
                .ToArray();
        }
    }

    /// <summary>
    /// Finds each field's column by its heading (the user may have moved or inserted columns), then reads every
    /// row that carries a key in the hidden column A. Group header/footer and totals rows have no key.
    /// </summary>
    private static void ReadCurrent(IXLWorksheet sheet, ScheduleExcelWorkbookContent content)
    {
        int keyColumn = ScheduleExcelFormat.KeyColumn;
        int headerRow = ScheduleExcelFormat.HeaderRow;
        IXLRow headings = sheet.Row(headerRow);
        foreach (IXLCell cell in headings.CellsUsed())
        {
            if (cell.GetString() == ScheduleExcelFormat.KeyHeader)
            {
                keyColumn = cell.Address.ColumnNumber;
            }
        }

        var byHeading = headings.CellsUsed()
            .GroupBy(cell => cell.GetString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Address.ColumnNumber, StringComparer.Ordinal);
        // Find each field by its heading; a stored position is used only when the heading there still matches,
        // so an inserted/moved column is never read as another field. Unresolved fields are left out (null).
        int[] fieldColumns = content.Fields
            .Select(field =>
            {
                if (field.Heading.Length > 0)
                {
                    return byHeading.TryGetValue(field.Heading, out int column) ? column : 0;
                }

                string atPosition = field.Column > 0 ? sheet.Cell(headerRow, field.Column).GetString() : string.Empty;
                return atPosition.Length > 0 && atPosition.StartsWith(field.Header.Replace('[', '(').Replace(']', ')'), StringComparison.Ordinal)
                    ? field.Column
                    : 0;
            })
            .ToArray();
        content.MissingFields.AddRange(content.Fields.Where((_, index) => fieldColumns[index] == 0));

        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int row = headerRow + 1; row <= lastRow; row++)
        {
            string key = sheet.Cell(row, keyColumn).GetString();
            if (key.Length == 0 || !content.RowElements.ContainsKey(key))
            {
                continue;
            }

            content.Current[key] = fieldColumns
                .Select(column => column > 0 ? ToObject(sheet.Cell(row, column)) : null)
                .ToArray();
        }
    }

    private static object? ToObject(IXLCell cell)
    {
        XLCellValue value = cell.Value;
        return value.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.Number => value.GetNumber(),
            XLDataType.Boolean => value.GetBoolean(),
            XLDataType.Text => value.GetText().Length == 0 ? null : value.GetText(),
            XLDataType.DateTime => value.GetDateTime().ToOADate(),
            XLDataType.TimeSpan => value.GetTimeSpan().TotalDays,
            _ => ScheduleExcelChangeSet.ErrorValue
        };
    }
}
