namespace HCB.RevitAddin.Features.AiFamilyBuilder.Models;

public sealed class AiFamilyBuilderValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];
}
