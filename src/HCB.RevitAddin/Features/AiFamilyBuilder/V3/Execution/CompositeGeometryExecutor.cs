using System;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Dispatchuje operacje geometrii do wyspecjalizowanych wykonawcow.
/// </summary>
public sealed class CompositeGeometryExecutor(IEnumerable<IGeometryExecutor> executors) : IGeometryExecutor
{
    private readonly IReadOnlyList<IGeometryExecutor> _executors = executors.ToList();

    public bool CanExecute(string kind) => _executors.Any(executor => executor.CanExecute(kind));

    public void Execute(BuildContext context, GeometrySpec spec)
    {
        string kind = spec.Kind?.Trim() ?? string.Empty;
        IGeometryExecutor? executor = _executors.FirstOrDefault(candidate => candidate.CanExecute(kind));
        if (executor == null)
        {
            throw new InvalidOperationException($"Nieobslugiwana geometria: {kind}");
        }

        executor.Execute(context, spec);
    }
}
