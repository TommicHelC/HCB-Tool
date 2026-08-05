using System.Collections.Generic;

namespace HCB.RevitAddin.Features.ParameterClear.Models;

public sealed class ParameterClearCatalog
{
    public IReadOnlyList<string> ModelParameterNames { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> CategoryParameterNames { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();
}
