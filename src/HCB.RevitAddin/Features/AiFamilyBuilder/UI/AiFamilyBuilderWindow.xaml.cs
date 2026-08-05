using System;
using System.Windows;
using System.Text.Json;
using Microsoft.Win32;
using HCB.RevitAddin.Features.AiFamilyBuilder;
using HCB.RevitAddin.Features.AiFamilyBuilder.Models;
using HCB.RevitAddin.Features.AiFamilyBuilder.V3;

namespace HCB.RevitAddin.Features.AiFamilyBuilder.UI;

public partial class AiFamilyBuilderWindow : Window
{
    private readonly AiFamilyBuilderValidationService _validationService = new();

    private const string SampleJson = """
{
  "schema": "hcb.ai_family_builder.v2",
  "meta": {
    "schema_version": "2.0",
    "family_name": "Damper_600x300",
    "category_hint": "OST_DuctAccessory",
    "template_hint": "Metric Generic Model"
  },
  "parameters": [
    {
      "name": "Width",
      "source": "family",
      "data_type": "length",
      "group": "Geometry",
      "scope": "type"
    },
    {
      "name": "Height",
      "source": "family",
      "data_type": "length",
      "group": "Geometry",
      "scope": "type"
    },
    {
      "name": "Depth",
      "source": "family",
      "data_type": "length",
      "group": "Geometry",
      "scope": "type"
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
      "kind": "rectangular_prism",
      "name": "Body",
      "solid": true,
      "width": { "ref": "Width" },
      "height": { "ref": "Height" },
      "depth": { "ref": "Depth" }
    }
  ]
}
""";

    public AiFamilyBuilderWindow()
    {
        InitializeComponent();
        JsonTextBox.Text = SampleJson;
        SetStatus("Wklej JSON v2 i uruchom generator parametrow, typow oraz geometrii parametrycznej.");
    }

    public AiFamilyBuilderOptions Options => new()
    {
        JsonText = JsonTextBox.Text
    };

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(JsonTextBox.Text))
        {
            SetStatus("Wklej kod JSON.");
            return;
        }

        AiFamilyBuilderValidationResult validation = ValidateInput(JsonTextBox.Text);
        if (!validation.IsValid)
        {
            SetValidationMessages(validation, "JSON ma bledy semantyczne.");
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
        SetStatus($"Wczytano plik: {dialog.FileName}");
    }

    private void InsertSampleButton_OnClick(object sender, RoutedEventArgs e)
    {
        JsonTextBox.Text = SampleJson;
        SetStatus("Wstawiono przykladowy JSON.");
    }

    private void ValidateJsonButton_OnClick(object sender, RoutedEventArgs e)
    {
        AiFamilyBuilderValidationResult validation = ValidateInput(JsonTextBox.Text);
        if (!validation.IsValid)
        {
            SetValidationMessages(validation, "JSON niepoprawny.");
            return;
        }

        SetValidationMessages(
            validation,
            validation.Warnings.Count == 0
                ? "JSON jest poprawny skladniowo i semantycznie."
                : "JSON poprawny, ale ma ostrzezenia.");
    }

    private AiFamilyBuilderValidationResult ValidateInput(string jsonText)
    {
        return AiFamilyBuilderSchemaDetector.IsV3(jsonText)
            ? new AiFamilyBuilderV3Service().Validate(jsonText)
            : _validationService.ValidateJson(jsonText);
    }

    private void FormatJsonButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseJson(out JsonDocument? document, out string errorMessage) || document == null)
        {
            SetStatus($"Nie mozna sformatowac JSON: {errorMessage}");
            return;
        }

        JsonTextBox.Text = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        SetStatus("Sformatowano JSON.");
    }

    private void CopyMessagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MessagesTextBox.Text))
        {
            SetStatus("Brak komunikatow do skopiowania.");
            return;
        }

        Clipboard.SetText(MessagesTextBox.Text);
        SetStatus("Skopiowano komunikaty do schowka.");
    }

    private void ClearMessagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        MessagesTextBox.Clear();
        SetStatus("Wyczyszczono komunikaty.");
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

    private void SetStatus(string statusText)
    {
        FooterBar.StatusText = statusText;
        MessagesTextBox.Text = statusText;
    }

    private void SetValidationMessages(AiFamilyBuilderValidationResult validation, string header)
    {
        List<string> lines = [header];
        if (validation.Errors.Count > 0)
        {
            lines.Add("Errors:");
            lines.AddRange(validation.Errors);
        }

        if (validation.Warnings.Count > 0)
        {
            lines.Add("Warnings:");
            lines.AddRange(validation.Warnings);
        }

        string text = string.Join(Environment.NewLine, lines);
        FooterBar.StatusText = header;
        MessagesTextBox.Text = text;
    }
}
