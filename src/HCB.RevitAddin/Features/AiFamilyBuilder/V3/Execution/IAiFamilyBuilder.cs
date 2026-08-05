using HCB.RevitAddin.Features.AiFamilyBuilder.Models;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Builder domenowy. Zna poprawna kolejnosc dzialan Revit API dla swojej klasy rodzin.
/// JSON wybiera builder i dostarcza dane; logika rodziny zyje tutaj.
/// </summary>
public interface IAiFamilyBuilder
{
    /// <summary>Nazwa zgodna z <c>builder.name</c> w JSON.</summary>
    string Name { get; }

    /// <summary>
    /// Buduje rodzine. Wywolywany wewnatrz <see cref="Autodesk.Revit.DB.TransactionGroup"/> otwartej przez serwis,
    /// ale BEZ otwartej transakcji — builder zarzadza wlasnymi transakcjami per etap (parametry, geometria, flex).
    /// </summary>
    AiFamilyBuilderResult Build(BuildContext context);
}
