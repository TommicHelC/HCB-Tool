namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Wybiera builder domenowy na podstawie <c>builder.name</c>.
/// </summary>
public interface IBuilderResolver
{
    IAiFamilyBuilder Resolve(string builderName);

    bool TryResolve(string builderName, out IAiFamilyBuilder builder);

    IReadOnlyCollection<string> KnownBuilders { get; }
}
