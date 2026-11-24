# HCB UI guidelines (WPF windows)

All tool windows share one look, defined in `src/HCB.RevitAddin/UI/Styles/SharedStyles.xaml`
plus the controls `DialogHeader`, `DialogFooterBar` and `SelectionActionsBar` in `UI/Controls`.
Reference implementations: `Features/FlowChanger/UI/FlowChangerOptionsWindow.xaml` (simple form)
and `Features/AccessoryTerminalNumbering/UI/AccessoryTerminalNumberingWindow.xaml` (form + info + preview).

## Window skeleton

```xml
<Window x:Class="..."
        xmlns:controls="clr-namespace:HCB.RevitAddin.UI.Controls"
        Title="Tool Name"
        Width="560" Height="420" MinWidth="480" MinHeight="360"
        ResizeMode="CanResizeWithGrip"
        WindowStartupLocation="CenterScreen"
        Style="{DynamicResource HcbWindowStyle}">
    <Window.Resources> <!-- merge SharedStyles.xaml, as today --> </Window.Resources>

    <Grid Style="{StaticResource DialogRootStyle}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <controls:DialogHeader Title="Tool Name" Description="One sentence: what the tool does." />

        <!-- Row 1: content, Margin="{StaticResource Space.Section}" -->

        <controls:DialogFooterBar x:Name="FooterBar" Grid.Row="2"
                                  Margin="{StaticResource Space.Section}" ... />
    </Grid>
</Window>
```

Rules:

- `Style="{DynamicResource HcbWindowStyle}"` on every window (an implicit `Window` style does not
  reach derived window classes). Must be `DynamicResource` - the resources are declared after it.
- Window `Title` and `DialogHeader.Title` are the same text: the full tool name (not the ribbon
  abbreviation). `Description` is one short Polish sentence; omit it for trivial prompts.
- `Width >= MinWidth`, `Height >= MinHeight`. Resizable windows use `CanResizeWithGrip`; small
  fixed prompts may use `NoResize` with `SizeToContent="Height"`.
- Root padding comes from `DialogRootStyle` - no `Margin="16"` on the root grid.
- Footer: `DialogFooterBar` always. Secondary button is "Anuluj" (or "Zamknij" for read-only
  windows) and closes on Esc. Use `IsPrimaryDefault="True"` only in windows without text inputs.

## Content

- Group related inputs in `GroupBox` with a short header. Groups are stacked 12 px apart:
  `Margin="{StaticResource Space.Section}"` on every group except the first in a column.
  Side-by-side groups: put a 12 px gap column between them (or `GridSplitter` with `SplitterStyle`).
- Forms: a `Grid` with `<ColumnDefinition Width="{StaticResource FormLabelColumnWidth}" />` and
  `<ColumnDefinition Width="*" />`. Label = `TextBlock Style="{StaticResource FieldLabelStyle}"`.
  Rows 8 px apart (`Margin="{StaticResource Space.Field}"` on the input, or a hint row).
- Helper text under an input: `TextBlock Style="{StaticResource HintTextStyle}"` in column 1,
  `Margin="{StaticResource Space.Hint}"`. Sentence case, Polish diacritics, ends with a full stop.
- "How it works" text: `Border Style="{StaticResource InfoPanelStyle}"` with `HintTextStyle` text.
- Live preview: `Border Style="{StaticResource PreviewPanelStyle}"`, value in `EmphasisTextStyle`.
- Warnings / errors in text: `WarningTextStyle` / `ErrorTextStyle`.
- Lists with select-all: `SelectionActionsBar` docked on top, 8 px above the list.
- A label above a full-width input (list filters, search): `SectionLabelStyle` with `Margin="0,0,0,4"`;
  inside a `DockPanel` every non-fill child needs `DockPanel.Dock="Top"`.

## Do not

- Hard-code colours (`#...`), `FontSize`, `FontWeight` or `Opacity` on text - use the styles/tokens.
- Set `Height`/`Width` on `TextBox`, `ComboBox`, `Button` (min sizes come from the styles), except
  for deliberately narrow numeric inputs.
- Use `StatusTextStyle` for hints (it is for the footer status line).
- Mix PL without diacritics: "Usun" -> "Usuń", "narzedzie" -> "narzędzie".

## Tokens (SharedStyles.xaml)

Brushes: `Brush.TextPrimary`, `Brush.TextSecondary`, `Brush.TextMuted`, `Brush.Accent`,
`Brush.AccentSubtle`, `Brush.Surface`, `Brush.SurfaceAlt`, `Brush.Border`, `Brush.BorderStrong`,
`Brush.Divider`, `Brush.Selection`, `Brush.Success`, `Brush.Warning`, `Brush.Danger`.
Spacing: `Space.DialogPadding`, `Space.Section`, `Space.Field`, `Space.Hint`, `FormLabelColumnWidth`.
Text styles: `SectionTitleStyle`, `WindowDescriptionStyle`, `SectionLabelStyle`, `FieldLabelStyle`,
`HintTextStyle`, `StatusTextStyle`, `EmphasisTextStyle`, `WarningTextStyle`, `ErrorTextStyle`.
Panels: `InfoPanelStyle`, `PreviewPanelStyle`, `DividerStyle`. Buttons: default (primary),
`SecondaryButtonStyle`, `InlineActionButtonStyle`. Other: `SplitterStyle`.
TextBox, ComboBox, CheckBox, RadioButton, ListBox, ListView, DataGrid and GroupBox are styled implicitly.
