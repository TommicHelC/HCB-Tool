using System.Collections.Generic;

namespace HCB.RevitAddin.Features.ParameterService.Models;

public sealed class ParameterServiceResolveResult
{
    public List<ParameterServiceDescriptor> Parameters { get; } = [];

    public List<string> Errors { get; } = [];
}
