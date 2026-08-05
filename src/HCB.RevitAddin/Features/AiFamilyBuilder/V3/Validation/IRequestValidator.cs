using HCB.RevitAddin.Features.AiFamilyBuilder.Models;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Validation;

/// <summary>
/// Walidacja kontraktu v3 przed jakakolwiek modyfikacja dokumentu (Etap 1).
/// </summary>
public interface IRequestValidator
{
    AiFamilyBuilderValidationResult Validate(AiFamilyBuildRequest request);
}
