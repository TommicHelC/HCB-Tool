using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.RibbonManager.UI;

namespace HCB.RevitAddin.Features.RibbonManager;

[Transaction(TransactionMode.ReadOnly)]
public sealed class RibbonManagerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        RibbonManagerWindow window = new();
        _ = new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
        window.ShowDialog();
        return Result.Succeeded;
    }
}
