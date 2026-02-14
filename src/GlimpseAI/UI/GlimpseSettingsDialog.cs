using System;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using GlimpseAI.Models;
using GlimpseAI.Services;

namespace GlimpseAI.UI;

/// <summary>
/// Modal settings dialog for Glimpse AI configuration.
/// Allows users to configure ComfyUI URL, presets, auto-generate behavior, and more.
/// </summary>
public class GlimpseSettingsDialog : Dialog
{
    // --- Controls ---
    private TextBox _comfyUrlTextBox;
    private DropDown _presetDropDown;
    private CheckBox _autoGenerateCheckBox;
    private NumericStepper _debounceStepper;
    private TextArea _defaultPromptTextArea;
    private NumericStepper _denoiseStepper;
    private NumericStepper _captureWidthStepper;
    private NumericStepper _captureHeightStepper;
    private Button _testConnectionButton;
    private Label _connectionStatusLabel;

    // --- ControlNet Settings ---
    private CheckBox _useControlNetCheckBox;
    private NumericStepper _controlNetStrengthStepper;
    private TextBox _controlNetModelTextBox;
    private CheckBox _useDepthPreprocessorCheckBox;

    // --- Auto-Prompt Settings ---
    private DropDown _promptModeDropDown;
    private DropDown _stylePresetDropDown;
    private TextArea _customStyleSuffixTextArea;
    private Label _stylePresetLabel;
    private Label _customStyleLabel;

    // Dark theme colors
    private static readonly Color DarkBg = Color.FromArgb(45, 45, 48);
    private static readonly Color DarkInputBg = Color.FromArgb(62, 62, 66);
    private static readonly Color DarkText = Color.FromArgb(204, 204, 204);
    private static readonly Color DarkInputText = Color.FromArgb(255, 255, 255);
    private static readonly Color DarkHeaderText = Color.FromArgb(255, 255, 255);

    public GlimpseSettingsDialog()
    {
        Title = "Glimpse AI Settings";
        MinimumSize = new Size(450, 520);
        Resizable = true;
        Padding = new Padding(0);
        BackgroundColor = DarkBg;

        Content = BuildUI();
        LoadCurrentSettings();
    }

    /// <summary>Helper to create a dark-themed label.</summary>
    private Label DarkLabel(string text) => new Label { Text = text, TextColor = DarkText };

    /// <summary>Helper to create a dark-themed section header.</summary>
    private Label DarkHeader(string text) => new Label { Text = text, TextColor = DarkHeaderText, Font = SystemFonts.Bold() };

    /// <summary>Applies dark theme to a TextBox.</summary>
    private void StyleTextBox(TextBox tb) { tb.BackgroundColor = DarkInputBg; tb.TextColor = DarkInputText; }

    /// <summary>Applies dark theme to a TextArea.</summary>
    private void StyleTextArea(TextArea ta) { ta.BackgroundColor = DarkInputBg; ta.TextColor = DarkInputText; }

    /// <summary>Applies dark theme to a DropDown.</summary>
    private void StyleDropDown(DropDown dd) { dd.BackgroundColor = DarkInputBg; dd.TextColor = DarkInputText; }

    /// <summary>Applies dark theme to a NumericStepper.</summary>
    private void StyleStepper(NumericStepper ns) { ns.BackgroundColor = DarkInputBg; ns.TextColor = DarkInputText; }

    /// <summary>Applies dark theme to a CheckBox.</summary>
    private void StyleCheckBox(CheckBox cb) { cb.TextColor = DarkText; }

    /// <summary>Applies dark theme to a Button.</summary>
    private void StyleButton(Button btn) { btn.BackgroundColor = DarkInputBg; btn.TextColor = DarkText; }

