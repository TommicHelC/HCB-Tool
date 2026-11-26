using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using HCB.RevitAddin.Features.RibbonManager.Models;
using AW = Autodesk.Windows;

namespace HCB.RevitAddin.Features.RibbonManager;

/// <summary>
/// Applies a <see cref="RibbonLayoutConfig"/> to the live Revit ribbon through AdWindows.
/// Every panel and tab is tracked with its original parent/title the first time it is seen,
/// so any layout (including an empty one = reset) can be applied idempotently at any time
/// without restarting Revit. All calls must run on the Revit UI thread.
/// </summary>
internal static class RibbonLayoutService
{
    private const string ApiPanelPrefix = "CustomCtrl_";
    private const string CustomTabPrefix = "HCB_CUSTOM_";

    private static readonly Dictionary<AW.RibbonPanel, PanelOrigin> TrackedPanels = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<AW.RibbonTab, TabOrigin> TrackedTabs = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<object> HiddenByLayout = new(ReferenceEqualityComparer.Instance);

    private static bool initialized;

    public static RibbonLayoutConfig Current { get; private set; } = new();

    /// <summary>
    /// Loads the saved layout and applies it. Called once the HCB ribbon is built (first idle cycles),
    /// i.e. after every add-in finished OnStartup. Re-applied on view activation so panels created later
    /// by other add-ins and Revit's own context switches (project / family editor) are covered too.
    /// </summary>
    public static void Initialize(UIControlledApplication application)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        Current = RibbonLayoutStore.Load();
        TryApply(Current);
        application.ViewActivated += OnViewActivated;
        RibbonContextMenuService.Attach();
    }

    public static string CreateCustomTabId()
    {
        return CustomTabPrefix + Guid.NewGuid().ToString("N");
    }

    /// <summary>Applies a change to the current layout and persists it immediately (used by the ribbon context menu).</summary>
    public static void Update(Action<RibbonLayoutConfig> change)
    {
        RibbonLayoutConfig config = Current.Clone();
        change(config);
        Apply(config);
        RibbonLayoutStore.Save(config);
    }

    /// <summary>Sets a panel move; moving a panel back to its original tab removes the entry.</summary>
    public static void SetPanelMove(RibbonLayoutConfig config, string panelKey, string targetTabId)
    {
        if (string.Equals(GetOriginalTabId(panelKey), targetTabId, StringComparison.Ordinal))
        {
            config.PanelMoves.Remove(panelKey);
        }
        else
        {
            config.PanelMoves[panelKey] = targetTabId;
        }
    }

    public static RibbonPanelInfo? DescribePanel(AW.RibbonPanel? panel)
    {
        AW.RibbonControl? ribbon = AW.ComponentManager.Ribbon;
        if (ribbon == null || panel == null)
        {
            return null;
        }

        Track(ribbon);
        if (!TrackedPanels.TryGetValue(panel, out PanelOrigin? origin))
        {
            return null;
        }

        AW.RibbonTab? currentTab = FindParentTab(ribbon, panel);
        return new RibbonPanelInfo(
            origin.Key,
            panel.Source?.Title ?? origin.OriginalTitle,
            origin.IsMovable,
            GetTabId(origin.OriginalTab),
            currentTab == null ? null : GetTabId(currentTab),
            GetHiddenItems(panel, Current));
    }

    public static RibbonTabInfo? DescribeTab(AW.RibbonTab? tab)
    {
        AW.RibbonControl? ribbon = AW.ComponentManager.Ribbon;
        if (ribbon == null || tab == null || tab.IsContextualTab)
        {
            return null;
        }

        Track(ribbon);
        return TrackedTabs.TryGetValue(tab, out TabOrigin? origin)
            ? new RibbonTabInfo(GetTabId(tab), tab.Title ?? origin.OriginalTitle, origin.IsCustom)
            : null;
    }

    /// <summary>Tabs a panel can be moved to / merged into: visible or own tabs, not hidden by the user.</summary>
    public static IReadOnlyList<RibbonTabInfo> ListTargetTabs()
    {
        return Snapshot()
            .Where(tab => !tab.IsHidden)
            .Select(tab => new RibbonTabInfo(tab.Id, tab.Title, tab.IsCustom))
            .ToList();
    }

    /// <summary>Id usable for hiding a single tool, or null (separators, unnamed controls).</summary>
    public static RibbonItemSnapshot? DescribeItem(AW.RibbonItem? item)
    {
        return item == null || string.IsNullOrEmpty(item.Id) ? null : new RibbonItemSnapshot(item.Id, GetItemName(item));
    }

    private static IReadOnlyList<RibbonItemSnapshot> GetHiddenItems(AW.RibbonPanel panel, RibbonLayoutConfig config)
    {
        if (config.HiddenItems.Count == 0)
        {
            return Array.Empty<RibbonItemSnapshot>();
        }

        return EnumerateItems(panel)
            .Where(item => !string.IsNullOrEmpty(item.Id) && config.HiddenItems.Contains(item.Id))
            .GroupBy(item => item.Id)
            .Select(group => new RibbonItemSnapshot(group.Key, GetItemName(group.First())))
            .ToList();
    }

    // Walks a panel including stacked rows, the slide-out part and pulldown/split contents.
    private static IEnumerable<AW.RibbonItem> EnumerateItems(AW.RibbonPanel panel)
    {
        if (panel.Source?.Items == null)
        {
            yield break;
        }

        Stack<AW.RibbonItem> pending = new(panel.Source.Items.Reverse());
        while (pending.Count > 0)
        {
            AW.RibbonItem item = pending.Pop();
            yield return item;

            IEnumerable<AW.RibbonItem>? children = item switch
            {
                AW.RibbonRowPanel row => row.Items,
                AW.RibbonListButton list => list.Items,
                _ => null
            };

            if (children != null)
            {
                foreach (AW.RibbonItem child in children.Reverse())
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static string GetItemName(AW.RibbonItem item)
    {
        string? text = item.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = item.AutomationName;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            text = item.Name;
        }

        return string.IsNullOrWhiteSpace(text)
            ? item.Id
            : new string(text.Select(c => char.IsControl(c) ? (char)32 : c).ToArray()).Trim();
    }

    public static AW.RibbonTab? FindParentTab(AW.RibbonPanel panel)
    {
        AW.RibbonControl? ribbon = AW.ComponentManager.Ribbon;
        return ribbon == null ? null : FindParentTab(ribbon, panel);
    }

    public static bool TryApply(RibbonLayoutConfig config)
    {
        try
        {
            Apply(config);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(RibbonLayoutConfig config)
    {
        Current = config;

        AW.RibbonControl? ribbon = AW.ComponentManager.Ribbon;
        if (ribbon == null)
        {
            return;
        }

        Track(ribbon);
        EnsureCustomTabs(ribbon, config);
        ApplyPanels(ribbon, config);
        ApplyTabs(ribbon, config);
    }

    public static IReadOnlyList<RibbonTabSnapshot> Snapshot()
    {
        AW.RibbonControl? ribbon = AW.ComponentManager.Ribbon;
        if (ribbon == null)
        {
            return Array.Empty<RibbonTabSnapshot>();
        }

        Track(ribbon);
        RibbonLayoutConfig config = Current;
        List<RibbonTabSnapshot> result = new();

        foreach (AW.RibbonTab tab in ribbon.Tabs.ToList())
        {
            if (!TrackedTabs.TryGetValue(tab, out TabOrigin? tabOrigin))
            {
                continue;
            }

            // Tabs Revit hides by context (e.g. family editor tabs in a project) are not offered.
            bool listed = tab.IsVisible || HiddenByLayout.Contains(tab) || tabOrigin.IsCustom;
            if (!listed || string.IsNullOrWhiteSpace(tabOrigin.OriginalTitle))
            {
                continue;
            }

            string tabId = GetTabId(tab);
            List<RibbonPanelSnapshot> panels = new();
            foreach (AW.RibbonPanel panel in tab.Panels.ToList())
            {
                if (!TrackedPanels.TryGetValue(panel, out PanelOrigin? panelOrigin))
                {
                    continue;
                }

                string originalTabTitle = TrackedTabs.TryGetValue(panelOrigin.OriginalTab, out TabOrigin? sourceTab)
                    ? sourceTab.OriginalTitle
                    : string.Empty;

                panels.Add(new RibbonPanelSnapshot(
                    panelOrigin.Key,
                    panel.Source?.Title ?? panelOrigin.OriginalTitle,
                    panelOrigin.OriginalTitle,
                    originalTabTitle,
                    panelOrigin.IsMovable,
                    config.HiddenPanels.Contains(panelOrigin.Key),
                    GetHiddenItems(panel, config)));
            }

            result.Add(new RibbonTabSnapshot(
                tabId,
                tab.Title ?? tabOrigin.OriginalTitle,
                tabOrigin.OriginalTitle,
                tabOrigin.IsCustom,
                config.HiddenTabs.Contains(tabId),
                panels));
        }

        return result;
    }

    /// <summary>Original tab id of a panel, used to drop a move entry when a panel is moved back home.</summary>
    public static string? GetOriginalTabId(string panelKey)
    {
        foreach (KeyValuePair<AW.RibbonPanel, PanelOrigin> pair in TrackedPanels)
        {
            if (string.Equals(pair.Value.Key, panelKey, StringComparison.Ordinal))
            {
                return GetTabId(pair.Value.OriginalTab);
            }
        }

        return null;
    }

    private static void OnViewActivated(object? sender, ViewActivatedEventArgs e)
    {
        if (!Current.IsEmpty)
        {
            TryApply(Current);
        }
    }

    private static void Track(AW.RibbonControl ribbon)
    {
        foreach (AW.RibbonTab tab in ribbon.Tabs.ToList())
        {
            if (tab.IsContextualTab)
            {
                continue;
            }

            if (!TrackedTabs.ContainsKey(tab))
            {
                TrackedTabs[tab] = new TabOrigin(tab.Title ?? string.Empty, IsCustomTabId(tab.Id), HadPanels: tab.Panels.Count > 0);
            }

            foreach (AW.RibbonPanel panel in tab.Panels.ToList())
            {
                if (TrackedPanels.ContainsKey(panel))
                {
                    continue;
                }

                string key = BuildPanelKey(tab, panel);
                TrackedPanels[panel] = new PanelOrigin(
                    key,
                    tab,
                    panel.Source?.Title ?? string.Empty,
                    key.StartsWith(ApiPanelPrefix, StringComparison.Ordinal));
            }
        }
    }

    private static void EnsureCustomTabs(AW.RibbonControl ribbon, RibbonLayoutConfig config)
    {
        foreach (RibbonCustomTab customTab in config.CustomTabs)
        {
            if (string.IsNullOrWhiteSpace(customTab.Id) || FindTab(ribbon, customTab.Id) != null)
            {
                continue;
            }

            AW.RibbonTab tab = new()
            {
                Id = customTab.Id,
                Name = customTab.Title,
                Title = customTab.Title
            };

            ribbon.Tabs.Add(tab);
            TrackedTabs[tab] = new TabOrigin(customTab.Title, IsCustom: true, HadPanels: false);
        }
    }

    private static void ApplyPanels(AW.RibbonControl ribbon, RibbonLayoutConfig config)
    {
        foreach (KeyValuePair<AW.RibbonPanel, PanelOrigin> pair in TrackedPanels.ToList())
        {
            AW.RibbonPanel panel = pair.Key;
            PanelOrigin origin = pair.Value;

            try
            {
                AW.RibbonTab? currentTab = FindParentTab(ribbon, panel);
                if (currentTab == null)
                {
                    // The owner removed the panel from the ribbon; forget it.
                    TrackedPanels.Remove(panel);
                    HiddenByLayout.Remove(panel);
                    continue;
                }

                AW.RibbonTab targetTab = origin.OriginalTab;
                if (origin.IsMovable &&
                    config.PanelMoves.TryGetValue(origin.Key, out string? targetTabId) &&
                    FindTab(ribbon, targetTabId) is { } movedTab)
                {
                    targetTab = movedTab;
                }

                if (!ReferenceEquals(targetTab, currentTab) && ribbon.Tabs.Contains(targetTab))
                {
                    currentTab.Panels.Remove(panel);
                    targetTab.Panels.Add(panel);
                }

                if (panel.Source != null)
                {
                    string title = config.PanelTitles.TryGetValue(origin.Key, out string? customTitle) && !string.IsNullOrWhiteSpace(customTitle)
                        ? customTitle
                        : config.ShortPanelNames
                            ? RibbonPanelNameShortener.Shorten(origin.OriginalTitle)
                            : origin.OriginalTitle;

                    if (!string.Equals(panel.Source.Title, title, StringComparison.Ordinal))
                    {
                        panel.Source.Title = title;
                    }
                }

                SetHidden(panel, config.HiddenPanels.Contains(origin.Key), () => panel.IsVisible, value => panel.IsVisible = value);

                foreach (AW.RibbonItem item in EnumerateItems(panel))
                {
                    if (!string.IsNullOrEmpty(item.Id))
                    {
                        SetHidden(item, config.HiddenItems.Contains(item.Id), () => item.IsVisible, value => item.IsVisible = value);
                    }
                }
            }
            catch
            {
                // One misbehaving panel (another add-in) must not break the rest of the layout.
            }
        }
    }

    private static void ApplyTabs(AW.RibbonControl ribbon, RibbonLayoutConfig config)
    {
        foreach (KeyValuePair<AW.RibbonTab, TabOrigin> pair in TrackedTabs.ToList())
        {
            AW.RibbonTab tab = pair.Key;
            TabOrigin origin = pair.Value;
            string tabId = GetTabId(tab);

            try
            {
                if (!ribbon.Tabs.Contains(tab))
                {
                    TrackedTabs.Remove(tab);
                    HiddenByLayout.Remove(tab);
                    continue;
                }

                RibbonCustomTab? customConfig = origin.IsCustom
                    ? config.CustomTabs.FirstOrDefault(item => string.Equals(item.Id, tabId, StringComparison.Ordinal))
                    : null;

                if (origin.IsCustom && customConfig == null)
                {
                    if (tab.Panels.Count == 0)
                    {
                        if (ReferenceEquals(ribbon.ActiveTab, tab))
                        {
                            ribbon.ActiveTab = ribbon.Tabs.FirstOrDefault(item => item.IsVisible && !ReferenceEquals(item, tab));
                        }

                        ribbon.Tabs.Remove(tab);
                        TrackedTabs.Remove(tab);
                        HiddenByLayout.Remove(tab);
                    }

                    continue;
                }

                string title = config.TabTitles.TryGetValue(tabId, out string? customTitle) && !string.IsNullOrWhiteSpace(customTitle)
                    ? customTitle
                    : customConfig?.Title ?? (config.ShortTabNames
                        ? RibbonPanelNameShortener.Shorten(origin.OriginalTitle)
                        : origin.OriginalTitle);

                if (!string.Equals(tab.Title, title, StringComparison.Ordinal))
                {
                    tab.Title = title;
                }

                // Tabs that started empty are Revit/add-in placeholders; leave them alone.
                bool isEmpty = (origin.IsCustom || origin.HadPanels) && !tab.Panels.Any(panel => !HiddenByLayout.Contains(panel));
                bool hide = config.HiddenTabs.Contains(tabId) || (config.HideEmptyTabs && isEmpty);
                if (hide && tab.IsVisible && ReferenceEquals(ribbon.ActiveTab, tab))
                {
                    AW.RibbonTab? fallback = ribbon.Tabs.FirstOrDefault(item =>
                        item.IsVisible && !item.IsContextualTab && !ReferenceEquals(item, tab));
                    if (fallback != null)
                    {
                        ribbon.ActiveTab = fallback;
                    }
                }

                SetHidden(tab, hide, () => tab.IsVisible, value => tab.IsVisible = value);
            }
            catch
            {
                // Same as panels: keep going with the remaining tabs.
            }
        }
    }

    // Only restores visibility that this layout removed, so Revit keeps control of
    // tabs/panels it hides by context (family editor, disabled disciplines).
    private static void SetHidden(object item, bool hide, Func<bool> isVisible, Action<bool> setVisible)
    {
        if (hide)
        {
            if (isVisible())
            {
                setVisible(false);
                HiddenByLayout.Add(item);
            }

            return;
        }

        if (HiddenByLayout.Remove(item))
        {
            setVisible(true);
        }
    }

    private static AW.RibbonTab? FindParentTab(AW.RibbonControl ribbon, AW.RibbonPanel panel)
    {
        foreach (AW.RibbonTab tab in ribbon.Tabs)
        {
            if (tab.Panels.Contains(panel))
            {
                return tab;
            }
        }

        return null;
    }

    private static AW.RibbonTab? FindTab(AW.RibbonControl ribbon, string tabId)
    {
        foreach (AW.RibbonTab tab in ribbon.Tabs)
        {
            if (!tab.IsContextualTab && string.Equals(GetTabId(tab), tabId, StringComparison.Ordinal))
            {
                return tab;
            }
        }

        return null;
    }

    private static string GetTabId(AW.RibbonTab tab)
    {
        return string.IsNullOrEmpty(tab.Id) ? tab.Title ?? string.Empty : tab.Id;
    }

    private static string BuildPanelKey(AW.RibbonTab tab, AW.RibbonPanel panel)
    {
        string? id = panel.Source?.Id;
        return string.IsNullOrEmpty(id)
            ? GetTabId(tab) + "/" + (panel.Source?.Title ?? string.Empty)
            : id;
    }

    private static bool IsCustomTabId(string? id)
    {
        return id != null && id.StartsWith(CustomTabPrefix, StringComparison.Ordinal);
    }

    private sealed record PanelOrigin(string Key, AW.RibbonTab OriginalTab, string OriginalTitle, bool IsMovable);

    private sealed record TabOrigin(string OriginalTitle, bool IsCustom, bool HadPanels);
}
