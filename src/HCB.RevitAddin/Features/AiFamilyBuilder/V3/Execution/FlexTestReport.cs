namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Wynik pojedynczego przypadku flex testu.
/// </summary>
public sealed class FlexCaseResult
{
    public required string Name { get; init; }

    public bool Success { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// Zbiorczy raport z flex testow (framework flex oraz final flex).
/// </summary>
public sealed class FlexTestReport
{
    public List<FlexCaseResult> Cases { get; } = [];

    public bool Success => Cases.All(c => c.Success);

    public void Add(string name, bool success, string? error = null)
    {
        Cases.Add(new FlexCaseResult { Name = name, Success = success, Error = error });
    }
}
