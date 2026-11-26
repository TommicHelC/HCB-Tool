using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Features.RibbonManager.Models;

namespace HCB.RevitAddin.Features.RibbonManager;

/// <summary>One-click toggle: short / full titles for every ribbon tab and panel, native ones included.</summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class RibbonShortNamesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        RibbonLayoutConfig config = RibbonLayoutService.Current.Clone();
        // Mixed state (only one enabled in Ribbon Manager) counts as "on", so one click restores full names.
        bool shorten = !(config.ShortPanelNames || config.ShortTabNames);
        config.ShortPanelNames = shorten;
        config.ShortTabNames = shorten;

        try
        {
            RibbonLayoutService.Apply(config);
            RibbonLayoutStore.Save(config);
        }
        catch (Exception exception)
        {
            message = "Nie udalo sie zmienic nazw kart i paneli: " + exception.Message;
            return Result.Failed;
        }

        return Result.Succeeded;
    }
}
