using System.Collections.Generic;
using Microsoft.Win32;

namespace HCB.RevitAddin.Infrastructure.WithoutOpen;

public sealed class WithoutOpenDialogService
{
    public IReadOnlyList<string> PickRevitFiles()
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Wybierz pliki Revit",
            Filter = "Pliki Revit (*.rvt;*.rfa)|*.rvt;*.rfa|Projekty (*.rvt)|*.rvt|Rodziny (*.rfa)|*.rfa|Wszystkie pliki (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : [];
    }

    public string? PickSharedParameterFile()
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Wybierz plik Shared Parameters",
            Filter = "Shared Parameters (*.txt)|*.txt|Wszystkie pliki (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public string? PickFolderPath(string description)
    {
        OpenFolderDialog dialog = new()
        {
            Title = description,
            Multiselect = false
        };

        return dialog.ShowDialog() == true
            ? dialog.FolderName
            : null;
    }

    public string? PickCsvOutputPath(string suggestedFileName)
    {
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "Zapisz raport CSV",
            Filter = "CSV (*.csv)|*.csv",
            FileName = suggestedFileName,
            AddExtension = true,
            DefaultExt = ".csv",
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
}

