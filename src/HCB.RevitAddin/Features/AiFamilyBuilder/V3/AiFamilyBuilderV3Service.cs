using System;
using System.Text.Json;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Builders;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Validation;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3;

/// <summary>
/// Punkt wejscia sciezki v3: parsuje JSON, waliduje (Etap 1), wybiera builder domenowy i uruchamia go.
/// Faza 5: builder biegnie w <see cref="TransactionGroup"/>; etapy (parametry, geometria) maja wlasne transakcje,
/// flex testy biegna w osobnych, wycofywanych transakcjach.
/// </summary>
public sealed class AiFamilyBuilderV3Service
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IBuilderResolver _resolver;
    private readonly IRequestValidator _validator;

    public AiFamilyBuilderV3Service()
    {
        IAiFamilyBuilder[] builders =
        [
            new GenericBoxBuilder()
        ];

        _resolver = new BuilderResolver(builders);
        _validator = new RequestValidator(_resolver.KnownBuilders);
    }

    public AiFamilyBuilderResult Apply(Document document, Application application, AiFamilyBuilderOptions options)
    {
        if (!document.IsFamilyDocument)
        {
            throw new InvalidOperationException("Aktywny dokument nie jest rodzina.");
        }

        AiFamilyBuildRequest request = Parse(options.JsonText);

        AiFamilyBuilderValidationResult validation = _validator.Validate(request);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join("\n", validation.Errors));
        }

        IAiFamilyBuilder builder = _resolver.Resolve(request.Builder.Name);
        BuildContext context = new()
        {
            Document = document,
            Application = application,
            Request = request,
            TypeMapper = new RevitTypeMapper()
        };

        // Grupa transakcji obejmuje etapy buildera (parametry, geometria) jako jeden krok cofania (Undo).
        // Builder zarzadza wlasnymi transakcjami per etap; flex testy biegna w osobnych, wycofywanych transakcjach.
        using TransactionGroup group = new(document, "AI Family Builder v3");
        group.Start();
        try
        {
            builder.Build(context);
            group.Assimilate();
        }
        catch
        {
            if (group.GetStatus() == TransactionStatus.Started)
            {
                group.RollBack();
            }

            throw;
        }

        return context.Result;
    }

    /// <summary>Walidacja bez modyfikacji dokumentu (na potrzeby UI). Nie rzuca przy bledzie JSON.</summary>
    public AiFamilyBuilderValidationResult Validate(string jsonText)
    {
        AiFamilyBuilderValidationResult result = new();
        AiFamilyBuildRequest request;
        try
        {
            request = Parse(jsonText);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Niepoprawny JSON: {ex.Message}");
            return result;
        }

        return _validator.Validate(request);
    }

    private static AiFamilyBuildRequest Parse(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            throw new InvalidOperationException("Wklej kod JSON.");
        }

        AiFamilyBuildRequest? request = JsonSerializer.Deserialize<AiFamilyBuildRequest>(jsonText, JsonOptions);
        if (request == null)
        {
            throw new InvalidOperationException("Nie udalo sie odczytac JSON.");
        }

        return request;
    }
}
