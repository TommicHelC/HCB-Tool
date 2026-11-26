namespace HCB.RevitAddin.Features.RibbonManager.Models;

/// <summary>
/// User ribbon layout persisted as JSON. Keys are AdWindows panel/tab ids
/// (see <see cref="RibbonLayoutService"/> for how keys are built).
/// </summary>
public sealed class RibbonLayoutConfig
{
    public int Version { get; set; } = 1;

    /// <summary>Show abbreviated titles for all panels (native ones too) unless a panel has an explicit title.</summary>
    public bool ShortPanelNames { get; set; }

    /// <summary>Show abbreviated titles for all tabs (native ones too) so more tabs fit in the tab strip.</summary>
    public bool ShortTabNames { get; set; }

    /// <summary>Hide tabs that have no visible panels left (e.g. after moving all panels away).</summary>
    public bool HideEmptyTabs { get; set; } = true;

    public List<RibbonCustomTab> CustomTabs { get; set; } = new();

    public List<string> HiddenTabs { get; set; } = new();

    public List<string> HiddenPanels { get; set; } = new();

    /// <summary>Ribbon item (button) ids hidden one by one.</summary>
    public List<string> HiddenItems { get; set; } = new();

    /// <summary>Panel key -> target tab id.</summary>
    public Dictionary<string, string> PanelMoves { get; set; } = new();

    /// <summary>Tab id -> custom title.</summary>
    public Dictionary<string, string> TabTitles { get; set; } = new();

    /// <summary>Panel key -> custom title.</summary>
    public Dictionary<string, string> PanelTitles { get; set; } = new();

    public bool IsEmpty =>
        !ShortPanelNames &&
        !ShortTabNames &&
        CustomTabs.Count == 0 &&
        HiddenTabs.Count == 0 &&
        HiddenPanels.Count == 0 &&
        HiddenItems.Count == 0 &&
        PanelMoves.Count == 0 &&
        TabTitles.Count == 0 &&
        PanelTitles.Count == 0;

    public RibbonLayoutConfig Clone()
    {
        return new RibbonLayoutConfig
        {
            Version = Version,
            ShortPanelNames = ShortPanelNames,
            ShortTabNames = ShortTabNames,
            HideEmptyTabs = HideEmptyTabs,
            CustomTabs = CustomTabs.Select(tab => new RibbonCustomTab { Id = tab.Id, Title = tab.Title }).ToList(),
            HiddenTabs = new List<string>(HiddenTabs),
            HiddenPanels = new List<string>(HiddenPanels),
            HiddenItems = new List<string>(HiddenItems),
            PanelMoves = new Dictionary<string, string>(PanelMoves),
            TabTitles = new Dictionary<string, string>(TabTitles),
            PanelTitles = new Dictionary<string, string>(PanelTitles)
        };
    }
}

public sealed class RibbonCustomTab
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
