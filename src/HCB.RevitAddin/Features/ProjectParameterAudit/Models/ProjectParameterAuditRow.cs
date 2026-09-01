namespace HCB.RevitAddin.Features.ProjectParameterAudit.Models;

public sealed class ProjectParameterAuditRow
{
    public string ParameterName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string ExpectedSpec { get; init; } = string.Empty;

    public string FoundSpec { get; init; } = string.Empty;

    public string ExpectedGuid { get; init; } = string.Empty;

    public string FoundGuids { get; init; } = string.Empty;

    public string BindingKind { get; init; } = string.Empty;

    public string GroupTypeId { get; init; } = string.Empty;

    public string Categories { get; init; } = string.Empty;

    public int BoundCategoryCount { get; init; }

    public int ElementsWithParameter { get; init; }

    public int ElementsWithValue { get; init; }

    public string StorageTypes { get; init; } = string.Empty;

    public int ScheduleCount { get; init; }

    public string Schedules { get; init; } = string.Empty;

    public int FilterCount { get; init; }

    public string Filters { get; init; } = string.Empty;

    public string Recommendation { get; init; } = string.Empty;
}
