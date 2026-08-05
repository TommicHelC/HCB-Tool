namespace HCB.RevitAddin.Features.ParameterClear.Models;

public sealed class ParameterClearResult
{
    public int ScopedElementCount { get; set; }

    public int TargetCount { get; set; }

    public int ClearedCount { get; set; }

    public int AlreadyEmptyCount { get; set; }

    public int SkippedCount { get; set; }
}
