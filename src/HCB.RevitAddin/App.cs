using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace HCB.RevitAddin
{
    public class App : IExternalApplication
    {
        private Queue<RibbonPanelDefinition>? _pendingPanels;
        private EventHandler<IdlingEventArgs>? _idlingHandler;
        private bool _tabCreated;

        public Result OnStartup(UIControlledApplication application)
        {
            _pendingPanels = new Queue<RibbonPanelDefinition>(RibbonDefinitions.Create(ParseRevitVersion(application)));
            _idlingHandler = (_, _) => OnApplicationIdling(application);
            application.Idling += _idlingHandler;
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            StopListening(application);
            return Result.Succeeded;
        }

        private void OnApplicationIdling(object? sender)
        {
            if (sender is not UIControlledApplication application || _pendingPanels == null)
            {
                return;
            }

            try
            {
                if (!_tabCreated)
                {
                    RibbonBuilder.EnsureTab(application);
                    _tabCreated = true;
                }

                if (_pendingPanels.Count == 0)
                {
                    _pendingPanels = null;
                    StopListening(application);
                    return;
                }

                RibbonBuilder.CreatePanel(application, _pendingPanels.Dequeue());
            }
            catch
            {
                _pendingPanels = null;
                StopListening(application);
                throw;
            }
        }

        // The ribbon is built once, spread over the first few idle cycles. Leaving the handler
        // attached would run it on every idle event for the rest of the Revit session.
        private void StopListening(UIControlledApplication application)
        {
            if (_idlingHandler == null)
            {
                return;
            }

            application.Idling -= _idlingHandler;
            _idlingHandler = null;
        }

        private static int ParseRevitVersion(UIControlledApplication application)
        {
            return int.TryParse(application.ControlledApplication.VersionNumber, out int version)
                ? version
                : int.MaxValue;
        }
    }
}
