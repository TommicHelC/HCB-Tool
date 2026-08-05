using System.Collections.Generic;

namespace HCB.RevitAddin.Features.AccessoryTerminalNumbering.Models;

public sealed class AccessoryTerminalNumberingResult
{
    public int SystemsCount { get; set; }

    public int DuctAccessoryCount { get; set; }

    public int TotalCount { get; set; }

    public int SharedNumberCount { get; set; }

    public string TargetParameterName { get; set; } = string.Empty;

    public List<string> Messages { get; } = [];
}
