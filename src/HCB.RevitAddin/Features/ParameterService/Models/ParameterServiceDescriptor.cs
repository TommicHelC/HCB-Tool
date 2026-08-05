namespace HCB.RevitAddin.Features.ParameterService.Models;

public sealed class ParameterServiceDescriptor
{
    public string ParameterTypeId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string GroupLabel { get; init; } = string.Empty;

    public string BindingLabel { get; init; } = string.Empty;

    public string CategoriesSummary { get; init; } = string.Empty;

    public bool ExistsInDocument { get; init; }

    public bool Visible { get; init; }
}
