namespace HCB.RevitAddin.Features.ScheduleExcel;

/// <summary>
/// Layout of the HCB schedule workbook. The sync step (stage 2) reads the file back with the same constants,
/// so change them only together with <see cref="FormatVersion"/>.
/// </summary>
internal static class ScheduleExcelFormat
{
    /// <summary>v3: several schedules per workbook (per-schedule metadata sheets "_hcb_meta_1"...).</summary>
    public const int FormatVersion = 3;

    /// <summary>Oldest file layout this version can sync (v2 added the lock column; v2 = one schedule).</summary>
    public const int MinFormatVersion = 2;

    /// <summary>Per-schedule hidden sheet name: "_hcb_fields_2" for the second schedule.</summary>
    public static string PartSheet(string baseName, int index) => $"{baseName}_{index}";

    public static string PartTable(int index) => $"{TableName}_{index}";
    public const string ToolId = "HCB.ScheduleExcel";

    public const string LegendSheet = "Legenda";
    public const string MetaSheet = "_hcb_meta";
    public const string FieldsSheet = "_hcb_fields";
    public const string RowsSheet = "_hcb_rows";
    public const string SnapshotSheet = "_hcb_snapshot";

    /// <summary>Very hidden sheet holding drop-down values (types, levels...) for reference columns.</summary>
    public const string ListsSheet = "_hcb_lists";

    /// <summary>Hidden first column on the data sheet: the row key that links a row to its elements.</summary>
    public const string KeyHeader = "HCB_KEY";
    public const int KeyColumn = 1;

    /// <summary>Hidden second column: per-row mask, '1' = cell is read only (moves with the row when sorted).</summary>
    public const string LockHeader = "HCB_LOCK";
    public const int LockColumn = 2;
    public const int FirstValueColumn = 3;
    public const int TitleRow = 1;
    /// <summary>Row above the headings saying per column: editable / type parameter / read only.</summary>
    public const int EditStateRow = 2;
    public const int HeaderRow = 3;
    public const int FirstDataRow = 4;

    /// <summary>Excel table (ListObject) holding headings, rows and the native totals row.</summary>
    public const string TableName = "HCB_Dane";

    /// <summary>Snapshot marker for a grouped row whose elements have different values.</summary>
    public const string MixedMarker = "<HCB_MIXED>";

    public const int MaxCellText = 32767;
}
