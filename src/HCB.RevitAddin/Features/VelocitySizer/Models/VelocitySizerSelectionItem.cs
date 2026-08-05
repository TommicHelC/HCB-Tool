using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HCB.RevitAddin.Features.VelocitySizer.Models;

public sealed class VelocitySizerSelectionItem : INotifyPropertyChanged
{
    private string _flowText = string.Empty;
    private string _selectedSizeDisplay = string.Empty;
    private string _selectedVelocityDisplay = string.Empty;
    private double? _selectedVelocity;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required long ElementId { get; init; }

    public required string ElementTypeLabel { get; init; }

    public required string CurrentSizeDisplay { get; init; }

    public required string SupportedShapeLabel { get; init; }

    public required IReadOnlyList<VelocitySizerSizeOption> AvailableSizes { get; init; }

    // klucz grupowania sekcji = SystemId + numer Section; dla elementow bez Section unikalny (brak grupy)
    public string SectionKey { get; init; } = string.Empty;

    public string SectionLabel { get; init; } = string.Empty;

    public bool HasSection => !string.IsNullOrEmpty(SectionKey) && !SectionKey.StartsWith("__solo_", System.StringComparison.Ordinal);

    public string FlowText
    {
        get => _flowText;
        set
        {
            if (value == _flowText)
            {
                return;
            }

            _flowText = value;
            OnPropertyChanged();
        }
    }

    public string SelectedSizeDisplay
    {
        get => _selectedSizeDisplay;
        set
        {
            if (value == _selectedSizeDisplay)
            {
                return;
            }

            _selectedSizeDisplay = value;
            OnPropertyChanged();
        }
    }

    public string SelectedVelocityDisplay
    {
        get => _selectedVelocityDisplay;
        set
        {
            if (value == _selectedVelocityDisplay)
            {
                return;
            }

            _selectedVelocityDisplay = value;
            OnPropertyChanged();
        }
    }

    public double? SelectedVelocity
    {
        get => _selectedVelocity;
        set
        {
            if (value == _selectedVelocity)
            {
                return;
            }

            _selectedVelocity = value;
            OnPropertyChanged();
        }
    }

    public VelocitySizerSizeOption? SelectedSize { get; set; }

    public bool HasWritableTargets { get; init; }

    public bool HasExistingVelocityValue { get; init; }

    public bool HasExistingFlowValue { get; init; }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
