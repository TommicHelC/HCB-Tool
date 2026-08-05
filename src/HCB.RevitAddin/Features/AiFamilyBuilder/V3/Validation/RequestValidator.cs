using System;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Validation;

/// <summary>
/// Realizuje liste kontrolna Etapu 1 z wytycznych v3. Czysty C# bez referencji do Revita.
/// Jezeli walidacja nie przejdzie, silnik nie uruchamia zadnej modyfikacji dokumentu.
/// </summary>
public sealed class RequestValidator : IRequestValidator
{
    private static readonly HashSet<string> SupportedSources =
        new(StringComparer.OrdinalIgnoreCase) { "family", "shared" };

    private static readonly HashSet<string> SupportedDataTypes =
        new(StringComparer.OrdinalIgnoreCase) { "text", "yesno", "integer", "number", "length", "area", "volume", "angle", "material" };

    private static readonly HashSet<string> SupportedScopes =
        new(StringComparer.OrdinalIgnoreCase) { "type", "instance" };

    private static readonly HashSet<string> SupportedReferenceKinds =
        new(StringComparer.OrdinalIgnoreCase) { "plane", "level" };

    private static readonly HashSet<string> SupportedOrientations =
        new(StringComparer.OrdinalIgnoreCase) { "vertical", "horizontal" };

    private static readonly HashSet<string> SupportedConstraintKinds =
        new(StringComparer.OrdinalIgnoreCase) { "dimension", "equality" };

    private static readonly HashSet<string> SupportedConnectorDomains =
        new(StringComparer.OrdinalIgnoreCase) { "hvac", "piping", "electrical" };

    private static readonly HashSet<string> SupportedConnectorShapes =
        new(StringComparer.OrdinalIgnoreCase) { "rectangular", "round", "oval" };

    private readonly HashSet<string>? _knownBuilders;

    public RequestValidator(IEnumerable<string>? knownBuilders = null)
    {
        _knownBuilders = knownBuilders is null
            ? null
            : new HashSet<string>(knownBuilders, StringComparer.OrdinalIgnoreCase);
    }

    public AiFamilyBuilderValidationResult Validate(AiFamilyBuildRequest request)
    {
        AiFamilyBuilderValidationResult result = new();

        if (!string.Equals(request.Schema, AiFamilyBuildRequest.SchemaId, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add($"Nieobslugiwany schema: '{request.Schema}'. Oczekiwano '{AiFamilyBuildRequest.SchemaId}'.");
        }

        ValidateBuilder(request, result);

        HashSet<string> parameterNames = ValidateParameters(request, result);
        ValidateParameterRules(request, parameterNames, result);
        ValidateTypes(request, parameterNames, result);

        HashSet<string> referenceNames = ValidateReferences(request, result);
        ValidateConstraints(request, parameterNames, referenceNames, result);

        HashSet<string> geometryNames = ValidateGeometry(request, parameterNames, referenceNames, result);
        ValidateConnectors(request, parameterNames, geometryNames, referenceNames, result);
        ValidateFlexTests(request, parameterNames, result);

        return result;
    }

    private void ValidateBuilder(AiFamilyBuildRequest request, AiFamilyBuilderValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(request.Builder.Name))
        {
            result.Errors.Add("Sekcja 'builder' jest obowiazkowa i wymaga pola 'name'.");
            return;
        }

        if (_knownBuilders != null && !_knownBuilders.Contains(request.Builder.Name))
        {
            result.Errors.Add($"Nieobslugiwany builder: '{request.Builder.Name}'.");
        }
    }

    private static HashSet<string> ValidateParameters(AiFamilyBuildRequest request, AiFamilyBuilderValidationResult result)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (ParameterSpec parameter in request.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                result.Errors.Add("Znaleziono parametr bez nazwy.");
                continue;
            }

            if (!names.Add(parameter.Name))
            {
                result.Errors.Add($"Powielona nazwa parametru: {parameter.Name}");
            }

