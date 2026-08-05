namespace HCB.RevitAddin.Features.ParameterCopy.Models;

public sealed class ParameterCopyResult
{
    public int ScopedElementCount { get; set; }

    public int CopiedCount { get; set; }

    public int MissingSourceCount { get; set; }

    public int MissingTargetCount { get; set; }

    public int ReadOnlyTargetCount { get; set; }

    public int EmptySourceCount { get; set; }

    public int IncompatibleTypeCount { get; set; }

    public int FailedSetCount { get; set; }

    public int SkippedCount => ScopedElementCount - CopiedCount;
}
