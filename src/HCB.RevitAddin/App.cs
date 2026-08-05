using System.Collections.Generic;
using Autodesk.Revit.UI;
using HCB.RevitAddin.Infrastructure.Mcp;

namespace HCB.RevitAddin
{
    public class App : IExternalApplication
    {
        private Queue<RibbonPanelDefinition>? _pendingPanels;
        private bool _tabCreated;

        // MCP — most między Claude CLI a Revitem
        private McpExternalEventHandler? _mcpEventHandler;
        private ExternalEvent? _mcpExternalEvent;
        private McpHttpServer? _mcpHttpServer;

        public Result OnStartup(UIControlledApplication application)
        {
            _pendingPanels = new Queue<RibbonPanelDefinition>(RibbonDefinitions.Create(ParseRevitVersion(application)));
            application.Idling += (sender, _) => OnApplicationIdling(application);

            // Inicjalizacja serwera MCP
            try
            {
                _mcpEventHandler = new McpExternalEventHandler();
                _mcpExternalEvent = ExternalEvent.Create(_mcpEventHandler);
                var dispatcher = new McpCommandDispatcher(_mcpEventHandler, _mcpExternalEvent);
                _mcpHttpServer = new McpHttpServer(dispatcher);
                _mcpHttpServer.Start();
            }
            catch (System.Exception ex)
            {
                // MCP to opcjonalna funkcja — nie blokujemy startu wtyczki w razie błędu
                System.Diagnostics.Trace.WriteLine($"[HCB MCP] Nie udało się uruchomić serwera MCP: {ex.Message}");
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            _mcpHttpServer?.Stop();
            _mcpHttpServer?.Dispose();
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
                    return;
                }

                RibbonBuilder.CreatePanel(application, _pendingPanels.Dequeue());
            }
            catch
            {
                _pendingPanels = null;
                throw;
            }
        }

        private static int ParseRevitVersion(UIControlledApplication application)
        {
            return int.TryParse(application.ControlledApplication.VersionNumber, out int version)
                ? version
                : int.MaxValue;
        }
    }
}

