using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Autodesk.Internal.Windows;
using HCB.RevitAddin.Features.RibbonManager.Models;
using HCB.RevitAddin.Features.RibbonManager.UI;
using HCB.RevitAddin.Features.RibbonShortcuts;
using HCB.RevitAddin.Features.RibbonShortcuts.UI;
using AW = Autodesk.Windows;

namespace HCB.RevitAddin.Features.RibbonManager;

/// <summary>
/// Adds HCB entries to Revit's own ribbon right-click menu (tab header, panel, button):
/// move / rename / hide panels, rename / merge / hide tabs, toggle short names, open Ribbon Manager.
/// Actions run after the menu closes (Dispatcher) and are saved immediately.
/// </summary>
internal static class RibbonContextMenuService
{
    private const string MenuTag = "HCB.RibbonManager";
    private const string DefaultCustomTabTitle = "Moja karta";

    private static bool attached;

    public static void Attach()
    {
        AW.RibbonControl? ribbon = AW.ComponentManager.Ribbon;
        if (attached || ribbon == null)
        {
            return;
        }

        ribbon.ContextMenuOpened += OnContextMenuOpened;
        attached = true;
    }

    private static void OnContextMenuOpened(object? sender, RibbonContextMenuEventArgs e)
    {
        try
        {
            ContextMenu? menu = e.ContextMenu;
            if (menu == null)
            {
                return;
            }

            // Revit reuses the menu instance; drop entries added on a previous opening.
            foreach (object item in menu.Items.OfType<FrameworkElement>().Where(item => Equals(item.Tag, MenuTag)).ToList())
            {
                menu.Items.Remove(item);
            }

            List<FrameworkElement> items = BuildItems(e, menu.Dispatcher);
            if (items.Count == 0)
            {
                return;
            }

            menu.Items.Add(new Separator { Tag = MenuTag });
            foreach (FrameworkElement item in items)
            {
                item.Tag = MenuTag;
                menu.Items.Add(item);
            }
        }
        catch
        {
            // Never break Revit's native ribbon menu.
        }
    }

    private static List<FrameworkElement> BuildItems(RibbonContextMenuEventArgs e, Dispatcher dispatcher)
    {
        List<FrameworkElement> items = new();

        if (e.Target == RibbonContextMenuTarget.Item)
        {
            items.AddRange(BuildShortcutItems(e.Item, dispatcher));
        }

        bool panelTarget = e.Target is RibbonContextMenuTarget.Panel or RibbonContextMenuTarget.PanelFrame or RibbonContextMenuTarget.Item;
        if (panelTarget && RibbonLayoutService.DescribePanel(e.Panel) is { } panel)
        {
            items.AddRange(BuildPanelItems(panel, dispatcher));
        }

        AW.RibbonTab? tabSource = e.Target == RibbonContextMenuTarget.Tab
            ? e.Tab
            : null;
        if (RibbonLayoutService.DescribeTab(tabSource) is { } tab)
        {
            items.AddRange(BuildTabItems(tab, dispatcher));
        }

        RibbonLayoutConfig config = RibbonLayoutService.Current;
        MenuItem shortNames = new()
        {
            Header = "HCB: Skrócone nazwy kart i paneli",
            IsCheckable = true,
            IsChecked = config.ShortTabNames || config.ShortPanelNames
        };
        shortNames.Click += (_, _) => Run(dispatcher, () =>
        {
            bool shorten = !(RibbonLayoutService.Current.ShortTabNames || RibbonLayoutService.Current.ShortPanelNames);
            RibbonLayoutService.Update(layout =>
            {
                layout.ShortTabNames = shorten;
                layout.ShortPanelNames = shorten;
            });
        });
        items.Add(shortNames);

        items.Add(CreateItem("HCB: Ribbon Manager…", dispatcher, () =>
        {
            RibbonManagerWindow window = new();
            SetOwner(window);
            window.ShowDialog();
        }));

        return items;
    }

    private static IEnumerable<FrameworkElement> BuildShortcutItems(AW.RibbonItem? item, Dispatcher dispatcher)
    {
        RibbonShortcutService.CommandInfo? command;
        try
        {
            command = RibbonShortcutService.Describe(item);
        }
        catch
        {
            // UIFramework internals changed or are unavailable: just skip the shortcut entries.
            yield break;
        }

        if (command == null)
        {
            yield break;
        }

        string current = command.Shortcuts.Count == 0 ? "brak" : string.Join(", ", command.Shortcuts);
        string name = Quote(command.Name);

        yield return CreateItem(
            command.Shortcuts.Count == 0 ? $"HCB: Ustaw skrót dla {name}…" : $"HCB: Zmień skrót {name} ({current})…",
            dispatcher,
            () =>
            {
                ShortcutEditWindow window = new(command);
                SetOwner(window);
                window.ShowDialog();
            });

        if (command.Shortcuts.Count > 0)
        {
            yield return CreateItem($"HCB: Usuń skrót {name} ({current})", dispatcher, () =>
                RibbonShortcutService.SetShortcuts(command.Id, Array.Empty<string>()));
        }
    }

