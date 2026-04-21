using System.Windows;
using System.Text.Json;
using Microsoft.Win32;
using HCB.RevitAddin.Features.AiFamilyBuilder;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.UI;

public partial class AiFamilyBuilderWindow : Window
{
    private readonly AiFamilyBuilderValidationService _validationService = new();

    private const string SampleJson = """
{
  "meta": {
    "schema_version": "1.0",
    "family_name": "Damper_600x300",
    "category_hint": "OST_DuctAccessory"
  },
  "parameters": [
    {
      "name": "Width",
      "kind": "family",
      "spec_type": "length",
      "group": "Geometry",
      "is_instance": false
    },
    {
      "name": "Height",
      "kind": "family",
      "spec_type": "length",
      "group": "Geometry",
      "is_instance": false
    },
    {
      "name": "Depth",
      "kind": "family",
      "spec_type": "length",
      "group": "Geometry",
      "is_instance": false
    }
  ],
  "types": [
    {
      "name": "600x300",
      "values": {
        "Width": 600,
        "Height": 300,
        "Depth": 200
      }
    }
  ],
  "geometry": [
    {
      "op": "extrusion_rectangle",
      "name": "Body",
      "width": "Width",
      "height": "Height",
      "depth": "Depth"
    }
  ]
}
""";

    public AiFamilyBuilderWindow()
    {
        InitializeComponent();
        JsonTextBox.Text = SampleJson;
        FooterBar.StatusText = "Wklej JSON i uruchom generator parametrow, typow oraz geometrii MVP.";
    }

    public AiFamilyBuilderOptions Options => new()
    {
        JsonText = JsonTextBox.Text
    };

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(JsonTextBox.Text))
        {
            FooterBar.StatusText = "Wklej kod JSON.";
            return;
        }

        AiFamilyBuilderValidationResult validation = _validationService.ValidateJson(JsonTextBox.Text);
        if (!validation.IsValid)
        {
            FooterBar.StatusText = $"JSON ma bledy semantyczne: {validation.Errors[0]}";
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void LoadFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "JSON (*.json)|*.json|Text (*.txt)|*.txt|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        JsonTextBox.Text = File.ReadAllText(dialog.FileName);
        FooterBar.StatusText = $"Wczytano plik: {dialog.FileName}";
    }

    private void InsertSampleButton_OnClick(object sender, RoutedEventArgs e)
    {
        JsonTextBox.Text = SampleJson;
        FooterBar.StatusText = "Wstawiono przykladowy JSON.";
    }

    private void ValidateJsonButton_OnClick(object sender, RoutedEventArgs e)
    {
        AiFamilyBuilderValidationResult validation = _validationService.ValidateJson(JsonTextBox.Text);
        if (!validation.IsValid)
        {
            FooterBar.StatusText = $"JSON niepoprawny: {validation.Errors[0]}";
            return;
        }

        FooterBar.StatusText = validation.Warnings.Count == 0
            ? "JSON jest poprawny skladniowo i semantycznie."
            : $"JSON poprawny, ale ma ostrzezenia: {validation.Warnings[0]}";
    }

    private void FormatJsonButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseJson(out JsonDocument? document, out string errorMessage) || document == null)
        {
            FooterBar.StatusText = $"Nie mozna sformatowac JSON: {errorMessage}";
            return;
        }

        JsonTextBox.Text = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        FooterBar.StatusText = "Sformatowano JSON.";
    }

    private bool TryParseJson(out JsonDocument? document, out string errorMessage)
    {
        document = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(JsonTextBox.Text))
        {
            errorMessage = "pole JSON jest puste";
            return false;
        }

        try
        {
            document = JsonDocument.Parse(JsonTextBox.Text);
            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
