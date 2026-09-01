using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using HCB.RevitAddin.Features.UnhideAllElements.Models;

namespace HCB.RevitAddin.Features.UnhideAllElements;

public sealed class UnhideAllElementsService
{
    public UnhideAllElementsResult Execute(Document document, View view)
    {
        // Only elements that are really hidden in this view are worth touching. Handing the whole
        // model to View.UnhideElements builds a huge undo record and reports a candidate count that
        // has nothing to do with what was hidden.
        List<ElementId> candidates = [];
        foreach (Element element in new FilteredElementCollector(document).WhereElementIsNotElementType())
        {
            try
            {
                if (element.IsHidden(view))
                {
                    candidates.Add(element.Id);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // The element cannot report its visibility in this view; there is nothing to unhide.
            }
        }

        UnhideAllElementsResult result = new()
        {
            CandidateCount = candidates.Count
        };

        if (candidates.Count == 0)
        {
            return result;
        }

        using Transaction transaction = new(document, "Unhide All Elements");
        transaction.Start();
        view.UnhideElements(candidates);
        transaction.Commit();

        result.UnhiddenCount = candidates.Count;
        return result;
    }
}
