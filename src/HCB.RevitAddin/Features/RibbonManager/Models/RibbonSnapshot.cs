namespace HCB.RevitAddin.Features.RibbonManager.Models;

public sealed record RibbonTabSnapshot(
    string Id,
    string Title,
    string OriginalTitle,
    bool IsCustom,
    bool IsHidden,
    IReadOnlyList<RibbonPanelSnapshot> Panels);

public sealed record RibbonPanelSnapshot(
    string Key,
    string Title,
    string OriginalTitle,
    string OriginalTabTitle,
    bool IsMovable,
    bool IsHidden,
    IReadOnlyList<RibbonItemSnapshot> HiddenItems);

/// <summary>A single ribbon tool hidden by the layout.</summary>
public sealed record RibbonItemSnapshot(string Id, string Name);
