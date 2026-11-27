namespace HCB.RevitAddin.Features.ScheduleExcel.Models;

public sealed class ScheduleExcelExportOptions
{
    public string FilePath { get; set; } = string.Empty;

    public bool OpenAfterExport { get; set; } = true;

    /// <summary>Locks read-only cells. Excel then blocks sorting ranges that contain locked cells.</summary>
    public bool ProtectReadOnlyCells { get; set; }
}
