using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HCB.RevitAddin.Features.VelocitySizer.Models;

namespace HCB.RevitAddin.Features.VelocitySizer.UI;

public partial class VelocitySizerWindow : Window
{
    private readonly VelocitySizerService _service = new();
    private readonly Action<long>? _showInModelCallback;

    public VelocitySizerWindow(
        IReadOnlyList<VelocitySizerSelectionItem> items,
        IReadOnlyList<string> infoMessages,
        Action<long>? showInModelCallback = null)
    {
        InitializeComponent();

        _showInModelCallback = showInModelCallback;

        SelectionItems = new ObservableCollection<VelocitySizerSelectionItem>(items);
        ElementsDataGrid.ItemsSource = SelectionItems;
        CandidatesDataGrid.ItemsSource = CandidateItems;

        InfoTextBlock.Text = infoMessages.Count == 0
            ? "Narzedzie pracuje wylacznie na aktualnym zaznaczeniu. Dwuklik lub przycisk po prawej przypisuje rozmiar dla zaznaczonego wiersza."
            : string.Join("\n", infoMessages.Take(5));

        FooterBar.StatusText = "Wybierz element, wpisz przeplyw i przypisz standardowy rozmiar.";

        if (SelectionItems.Count > 0)
        {
            ElementsDataGrid.SelectedIndex = 0;
            RefreshCandidatePreview();
        }
    }

    public ObservableCollection<VelocitySizerSelectionItem> SelectionItems { get; }

    public ObservableCollection<VelocitySizerSizeOption> CandidateItems { get; } = [];

    public VelocitySizerCommitMode CommitMode { get; private set; } = VelocitySizerCommitMode.ApplyWithGeometry;

