using System.Collections.Generic;

namespace HCB.RevitAddin.Features.ParameterCalculator.Models;

public sealed class ParameterCalculatorOptions
{
    public string TargetParameterName { get; init; } = string.Empty;

    public int DecimalPlaces { get; init; }

    public IReadOnlyList<FormulaOperand> Operands { get; init; } = [];
}
