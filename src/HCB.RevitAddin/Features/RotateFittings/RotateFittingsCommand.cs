using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.RotateFittings.UI;

namespace HCB.RevitAddin.Features.RotateFittings;

[Transaction(TransactionMode.Manual)]
public sealed class RotateFittingsCommand : IExternalCommand
{
    private static RotateFittingsWindow? openWindow;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
        if (uiDocument?.Document == null || uiDocument.Document.IsFamilyDocument)
        {
            TaskDialog.Show("Rotate Fittings", "To narzedzie wymaga otwartego projektu.");
            return Result.Succeeded;
        }

        if (openWindow != null)
        {
            openWindow.Activate();
            openWindow.RequestPicking();
            return Result.Succeeded;
        }

        RotateFittingsPickHandler handler = new();
        ExternalEvent pickEvent = ExternalEvent.Create(handler);
        RotateFittingsWindow window = new(pickEvent, handler);
        handler.Window = window;
        _ = new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
        window.Closed += (_, _) =>
        {
            openWindow = null;
            handler.Window = null;
            pickEvent.Dispose();
        };

        openWindow = window;
        window.Show();
        window.RequestPicking();
        return Result.Succeeded;
    }
}
