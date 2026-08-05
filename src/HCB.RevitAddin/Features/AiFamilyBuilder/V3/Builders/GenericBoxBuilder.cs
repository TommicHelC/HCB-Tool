using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Builders;

/// <summary>
/// Referencyjny builder startowy: framework parametrow/typow/wartosci + podstawowa geometria
/// (delegowana do wyspecjalizowanych executorow).
/// Faza 5: etapy budowy biegna w osobnych transakcjach (objetych <see cref="TransactionGroup"/> w serwisie),
/// a flex testy uruchamiane sa po frameworku (przed geometria) i koncowo (po geometrii).
/// </summary>
public sealed class GenericBoxBuilder : IAiFamilyBuilder
{
    private readonly IParameterService _parameterService;
    private readonly IGeometryExecutor _geometryExecutor;
    private readonly IConnectorService _connectorService;
    private readonly IFlexTestService _flexTestService;

    public GenericBoxBuilder(
        IParameterService? parameterService = null,
        IGeometryExecutor? geometryExecutor = null,
        IConnectorService? connectorService = null,
        IFlexTestService? flexTestService = null)
    {
        _parameterService = parameterService ?? new V3ParameterService();
        _geometryExecutor = geometryExecutor ?? new CompositeGeometryExecutor(
        [
            new RectangularPrismExecutor(),
            new CircularExtrusionExecutor()
        ]);
        _connectorService = connectorService ?? new DuctConnectorService();
        _flexTestService = flexTestService ?? new FlexTestService();
    }

    public string Name => "GenericBoxBuilder";

    public AiFamilyBuilderResult Build(BuildContext context)
    {
        Document document = context.Document;
        ValidationSpec validation = context.Request.Validation;
        IReadOnlyList<FlexTestSpec> flexTests = context.Request.FlexTests;

        // Etap 2: parametry, typy, wartosci, formuly — osobna transakcja.
        using (Transaction parametersStage = new(document, "AI Family v3: parametry i typy"))
        {
            parametersStage.Start();
            try
            {
                _parameterService.CreateOrReuse(context);
                _parameterService.ApplyFormulas(context);
                _parameterService.ApplyTypeValues(context);
                document.Regenerate();
                parametersStage.Commit();
            }
            catch
            {
                if (parametersStage.GetStatus() == TransactionStatus.Started)
                {
                    parametersStage.RollBack();
                }

                throw;
            }
        }

        // Etap 4: flex frameworku — po parametrach/typach, przed geometria.
        if (validation.RunFrameworkFlex)
        {
            RunFlex(context, flexTests, "framework");
        }

        // Etap 5: geometria — osobna transakcja; bledy pojedynczych operacji sa nieblokujace.
        using (Transaction geometryStage = new(document, "AI Family v3: geometria"))
        {
            geometryStage.Start();
            try
            {
                ExecuteGeometry(context);
                geometryStage.Commit();
            }
            catch
            {
                if (geometryStage.GetStatus() == TransactionStatus.Started)
                {
                    geometryStage.RollBack();
                }

                throw;
            }
        }

        // Etap 6: konektory MEP — osobna transakcja po utworzeniu geometrii.
        if (context.Request.Connectors.Count > 0)
        {
            using Transaction connectorStage = new(document, "AI Family v3: konektory");
            connectorStage.Start();
            try
            {
                _connectorService.Create(context);
                connectorStage.Commit();
            }
            catch
            {
                if (connectorStage.GetStatus() == TransactionStatus.Started)
                {
                    connectorStage.RollBack();
                }

                throw;
            }
        }

        // Etap 7: flex koncowy — po geometrii.
        if (validation.RunFinalFlex)
        {
            RunFlex(context, flexTests, "koncowy");
        }

        return context.Result;
    }

    private void ExecuteGeometry(BuildContext context)
    {
        foreach (GeometrySpec geometry in context.Request.Geometry)
        {
            string kind = geometry.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!_geometryExecutor.CanExecute(kind))
            {
                context.Result.SkippedGeometryOperationsCount++;
                context.Result.Messages.Add($"Geometria '{kind}' nieobslugiwana przez GenericBoxBuilder.");
                continue;
            }

            // Blad geometrii jest nieblokujacy: parametry/typy pozostaja zatwierdzone, a raport wskazuje krok bledu.
            try
            {
                _geometryExecutor.Execute(context, geometry);
            }
            catch (Exception ex)
            {
                context.Result.SkippedGeometryOperationsCount++;
                context.Result.Messages.Add($"Blad geometrii '{geometry.Name}': {ex.Message}");
            }
        }
    }

    private void RunFlex(BuildContext context, IReadOnlyList<FlexTestSpec> flexTests, string phase)
    {
        if (flexTests.Count == 0)
        {
            return;
        }

        FlexTestReport report = _flexTestService.Run(context, flexTests);

        int passed = 0;
        foreach (FlexCaseResult caseResult in report.Cases)
        {
            context.FlexReport.Add($"{phase}: {caseResult.Name}", caseResult.Success, caseResult.Error);
            if (caseResult.Success)
            {
                passed++;
            }
            else
            {
                context.Result.Messages.Add($"Flex ({phase}) '{caseResult.Name}' nie przeszedl: {caseResult.Error}");
            }
        }

        context.Result.Messages.Add($"Flex ({phase}): {passed}/{report.Cases.Count} przypadkow OK.");
    }
}
