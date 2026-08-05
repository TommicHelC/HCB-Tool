using System;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Rejestr builderow domenowych. JSON -> resolver buildera -> builder domenowy -> Revit API.
/// </summary>
public sealed class BuilderResolver : IBuilderResolver
{
    private readonly Dictionary<string, IAiFamilyBuilder> _builders;

    public BuilderResolver(IEnumerable<IAiFamilyBuilder> builders)
    {
        _builders = builders.ToDictionary(builder => builder.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> KnownBuilders => _builders.Keys;

    public IAiFamilyBuilder Resolve(string builderName)
    {
        if (!_builders.TryGetValue(builderName, out IAiFamilyBuilder? builder))
        {
            throw new InvalidOperationException($"Nieobslugiwany builder: {builderName}");
        }

        return builder;
    }

    public bool TryResolve(string builderName, out IAiFamilyBuilder builder)
    {
        return _builders.TryGetValue(builderName, out builder!);
    }
}
