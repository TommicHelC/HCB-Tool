using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.HvacTagging.UI;

namespace HCB.RevitAddin.Features.HvacTagging;

[Transaction(TransactionMode.Manual)]
public sealed class HvacTaggingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uiDocument = commandData.Application.ActiveUIDocument;
        Document document = uiDocument.Document;

        HvacTaggingWindow window = new(document, uiDocument.ActiveView);
        return window.ShowDialog() == true ? Result.Succeeded : Result.Cancelled;
    }
}
