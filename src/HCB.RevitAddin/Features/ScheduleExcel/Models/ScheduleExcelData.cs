namespace HCB.RevitAddin.Features.ScheduleExcel.Models;

/// <summary>How a schedule column maps to Revit data.</summary>
public enum ScheduleExcelValueKind
{
    Text,
    Number,
    Integer,
    YesNo,
    ElementId
}

/// <summary>Revit table cell look, copied to Excel. Null members = Revit default (not overridden).</summary>
public sealed record ScheduleExcelCellStyle(
    string? Background,
    string? TextColor,
    bool Bold,
    bool Italic,
    bool Underline,
    string? HorizontalAlignment);

/// <summary>One visible schedule field exported as one Excel column.</summary>
public sealed class ScheduleExcelColumn
{
    public int FieldIndex { get; init; }

    public string Header { get; init; } = string.Empty;

    /// <summary>Unique heading text written to the Excel table; sync finds the column by it.</summary>
    public string Heading { get; set; } = string.Empty;

    public string FieldType { get; init; } = string.Empty;

    /// <summary>Parameter ElementId value (negative = BuiltInParameter); 0 when the field has no parameter.</summary>
    public long ParameterId { get; init; }

    public string ParameterName { get; init; } = string.Empty;

    /// <summary>Shared parameter GUID, empty for built-in and project parameters.</summary>
    public string ParameterGuid { get; init; } = string.Empty;

    public bool IsTypeParameter { get; init; }

    public string StorageType { get; init; } = string.Empty;

    public ScheduleExcelValueKind ValueKind { get; set; }

    public string SpecTypeId { get; init; } = string.Empty;

    /// <summary>Display unit the numbers are written in (ForgeTypeId), empty for non-measurable values.</summary>
    public string UnitTypeId { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    /// <summary>Excel number format, e.g. "0.00".</summary>
    public string NumberFormat { get; set; } = string.Empty;

    public bool IsReadOnly { get; set; }

    /// <summary>Editable column in which some cells are read only (e.g. a parameter locked for some elements).</summary>
    public bool HasReadOnlyCells { get; set; }

    public string ReadOnlyReason { get; set; } = string.Empty;

    /// <summary>True when values were taken from the schedule's display text (formulas, room fields...).</summary>
    public bool FromScheduleText { get; set; }

    /// <summary>"Sum", "Min", "Max" or empty - from the field's "Calculate totals" setting.</summary>
    public string TotalFunction { get; init; } = string.Empty;

    /// <summary>Allowed display values for a reference column (types, levels...); shown as a drop-down.</summary>
    public List<string> Choices { get; init; } = [];

    public ScheduleExcelCellStyle? HeaderStyle { get; set; }

    public ScheduleExcelCellStyle? BodyStyle { get; set; }
}

/// <summary>One cell value. <see cref="Mixed"/> = elements of a grouped row differ.</summary>
public readonly record struct ScheduleExcelValue(object? Value, bool Mixed, bool ReadOnly)
{
    public static ScheduleExcelValue Empty { get; } = new(null, false, false);
}

/// <summary>A sort/group level of the schedule that shows a header and/or footer.</summary>
public sealed class ScheduleExcelGroupLevel
{
    public bool ShowHeader { get; init; }

    public bool ShowFooter { get; init; }

    public bool ShowFooterTitle { get; init; }

    public bool ShowFooterCount { get; init; }
}

/// <summary>One exported row: a single element (itemized) or a group of elements.</summary>
public sealed class ScheduleExcelRow
{
    public string Key { get; init; } = string.Empty;

    public List<string> UniqueIds { get; } = [];

    public ScheduleExcelValue[] Values { get; set; } = [];

    /// <summary>Per-cell Revit style; null entries when the row was not matched in the schedule view.</summary>
    public ScheduleExcelCellStyle?[] Styles { get; set; } = [];

    /// <summary>Display text of the row's value for each <see cref="ScheduleExcelData.GroupLevels"/> entry.</summary>
    public string[] GroupValues { get; init; } = [];
}

public sealed class ScheduleExcelData
{
    public string ScheduleName { get; init; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public ScheduleExcelCellStyle? TitleStyle { get; set; }

    public string ScheduleUniqueId { get; init; } = string.Empty;

    public string DocumentTitle { get; init; } = string.Empty;

    public string DocumentPath { get; init; } = string.Empty;

    public string DocumentId { get; init; } = string.Empty;

    public bool IsItemized { get; init; }

    public int ElementCount { get; init; }

    public bool ShowGrandTotal { get; init; }

    public string GrandTotalTitle { get; init; } = string.Empty;

    public List<ScheduleExcelGroupLevel> GroupLevels { get; } = [];

    public List<ScheduleExcelColumn> Columns { get; } = [];

    public List<ScheduleExcelRow> Rows { get; } = [];
}
