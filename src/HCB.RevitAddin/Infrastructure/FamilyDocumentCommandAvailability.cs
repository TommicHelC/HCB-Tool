using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace HCB.RevitAddin;

public sealed class FamilyDocumentCommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        return applicationData.ActiveUIDocument?.Document?.IsFamilyDocument == true;
    }
}
