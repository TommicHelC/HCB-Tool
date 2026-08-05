using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3.Schema;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.V3.Execution;

/// <summary>
/// Etap 4 i 7: nieblokujace flex testy rodziny. Kazdy przypadek aktywuje wskazany typ lub ustawia
/// wartosci parametrow, wykonuje <see cref="Document.Regenerate"/> i zbiera ewentualny blad.
/// Wszystkie zmiany sa wycofywane (transakcja zawsze RollBack) — flex jest walidacja, nie modyfikuje rodziny.
/// </summary>
public sealed class FlexTestService : IFlexTestService
{
    public FlexTestReport Run(BuildContext context, IReadOnlyList<FlexTestSpec> tests)
    {
        FlexTestReport report = new();
        if (tests.Count == 0)
        {
            return report;
        }

        FamilyManager familyManager = context.Document.FamilyManager;
        FamilyType? originalType = familyManager.CurrentType;

        foreach (FlexTestSpec test in tests)
        {
            RunCase(context, familyManager, test, report);
        }

        // Defensywnie: przywroc aktywny typ na wypadek, gdyby ktorys przypadek nie wycofal sie czysto.
        if (originalType != null && familyManager.CurrentType?.Name != originalType.Name)
        {
            using Transaction restore = new(context.Document, "flex: restore type");
            restore.Start();
            try
            {
                familyManager.CurrentType = originalType;
                restore.Commit();
            }
            catch
            {
                if (restore.GetStatus() == TransactionStatus.Started)
                {
                    restore.RollBack();
                }
            }
        }

        return report;
    }

    private static void RunCase(BuildContext context, FamilyManager familyManager, FlexTestSpec test, FlexTestReport report)
    {
        Document document = context.Document;
        string name = string.IsNullOrWhiteSpace(test.Name) ? "(bez nazwy)" : test.Name;

        using Transaction transaction = new(document, $"flex: {name}");
        transaction.Start();
        try
        {
            if (!string.IsNullOrWhiteSpace(test.TypeName))
            {
                FamilyType? target = familyManager.Types
                    .Cast<FamilyType>()
                    .FirstOrDefault(type => string.Equals(type.Name, test.TypeName, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                {
                    report.Add(name, false, $"Brak typu '{test.TypeName}' do aktywacji.");
                    return;
                }

                familyManager.CurrentType = target;
            }

            foreach ((string parameterName, JsonElement valueElement) in test.Values)
            {
                if (!context.Parameters.TryGet(parameterName, out FamilyParameter parameter))
                {
                    report.Add(name, false, $"Brak parametru '{parameterName}' w rodzinie.");
                    return;
                }

                if (parameter.IsDeterminedByFormula)
                {
                    report.Add(name, false, $"Parametr '{parameterName}' jest sterowany formula — nie mozna ustawic wartosci.");
                    return;
                }

                if (!FamilyValueWriter.TrySet(familyManager, parameter, valueElement, context.TypeMapper))
                {
                    report.Add(name, false, $"Nie udalo sie ustawic wartosci parametru '{parameterName}'.");
                    return;
                }
            }

            document.Regenerate();
            report.Add(name, true);
        }
        catch (Exception ex)
        {
            report.Add(name, false, ex.Message);
        }
        finally
        {
            // Flex nie modyfikuje rodziny: zawsze wycofujemy zmiany testowe.
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }
        }
    }
}
