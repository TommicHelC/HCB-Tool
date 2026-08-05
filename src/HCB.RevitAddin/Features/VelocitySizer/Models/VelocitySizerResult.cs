using System.Collections.Generic;

namespace HCB.RevitAddin.Features.VelocitySizer.Models;

public sealed class VelocitySizerResult
{
    public int UpdatedCount { get; set; }

    public int SkippedExistingValuesCount { get; set; }

    public int MissingParameterCount { get; set; }

    public int FailedCount { get; set; }

    public List<string> Messages { get; } = [];
}
