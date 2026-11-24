using System;
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
        IntPtr revitWindowHandle = commandData.Application.MainWindowHandle;
        RotateFittingsWindow window = new(pickEvent, handler, revitWindowHandle);
        handler.Window = window;
        _ = new WindowInteropHelper(window) { Owner = revitWindowHandle };
        window.Closed += (_, _) =>
        {
            openWindow = null;
            handler.Window = null;
            // A running pick loop is still inside this event; it ends on the ESC the window just sent.
            if (!handler.IsPicking)
            {
                pickEvent.Dispose();
            }
        };

        openWindow = window;
        window.Show();
        window.RequestPicking();
        return Result.Succeeded;
    }
}