            if (!SupportedSources.Contains(parameter.Source))
            {
                result.Errors.Add($"Nieobslugiwany source '{parameter.Source}' dla parametru {parameter.Name}.");
            }

            if (string.Equals(parameter.Source, "family", StringComparison.OrdinalIgnoreCase) &&
                !SupportedDataTypes.Contains(parameter.DataType))
            {
                result.Errors.Add($"Nieobslugiwany data_type '{parameter.DataType}' dla parametru {parameter.Name}.");
            }

            if (!SupportedScopes.Contains(parameter.Scope))
            {
                result.Errors.Add($"Nieobslugiwany scope '{parameter.Scope}' dla parametru {parameter.Name}.");
            }
        }

        return names;
    }

    private static void ValidateParameterRules(AiFamilyBuildRequest request, IReadOnlySet<string> parameterNames, AiFamilyBuilderValidationResult result)
    {
        foreach (ParameterRuleSpec rule in request.ParameterRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name) || !parameterNames.Contains(rule.Name))
            {
                result.Errors.Add($"Regula parametru odwoluje sie do nieistniejacego parametru: '{rule.Name}'.");
                continue;
            }

            if (rule.Min.HasValue && rule.Max.HasValue && rule.Min.Value > rule.Max.Value)
            {
                result.Errors.Add($"Regula parametru '{rule.Name}' ma min wieksze niz max.");
            }

            if (rule.Step.HasValue && rule.Step.Value <= 0)
            {
                result.Warnings.Add($"Regula parametru '{rule.Name}' ma niedodatni step.");
            }
        }
    }

    private static void ValidateTypes(AiFamilyBuildRequest request, IReadOnlySet<string> parameterNames, AiFamilyBuilderValidationResult result)
    {
        HashSet<string> typeNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (TypeSpec type in request.Types)
        {
            if (string.IsNullOrWhiteSpace(type.Name))
            {
                result.Errors.Add("Znaleziono typ bez nazwy.");
                continue;
            }

            if (!typeNames.Add(type.Name))
            {
                result.Errors.Add($"Powielona nazwa typu: {type.Name}");
            }

            foreach (string valueName in type.Values.Keys)
            {
                if (!parameterNames.Contains(valueName))
                {
                    result.Errors.Add($"Typ '{type.Name}' ustawia parametr, ktory nie jest zadeklarowany: {valueName}");
                }
            }
        }
    }

    private static HashSet<string> ValidateReferences(AiFamilyBuildRequest request, AiFamilyBuilderValidationResult result)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (ReferenceSpec reference in request.References)
        {
            if (string.IsNullOrWhiteSpace(reference.Name))
            {
                result.Errors.Add("Znaleziono referencje bez nazwy.");
                continue;
            }

            if (!names.Add(reference.Name))
            {
                result.Errors.Add($"Powielona nazwa referencji: {reference.Name}");
            }

            if (!SupportedReferenceKinds.Contains(reference.Kind))
            {
                result.Errors.Add($"Nieobslugiwany kind referencji '{reference.Kind}' dla {reference.Name}.");
            }

            if (!string.IsNullOrWhiteSpace(reference.Orientation) && !SupportedOrientations.Contains(reference.Orientation))
            {
                result.Errors.Add($"Nieobslugiwana orientacja '{reference.Orientation}' dla referencji {reference.Name}.");
            }
        }

        return names;
    }

    private static void ValidateConstraints(
        AiFamilyBuildRequest request,
        IReadOnlySet<string> parameterNames,
        IReadOnlySet<string> referenceNames,
        AiFamilyBuilderValidationResult result)
    {
        foreach (ConstraintSpec constraint in request.Constraints)
        {
            if (!SupportedConstraintKinds.Contains(constraint.Kind))
            {
                result.Errors.Add($"Nieobslugiwany kind constraintu '{constraint.Kind}' dla {constraint.Name}.");
                continue;
            }

            if (string.Equals(constraint.Kind, "dimension", StringComparison.OrdinalIgnoreCase))
            {
                if (constraint.Between.Count != 2)
                {
                    result.Errors.Add($"Constraint wymiarowy '{constraint.Name}' wymaga dokladnie dwoch referencji w 'between'.");
                }

                foreach (string reference in constraint.Between)
                {
                    if (!referenceNames.Contains(reference))
                    {
                        result.Errors.Add($"Constraint '{constraint.Name}' odwoluje sie do nieistniejacej referencji '{reference}'.");
                    }
                }

                if (string.IsNullOrWhiteSpace(constraint.Parameter) || !parameterNames.Contains(constraint.Parameter))
                {
                    result.Errors.Add($"Constraint wymiarowy '{constraint.Name}' wymaga parametru z sekcji parameters.");
                }
            }
            else
            {
                if (constraint.Chain.Count < 2)
                {
                    result.Errors.Add($"Constraint rownosci '{constraint.Name}' wymaga co najmniej dwoch referencji w 'chain'.");
                }

                foreach (string reference in constraint.Chain)
                {
                    if (!referenceNames.Contains(reference))
                    {
                        result.Errors.Add($"Constraint '{constraint.Name}' odwoluje sie do nieistniejacej referencji '{reference}'.");
                    }
                }
            }
        }
    }

    private static HashSet<string> ValidateGeometry(
        AiFamilyBuildRequest request,
        IReadOnlySet<string> parameterNames,
        IReadOnlySet<string> referenceNames,
        AiFamilyBuilderValidationResult result)
    {
        HashSet<string> geometryNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (GeometrySpec geometry in request.Geometry)
        {
            if (string.IsNullOrWhiteSpace(geometry.Kind))
            {
                result.Errors.Add($"Operacja geometrii '{geometry.Name}' nie ma pola 'kind'.");
            }

            if (!string.IsNullOrWhiteSpace(geometry.Name))
            {
                geometryNames.Add(geometry.Name);
            }

            ValidateValueRef(geometry.Width, $"geometry '{geometry.Name}'.width", parameterNames, referenceNames, result);
            ValidateValueRef(geometry.Height, $"geometry '{geometry.Name}'.height", parameterNames, referenceNames, result);
            ValidateValueRef(geometry.Depth, $"geometry '{geometry.Name}'.depth", parameterNames, referenceNames, result);
            ValidateValueRef(geometry.Radius, $"geometry '{geometry.Name}'.radius", parameterNames, referenceNames, result);

            if (geometry.Offsets != null)
            {
                foreach ((string key, ValueRefSpec offset) in geometry.Offsets)
                {
                    ValidateValueRef(offset, $"geometry '{geometry.Name}'.offsets['{key}']", parameterNames, referenceNames, result);
                }
            }
        }

        return geometryNames;
    }

    private static void ValidateConnectors(
        AiFamilyBuildRequest request,
        IReadOnlySet<string> parameterNames,
        IReadOnlySet<string> geometryNames,
        IReadOnlySet<string> referenceNames,
        AiFamilyBuilderValidationResult result)
    {
        HashSet<string> connectorNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (ConnectorSpec connector in request.Connectors)
        {
            if (!string.IsNullOrWhiteSpace(connector.Name))
            {
                connectorNames.Add(connector.Name);
            }
        }

        foreach (ConnectorSpec connector in request.Connectors)
        {
            if (!SupportedConnectorDomains.Contains(connector.Domain))
            {
                result.Errors.Add($"Konektor '{connector.Name}' ma nieobslugiwany domain '{connector.Domain}'.");
            }

            if (!SupportedConnectorShapes.Contains(connector.Shape))
            {
                result.Errors.Add($"Konektor '{connector.Name}' ma nieobslugiwany shape '{connector.Shape}'.");
            }

            ConnectorHostSpec? host = connector.Host;
            if (host == null)
            {
                result.Errors.Add($"Konektor '{connector.Name}' nie ma sekcji 'host'.");
            }
            else
            {
                bool hostsGeometry = !string.IsNullOrWhiteSpace(host.Geometry) && geometryNames.Contains(host.Geometry);
                bool hostsWorkPlane = !string.IsNullOrWhiteSpace(host.WorkPlane) && referenceNames.Contains(host.WorkPlane);
                if (!hostsGeometry && !hostsWorkPlane)
                {
                    result.Errors.Add($"Konektor '{connector.Name}' musi byc hostowany na istniejacej geometrii lub work plane/referencji.");
                }
            }

            if (connector.Size != null)
            {
                ValidateValueRef(connector.Size.Width, $"connector '{connector.Name}'.size.width", parameterNames, referenceNames, result);
                ValidateValueRef(connector.Size.Height, $"connector '{connector.Name}'.size.height", parameterNames, referenceNames, result);
                ValidateValueRef(connector.Size.Diameter, $"connector '{connector.Name}'.size.diameter", parameterNames, referenceNames, result);
            }

            if (!string.IsNullOrWhiteSpace(connector.LinkedTo) && !connectorNames.Contains(connector.LinkedTo))
            {
                result.Errors.Add($"Konektor '{connector.Name}' linkuje do nieistniejacego konektora '{connector.LinkedTo}'.");
            }
        }
    }

    private static void ValidateFlexTests(AiFamilyBuildRequest request, IReadOnlySet<string> parameterNames, AiFamilyBuilderValidationResult result)
    {
        HashSet<string> typeNames = new(request.Types.Select(type => type.Name), StringComparer.OrdinalIgnoreCase);
        foreach (FlexTestSpec test in request.FlexTests)
        {
            bool hasType = !string.IsNullOrWhiteSpace(test.TypeName);
            if (hasType && !typeNames.Contains(test.TypeName!))
            {
                result.Errors.Add($"Flex test '{test.Name}' odwoluje sie do nieistniejacego typu '{test.TypeName}'.");
            }

            if (!hasType && test.Values.Count == 0)
            {
                result.Errors.Add($"Flex test '{test.Name}' musi wskazywac 'type_name' albo zestaw 'values'.");
            }

            foreach (string valueName in test.Values.Keys)
            {
                if (!parameterNames.Contains(valueName))
                {
                    result.Errors.Add($"Flex test '{test.Name}' ustawia nieznany parametr '{valueName}'.");
                }
            }
        }
    }

    private static void ValidateValueRef(
        ValueRefSpec? spec,
        string context,
        IReadOnlySet<string> parameterNames,
        IReadOnlySet<string> referenceNames,
        AiFamilyBuilderValidationResult result)
    {
        if (spec == null || spec.IsEmpty)
        {
            return;
        }

        int filled = (spec.HasRef ? 1 : 0) + (spec.HasExpr ? 1 : 0) + (spec.HasValue ? 1 : 0);
        if (filled > 1)
        {
            result.Errors.Add($"{context}: nalezy podac dokladnie jedno z pol 'ref', 'expr', 'value'.");
            return;
        }

        if (spec.HasRef)
        {
            if (!parameterNames.Contains(spec.Ref!) && !referenceNames.Contains(spec.Ref!))
            {
                result.Errors.Add($"{context}: 'ref' wskazuje na nieznany parametr/referencje '{spec.Ref}'.");
            }

            return;
        }

        if (spec.HasExpr)
        {
            if (!ExpressionParser.TryValidate(spec.Expr, out IReadOnlyCollection<string> identifiers, out string? error))
            {
                result.Errors.Add($"{context}: niepoprawne 'expr' ({error}).");
                return;
            }

            foreach (string identifier in identifiers)
            {
                if (!parameterNames.Contains(identifier))
                {
                    result.Errors.Add($"{context}: 'expr' uzywa nieznanego parametru '{identifier}'.");
                }
            }
        }
    }
}
