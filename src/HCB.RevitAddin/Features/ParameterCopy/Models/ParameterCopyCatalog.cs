using System;

namespace HCB.RevitAddin.Features.ParameterCopy.Models;

public sealed class ParameterCopyCatalog
{
    public int ElementCount { get; init; }

    public IReadOnlyList<string> SourceParameterNames { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> CompatibleTargetNamesBySource { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.CurrentCultureIgnoreCase);
}