    private void ElementsDataGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshCandidatePreview();
    }

    private void ElementsDataGrid_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        VelocitySizerSelectionItem? edited = e.Row?.Item as VelocitySizerSelectionItem;
        Dispatcher.BeginInvoke(() => PropagateFlowWithinSection(edited));
    }

    private void PropagateFlowWithinSection(VelocitySizerSelectionItem? source)
    {
        if (source is { HasSection: true })
        {
            foreach (VelocitySizerSelectionItem item in SelectionItems)
            {
                if (!ReferenceEquals(item, source) &&
                    item.SectionKey == source.SectionKey &&
                    item.FlowText != source.FlowText)
                {
                    item.FlowText = source.FlowText;
                }
            }

            ElementsDataGrid.Items.Refresh();
        }

        RefreshCandidatePreview();
    }

    private void AssignCandidateButton_OnClick(object sender, RoutedEventArgs e)
    {
        AssignSelectedCandidate();
    }

    private void CandidatesDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        AssignSelectedCandidate();
    }

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        TryCommit(VelocitySizerCommitMode.ApplyWithGeometry);
    }

    private void SaveParametersButton_OnClick(object sender, RoutedEventArgs e)
    {
        TryCommit(VelocitySizerCommitMode.ParametersOnly);
    }

    private void TryCommit(VelocitySizerCommitMode mode)
    {
        if (SelectionItems.Count == 0)
        {
            FooterBar.StatusText = "Brak elementow do zapisania.";
            return;
        }

        // pozwalamy zapisac nawet gdy nie wszystkie pozycje sa wybrane/obliczone -
        // zastosowane zostana tylko te z wpisanym przeplywem i wybranym rozmiarem
        int ready = SelectionItems.Count(IsReadyToCommit);
        if (ready == 0)
        {
            FooterBar.StatusText = "Brak pozycji z wpisanym przeplywem i wybranym rozmiarem.";
            return;
        }

        int skipped = SelectionItems.Count - ready;
        FooterBar.StatusText = skipped > 0
            ? $"Zastosowano {ready} z {SelectionItems.Count} (pominieto {skipped} bez rozmiaru/przeplywu)."
            : $"Zastosowano {ready} pozycji.";

        CommitMode = mode;
        DialogResult = true;
        Close();
    }

    private static bool IsReadyToCommit(VelocitySizerSelectionItem item)
    {
        return item.SelectedSize != null && !string.IsNullOrWhiteSpace(item.FlowText);
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RefreshCandidatePreview()
    {
        CandidateItems.Clear();

        if (ElementsDataGrid.SelectedItem is not VelocitySizerSelectionItem item)
        {
            FooterBar.StatusText = "Wybierz element z lewej tabeli.";
            return;
        }

        IReadOnlyList<VelocitySizerSizeOption> preview = _service.BuildPreview(item, out string? errorMessage);
        foreach (VelocitySizerSizeOption candidate in preview)
        {
            CandidateItems.Add(candidate);
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            FooterBar.StatusText = errorMessage;
            return;
        }

        FooterBar.StatusText = $"Element {item.ElementId}: standardowych rozmiarow {CandidateItems.Count}.";
        SelectCurrentCandidate(item);
    }

    private void AssignSelectedCandidate()
    {
        if (CandidatesDataGrid.SelectedItem is not VelocitySizerSizeOption candidate)
        {
            FooterBar.StatusText = "Wybierz rozmiar z prawej tabeli.";
            return;
        }

        List<VelocitySizerSelectionItem> targets = ElementsDataGrid.SelectedItems
            .OfType<VelocitySizerSelectionItem>()
            .ToList();
        if (targets.Count == 0)
        {
            FooterBar.StatusText = "Wybierz element z lewej tabeli.";
            return;
        }

        foreach (VelocitySizerSelectionItem item in targets)
        {
            ApplyCandidateToItem(item, candidate);
        }

        ElementsDataGrid.Items.Refresh();

        if (targets.Count == 1)
        {
            FooterBar.StatusText = $"Element {targets[0].ElementId}: przypisano {candidate.DisplayName}.";
            MoveToNextElement(targets[0]);
        }
        else
        {
            FooterBar.StatusText = $"Przypisano {candidate.DisplayName} do {targets.Count} elementow.";
        }
    }

    // przypisuje rozmiar i przelicza predkosc wedlug wlasnego przeplywu danego elementu
    private void ApplyCandidateToItem(VelocitySizerSelectionItem item, VelocitySizerSizeOption candidate)
    {
        item.SelectedSize = candidate;
        item.SelectedSizeDisplay = candidate.DisplayName;

        double velocity = _service.ComputeVelocity(item, candidate) ?? candidate.VelocityMetersPerSecond;
        item.SelectedVelocity = velocity;
        item.SelectedVelocityDisplay = velocity.ToString("0.##");
    }

    // po przypisaniu rozmiaru przechodzi do kolejnego wiersza
    private void MoveToNextElement(VelocitySizerSelectionItem current)
    {
        int index = SelectionItems.IndexOf(current);
        if (index < 0 || index + 1 >= SelectionItems.Count)
        {
            return;
        }

        VelocitySizerSelectionItem next = SelectionItems[index + 1];
        ElementsDataGrid.UnselectAll();
        ElementsDataGrid.SelectedItem = next;
        ElementsDataGrid.ScrollIntoView(next);
    }

    private void AutoSizeAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectionItems.Count == 0)
        {
            FooterBar.StatusText = "Brak elementow do doboru.";
            return;
        }

        if (!TryGetMaxVelocity(out double maxVelocity))
        {
            return;
        }

        int sized = 0;
        int exceeded = 0;
        int missingFlow = 0;
        foreach (VelocitySizerSelectionItem item in SelectionItems)
        {
            switch (ApplyAutoSize(item, maxVelocity))
            {
                case AutoSizeOutcome.Sized:
                    sized++;
                    break;
                case AutoSizeOutcome.SizedExceeded:
                    sized++;
                    exceeded++;
                    break;
                case AutoSizeOutcome.MissingFlow:
                    missingFlow++;
                    break;
            }
        }

        ElementsDataGrid.Items.Refresh();
        RefreshCandidatePreview();

        string summary = $"Dobrano automatycznie {sized} z {SelectionItems.Count} (max v = {maxVelocity:0.##} m/s).";
        if (missingFlow > 0)
        {
            summary += $" Bez przeplywu: {missingFlow}.";
        }

        if (exceeded > 0)
        {
            summary += $" Przekroczono limit: {exceeded} (przyjeto najwiekszy rozmiar).";
        }

        FooterBar.StatusText = summary;
    }

    private void AutoSizeSelectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ElementsDataGrid.SelectedItem is not VelocitySizerSelectionItem item)
        {
            FooterBar.StatusText = "Wybierz element z lewej tabeli.";
            return;
        }

        if (!TryGetMaxVelocity(out double maxVelocity))
        {
            return;
        }

        switch (ApplyAutoSize(item, maxVelocity))
        {
            case AutoSizeOutcome.MissingFlow:
                FooterBar.StatusText = $"Element {item.ElementId}: wpisz przeplyw przed doborem.";
                break;
            case AutoSizeOutcome.Sized:
                FooterBar.StatusText = $"Element {item.ElementId}: dobrano {item.SelectedSizeDisplay} (v={item.SelectedVelocityDisplay} m/s).";
                break;
            case AutoSizeOutcome.SizedExceeded:
                FooterBar.StatusText = $"Element {item.ElementId}: limit przekroczony, przyjeto najwiekszy rozmiar {item.SelectedSizeDisplay} (v={item.SelectedVelocityDisplay} m/s).";
                break;
        }

        ElementsDataGrid.Items.Refresh();
        SelectCurrentCandidate(item);
    }

    private void ShowInModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowSelectedInModel();
    }

    private void ElementsDataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ShowSelectedInModel();
    }

    private void ShowSelectedInModel()
    {
        if (ElementsDataGrid.SelectedItem is not VelocitySizerSelectionItem item)
        {
            FooterBar.StatusText = "Wybierz element z lewej tabeli.";
            return;
        }

        if (_showInModelCallback == null)
        {
            FooterBar.StatusText = "Podglad w modelu jest niedostepny.";
            return;
        }

        _showInModelCallback(item.ElementId);
        FooterBar.StatusText = $"Element {item.ElementId}: pokazano w modelu.";
    }

    private AutoSizeOutcome ApplyAutoSize(VelocitySizerSelectionItem item, double maxVelocity)
    {
        VelocitySizerSizeOption? chosen = _service.AutoSelectSize(item, maxVelocity, out bool exceededLimit, out _);
        if (chosen == null)
        {
            return AutoSizeOutcome.MissingFlow;
        }

        item.SelectedSize = chosen;
        item.SelectedSizeDisplay = chosen.DisplayName;
        item.SelectedVelocityDisplay = chosen.VelocityMetersPerSecond.ToString("0.##");
        item.SelectedVelocity = chosen.VelocityMetersPerSecond;
        return exceededLimit ? AutoSizeOutcome.SizedExceeded : AutoSizeOutcome.Sized;
    }

    private bool TryGetMaxVelocity(out double maxVelocity)
    {
        string text = (MaxVelocityTextBox.Text ?? string.Empty).Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out maxVelocity) || maxVelocity <= 0d)
        {
            maxVelocity = 0d;
            FooterBar.StatusText = "Wpisz dodatnia maksymalna predkosc w m/s.";
            return false;
        }

        return true;
    }

    private enum AutoSizeOutcome
    {
        MissingFlow,
        Sized,
        SizedExceeded
    }

    private void SelectCurrentCandidate(VelocitySizerSelectionItem item)
    {
        if (item.SelectedSize == null)
        {
            CandidatesDataGrid.SelectedIndex = CandidateItems.Count > 0 ? 0 : -1;
            return;
        }

        VelocitySizerSizeOption? current = CandidateItems.FirstOrDefault(candidate =>
            candidate.DisplayName == item.SelectedSize.DisplayName);
        CandidatesDataGrid.SelectedItem = current;
    }
}
