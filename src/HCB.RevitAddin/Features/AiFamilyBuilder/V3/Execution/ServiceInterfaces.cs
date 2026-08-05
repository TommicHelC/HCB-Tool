using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Etap 2: tworzenie/reuse parametrow i typow oraz aplikacja formul i wartosci.
/// </summary>
public interface IParameterService
{
    void CreateOrReuse(BuildContext context);

    void ApplyFormulas(BuildContext context);

    void ApplyTypeValues(BuildContext context);
}

/// <summary>
/// Etap 3: budowa lub reuse frameworku referencyjnego (reference planes, levels, relacje).
/// </summary>
public interface IReferenceService
{
    void CreateOrReuse(BuildContext context);

    void ApplyConstraints(BuildContext context);
}

/// <summary>
/// Etap 5: wykonanie pojedynczej operacji geometrii na zwalidowanym frameworku.
/// </summary>
public interface IGeometryExecutor
{
    bool CanExecute(string kind);

    void Execute(BuildContext context, GeometrySpec spec);
}

/// <summary>
/// Etap 6: tworzenie funkcjonalnych konektorow MEP na utworzonej geometrii.
/// </summary>
public interface IConnectorService
{
    void Create(BuildContext context);
}

/// <summary>
/// Etap 4 i 7: flex testy frameworku oraz koncowe.
/// </summary>
public interface IFlexTestService
{
    FlexTestReport Run(BuildContext context, IReadOnlyList<FlexTestSpec> tests);
}
