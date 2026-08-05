namespace HCB.RevitAddin.Features.AccessoryTerminalNumbering.Models;

public sealed class AccessoryTerminalNumberingOptions
{
    public string TargetParameterName { get; set; } = string.Empty;

    public int StartNumber { get; set; } = 1;

    public bool UseLastProjectNumber { get; set; }
}
