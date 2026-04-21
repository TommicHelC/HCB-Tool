namespace HCB.RevitAddin.Features.AiFamilyBuilder.Models;

public sealed class AiFamilyBuilderResult
{
    public int AddedParametersCount { get; set; }

    public int SkippedParametersCount { get; set; }

    public int AddedTypesCount { get; set; }

    public int UpdatedValuesCount { get; set; }

    public int ExecutedGeometryOperationsCount { get; set; }

    public int SkippedGeometryOperationsCount { get; set; }

    public List<string> Messages { get; } = [];
}