    private Control BuildUI()
    {
        // --- ComfyUI Connection ---
        _comfyUrlTextBox = new TextBox();
        StyleTextBox(_comfyUrlTextBox);

        _testConnectionButton = new Button { Text = "Test Connection", Width = 120 };
        StyleButton(_testConnectionButton);
        _testConnectionButton.Click += OnTestConnectionClicked;

        _connectionStatusLabel = new Label
        {
            Text = "",
            TextColor = Colors.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };

        var urlRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                new StackLayoutItem(_comfyUrlTextBox, expand: true),
                _testConnectionButton,
                _connectionStatusLabel
            }
        };

        // --- Default Preset ---
        _presetDropDown = new DropDown();
        foreach (var preset in Enum.GetValues(typeof(PresetType)))
            _presetDropDown.Items.Add(preset.ToString());
        StyleDropDown(_presetDropDown);

        // --- Auto Generate ---
        _autoGenerateCheckBox = new CheckBox { Text = "Auto-generate on viewport change" };
        StyleCheckBox(_autoGenerateCheckBox);

        // --- Debounce ---
        _debounceStepper = new NumericStepper
        {
            MinValue = 100,
            MaxValue = 2000,
            Increment = 50,
            DecimalPlaces = 0
        };
        StyleStepper(_debounceStepper);

        // --- Default Prompt ---
        _defaultPromptTextArea = new TextArea
        {
            Height = 60,
            Wrap = true,
            SpellCheck = false
        };
        StyleTextArea(_defaultPromptTextArea);

        // --- Denoise Strength ---
        _denoiseStepper = new NumericStepper
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Increment = 0.05,
            DecimalPlaces = 2
        };
        StyleStepper(_denoiseStepper);

        // --- Capture Resolution ---
        _captureWidthStepper = new NumericStepper
        {
            MinValue = 128,
            MaxValue = 2048,
            Increment = 64,
            DecimalPlaces = 0
        };
        StyleStepper(_captureWidthStepper);
        _captureHeightStepper = new NumericStepper
        {
            MinValue = 128,
            MaxValue = 2048,
            Increment = 64,
            DecimalPlaces = 0
        };
        StyleStepper(_captureHeightStepper);

        var resolutionRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _captureWidthStepper,
                new Label { Text = "×", TextColor = DarkText, VerticalAlignment = VerticalAlignment.Center },
                _captureHeightStepper,
                new Label { Text = "px", TextColor = DarkText, VerticalAlignment = VerticalAlignment.Center }
            }
        };

        // --- ControlNet Settings ---
        _useControlNetCheckBox = new CheckBox { Text = "Use ControlNet for depth-guided generation (Balanced/HQ/4K only)" };
        StyleCheckBox(_useControlNetCheckBox);
        
        _controlNetStrengthStepper = new NumericStepper
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Increment = 0.05,
            DecimalPlaces = 2,
            Value = 0.7
        };
        StyleStepper(_controlNetStrengthStepper);

        _controlNetModelTextBox = new TextBox 
        { 
            PlaceholderText = "Auto-detect (leave empty for auto-detection)"
        };
        StyleTextBox(_controlNetModelTextBox);

        _useDepthPreprocessorCheckBox = new CheckBox 
        { 
            Text = "Use depth preprocessor (DepthAnything_V2)"
        };
        StyleCheckBox(_useDepthPreprocessorCheckBox);

        // --- Auto-Prompt Settings ---
        _promptModeDropDown = new DropDown();
        _promptModeDropDown.Items.Add("Manual");
        _promptModeDropDown.Items.Add("Auto Basic");
        _promptModeDropDown.Items.Add("Auto Vision");
        _promptModeDropDown.SelectedIndexChanged += OnPromptModeChanged;
        StyleDropDown(_promptModeDropDown);

        _stylePresetLabel = new Label { Text = "Style Preset:", TextColor = DarkText };
        
        _stylePresetDropDown = new DropDown();
        _stylePresetDropDown.Items.Add("Architecture");
        _stylePresetDropDown.Items.Add("Artistic");
        _stylePresetDropDown.Items.Add("Textured");
        _stylePresetDropDown.Items.Add("Dramatic");
        _stylePresetDropDown.Items.Add("Minimal");
        _stylePresetDropDown.Items.Add("Nature");
        _stylePresetDropDown.Items.Add("Interior");
        _stylePresetDropDown.Items.Add("Custom");
        _stylePresetDropDown.SelectedIndexChanged += OnStylePresetChanged;
        StyleDropDown(_stylePresetDropDown);

        _customStyleLabel = new Label { Text = "Custom Style Suffix:", TextColor = DarkText };
        
        _customStyleSuffixTextArea = new TextArea
        {
            Height = 40,
            Wrap = true,
            SpellCheck = false
        };
        StyleTextArea(_customStyleSuffixTextArea);

        // --- Buttons ---
        var okButton = new Button { Text = "OK" };
        StyleButton(okButton);
        okButton.Click += OnOkClicked;

        var cancelButton = new Button { Text = "Cancel" };
        StyleButton(cancelButton);
        cancelButton.Click += (s, e) => Close();

        DefaultButton = okButton;
        AbortButton = cancelButton;

        // --- Layout ---
        var layout = new DynamicLayout
        {
            DefaultSpacing = new Size(8, 6),
            Padding = new Padding(16),
            BackgroundColor = DarkBg
        };

        layout.BeginVertical();

        layout.AddRow(DarkHeader("ComfyUI Server"));
        layout.AddRow(DarkLabel("URL:"));
        layout.AddRow(urlRow);

        layout.AddSpace();

        layout.AddRow(DarkHeader("Generation Defaults"));
        layout.AddRow(DarkLabel("Default Preset:"));
        layout.AddRow(_presetDropDown);
        layout.AddRow(DarkLabel("Default Prompt:"));
        layout.AddRow(_defaultPromptTextArea);
        layout.AddRow(DarkLabel("Denoise Strength:"));
        layout.AddRow(_denoiseStepper);

        layout.AddSpace();

        layout.AddRow(DarkHeader("Auto-Prompt"));
        layout.AddRow(DarkLabel("Prompt Mode:"));
        layout.AddRow(_promptModeDropDown);
        layout.AddRow(_stylePresetLabel);
        layout.AddRow(_stylePresetDropDown);
        layout.AddRow(_customStyleLabel);
        layout.AddRow(_customStyleSuffixTextArea);

        layout.AddSpace();

        layout.AddRow(DarkHeader("Viewport Watcher"));
        layout.AddRow(_autoGenerateCheckBox);
        layout.AddRow(DarkLabel("Debounce (ms):"));
        layout.AddRow(_debounceStepper);

        layout.AddSpace();

        layout.AddRow(DarkHeader("ControlNet (Advanced)"));
        layout.AddRow(_useControlNetCheckBox);
        layout.AddRow(DarkLabel("ControlNet Strength:"));
        layout.AddRow(_controlNetStrengthStepper);
        layout.AddRow(DarkLabel("ControlNet Model (optional):"));
        layout.AddRow(_controlNetModelTextBox);
        layout.AddRow(_useDepthPreprocessorCheckBox);

        layout.AddSpace();

        layout.AddRow(DarkHeader("Capture Resolution"));
        layout.AddRow(resolutionRow);

        layout.EndVertical();

        layout.Add(null); // spacer

        layout.BeginVertical();
        layout.BeginHorizontal();
        layout.Add(null, xscale: true);
        layout.Add(cancelButton);
        layout.Add(okButton);
        layout.EndHorizontal();
        layout.EndVertical();

        return layout;
    }

    /// <summary>
    /// Loads the current plugin settings into the dialog controls.
    /// </summary>
    private void LoadCurrentSettings()
    {
        var settings = GlimpseAIPlugin.Instance?.GlimpseSettings ?? new GlimpseSettings();

        _comfyUrlTextBox.Text = settings.ComfyUIUrl;
        _presetDropDown.SelectedIndex = (int)settings.ActivePreset;
        _autoGenerateCheckBox.Checked = settings.AutoGenerate;
        _debounceStepper.Value = settings.DebounceMs;
        _defaultPromptTextArea.Text = settings.DefaultPrompt;
        _denoiseStepper.Value = settings.DenoiseStrength;
        _captureWidthStepper.Value = settings.CaptureWidth;
        _captureHeightStepper.Value = settings.CaptureHeight;
        
        // ControlNet settings
        _useControlNetCheckBox.Checked = settings.UseControlNet;
        _controlNetStrengthStepper.Value = settings.ControlNetStrength;
        _controlNetModelTextBox.Text = settings.ControlNetModel ?? "";
        _useDepthPreprocessorCheckBox.Checked = settings.UseDepthPreprocessor;

        // Auto-Prompt settings
        _promptModeDropDown.SelectedIndex = (int)settings.PromptMode;
        _stylePresetDropDown.SelectedIndex = (int)settings.StylePreset;
        _customStyleSuffixTextArea.Text = settings.CustomStyleSuffix ?? "";
        
        UpdateAutoPromptVisibility();
    }

    /// <summary>
    /// Tests the ComfyUI connection and updates the status label.
    /// </summary>
    private void OnTestConnectionClicked(object sender, EventArgs e)
    {
        _testConnectionButton.Enabled = false;
        _connectionStatusLabel.Text = "Testing...";
        _connectionStatusLabel.TextColor = Colors.Gray;

        var url = _comfyUrlTextBox.Text;

        System.Threading.Tasks.Task.Run(async () =>
        {
            bool ok;
            try
            {
                using var client = new ComfyUIClient(url);
                ok = await client.IsAvailableAsync();
            }
            catch
            {
                ok = false;
            }

            Application.Instance.Invoke(() =>
            {
                if (ok)
                {
                    _connectionStatusLabel.Text = "🟢 Connected";
                    _connectionStatusLabel.TextColor = Color.FromArgb(80, 180, 80);
                }
                else
                {
                    _connectionStatusLabel.Text = "🔴 Unreachable";
                    _connectionStatusLabel.TextColor = Color.FromArgb(220, 60, 60);
                }
                _testConnectionButton.Enabled = true;
            });
        });
    }

    /// <summary>
    /// Updates visibility of auto-prompt controls based on the selected mode.
    /// </summary>
    private void UpdateAutoPromptVisibility()
    {
        var isAuto = _promptModeDropDown.SelectedIndex != 0; // Not Manual
        _stylePresetLabel.Visible = isAuto;
        _stylePresetDropDown.Visible = isAuto;
        
        var isCustomStyle = isAuto && _stylePresetDropDown.SelectedIndex == 7; // Custom
        _customStyleLabel.Visible = isCustomStyle;
        _customStyleSuffixTextArea.Visible = isCustomStyle;
    }

    /// <summary>
    /// Handles prompt mode dropdown changes.
    /// </summary>
    private void OnPromptModeChanged(object sender, EventArgs e)
    {
        UpdateAutoPromptVisibility();
    }

    /// <summary>
    /// Handles style preset dropdown changes.
    /// </summary>
    private void OnStylePresetChanged(object sender, EventArgs e)
    {
        UpdateAutoPromptVisibility();
    }

    /// <summary>
    /// Saves settings and closes the dialog.
    /// </summary>
    private void OnOkClicked(object sender, EventArgs e)
    {
        var settings = new GlimpseSettings
        {
            ComfyUIUrl = _comfyUrlTextBox.Text,
            ActivePreset = (PresetType)_presetDropDown.SelectedIndex,
            AutoGenerate = _autoGenerateCheckBox.Checked ?? false,
            DebounceMs = (int)_debounceStepper.Value,
            DefaultPrompt = _defaultPromptTextArea.Text,
            DenoiseStrength = _denoiseStepper.Value,
            CaptureWidth = (int)_captureWidthStepper.Value,
            CaptureHeight = (int)_captureHeightStepper.Value,
            
            // ControlNet settings
            UseControlNet = _useControlNetCheckBox.Checked ?? false,
            ControlNetStrength = _controlNetStrengthStepper.Value,
            ControlNetModel = string.IsNullOrWhiteSpace(_controlNetModelTextBox.Text) ? "" : _controlNetModelTextBox.Text,
            UseDepthPreprocessor = _useDepthPreprocessorCheckBox.Checked ?? false,

            // Auto-Prompt settings
            PromptMode = (PromptMode)_promptModeDropDown.SelectedIndex,
            StylePreset = (StylePreset)_stylePresetDropDown.SelectedIndex,
            CustomStyleSuffix = _customStyleSuffixTextArea.Text ?? ""
        };

        GlimpseAIPlugin.Instance?.UpdateGlimpseSettings(settings);
        RhinoApp.WriteLine("Glimpse AI: Settings saved.");
        Close();
    }
}
