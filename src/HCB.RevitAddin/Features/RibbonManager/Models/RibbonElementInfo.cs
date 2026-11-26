namespace HCB.RevitAddin.Features.RibbonManager.Models;

public sealed record RibbonPanelInfo(
    string Key,
    string Title,
    bool IsMovable,
    string OriginalTabId,
    string? CurrentTabId,
    IReadOnlyList<RibbonItemSnapshot> HiddenItems);

public sealed record RibbonTabInfo(
    string Id,
    string Title,
    bool IsCustom);