    private static IEnumerable<FrameworkElement> BuildPanelItems(RibbonPanelInfo panel, Dispatcher dispatcher)
    {
        string name = Quote(panel.Title);

        if (panel.IsMovable)
        {
            MenuItem move = new() { Header = $"HCB: Przenieś panel {name} na kartę" };
            foreach (RibbonTabInfo target in RibbonLayoutService.ListTargetTabs().Where(tab => tab.Id != panel.CurrentTabId))
            {
                move.Items.Add(CreateItem(target.Title, dispatcher, () =>
                    RibbonLayoutService.Update(layout => RibbonLayoutService.SetPanelMove(layout, panel.Key, target.Id))));
            }

            move.Items.Add(new Separator());
            move.Items.Add(CreateItem("Nowa karta…", dispatcher, () =>
            {
                string? title = Ask("Nowa karta", DefaultCustomTabTitle, $"Panel {name} zostanie przeniesiony na nową kartę.");
                if (string.IsNullOrWhiteSpace(title))
                {
                    return;
                }

                RibbonLayoutService.Update(layout =>
                {
                    string tabId = RibbonLayoutService.CreateCustomTabId();
                    layout.CustomTabs.Add(new RibbonCustomTab { Id = tabId, Title = title });
                    layout.PanelMoves[panel.Key] = tabId;
                });
            }));

            if (panel.CurrentTabId != panel.OriginalTabId)
            {
                move.Items.Add(CreateItem("Przywróć na oryginalną kartę", dispatcher, () =>
                    RibbonLayoutService.Update(layout => layout.PanelMoves.Remove(panel.Key))));
            }

            yield return move;
        }

        yield return CreateItem($"HCB: Zmień nazwę panelu {name}…", dispatcher, () =>
        {
            string? title = Ask("Zmień nazwę panelu", panel.Title, "Pusta nazwa = oryginalna (lub skrócona).");
            if (title == null)
            {
                return;
            }

            RibbonLayoutService.Update(layout => SetOrRemove(layout.PanelTitles, panel.Key, title));
        });

        yield return CreateItem($"HCB: Ukryj panel {name}", dispatcher, () =>
            RibbonLayoutService.Update(layout =>
            {
                if (!layout.HiddenPanels.Contains(panel.Key))
                {
                    layout.HiddenPanels.Add(panel.Key);
                }
            }));
    }

    private static IEnumerable<FrameworkElement> BuildTabItems(RibbonTabInfo tab, Dispatcher dispatcher)
    {
        string name = Quote(tab.Title);

        yield return CreateItem($"HCB: Zmień nazwę karty {name}…", dispatcher, () =>
        {
            string? title = Ask("Zmień nazwę karty", tab.Title, tab.IsCustom ? string.Empty : "Pusta nazwa = oryginalna (lub skrócona).");
            if (title == null)
            {
                return;
            }

            RibbonLayoutService.Update(layout =>
            {
                RibbonCustomTab? custom = layout.CustomTabs.FirstOrDefault(item => item.Id == tab.Id);
                if (custom != null)
                {
                    if (title.Length > 0)
                    {
                        custom.Title = title;
                    }
                }
                else
                {
                    SetOrRemove(layout.TabTitles, tab.Id, title);
                }
            });
        });

        MenuItem merge = new() { Header = $"HCB: Połącz kartę {name} z" };
        foreach (RibbonTabInfo target in RibbonLayoutService.ListTargetTabs().Where(item => item.Id != tab.Id))
        {
            merge.Items.Add(CreateItem(target.Title, dispatcher, () =>
            {
                RibbonTabSnapshot? source = RibbonLayoutService.Snapshot().FirstOrDefault(item => item.Id == tab.Id);
                if (source == null)
                {
                    return;
                }

                RibbonLayoutService.Update(layout =>
                {
                    foreach (RibbonPanelSnapshot panel in source.Panels.Where(panel => panel.IsMovable))
                    {
                        RibbonLayoutService.SetPanelMove(layout, panel.Key, target.Id);
                    }
                });
            }));
        }

        merge.IsEnabled = merge.Items.Count > 0;
        merge.ToolTip = "Przenosi panele wtyczek; panele natywne zostają na miejscu.";
        yield return merge;

        yield return CreateItem($"HCB: Ukryj kartę {name}", dispatcher, () =>
            RibbonLayoutService.Update(layout =>
            {
                if (!layout.HiddenTabs.Contains(tab.Id))
                {
                    layout.HiddenTabs.Add(tab.Id);
                }
            }));

        if (tab.IsCustom)
        {
            yield return CreateItem($"HCB: Usuń kartę {name}", dispatcher, () =>
                RibbonLayoutService.Update(layout =>
                {
                    layout.CustomTabs.RemoveAll(item => item.Id == tab.Id);
                    foreach (string key in layout.PanelMoves.Where(pair => pair.Value == tab.Id).Select(pair => pair.Key).ToList())
                    {
                        layout.PanelMoves.Remove(key);
                    }

                    layout.TabTitles.Remove(tab.Id);
                    layout.HiddenTabs.Remove(tab.Id);
                }));
        }
    }

    private static MenuItem CreateItem(string header, Dispatcher dispatcher, Action action)
    {
        MenuItem item = new() { Header = header };
        item.Click += (_, _) => Run(dispatcher, action);
        return item;
    }

    // Let the native menu close first; dialogs opened from inside a menu click misbehave.
    private static void Run(Dispatcher dispatcher, Action action)
    {
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                MessageBox.Show("Nie udało się zmienić wstążki: " + exception.Message, "HCB Ribbon Manager",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }));
    }

    private static string? Ask(string title, string value, string hint)
    {
        return RibbonTextInputWindow.Ask(title, value, hint, Process.GetCurrentProcess().MainWindowHandle);
    }

    private static void SetOwner(Window window)
    {
        _ = new WindowInteropHelper(window) { Owner = Process.GetCurrentProcess().MainWindowHandle };
    }

    private static void SetOrRemove(Dictionary<string, string> map, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            map.Remove(key);
        }
        else
        {
            map[key] = value;
        }
    }

    private static string Quote(string text)
    {
        return "„" + text + "”";
    }
}
