using System;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using GlimpseAI.Models;

namespace GlimpseAI.UI;

/// <summary>
/// Modal settings dialog for Glimpse AI configuration.
/// </summary>
public class GlimpseSettingsDialog : Dialog
{
    private TextBox _comfyUrlTextBox;
    private DropDown _presetDropDown;
    private CheckBox _autoGenerateCheckBox;
    private NumericStepper _debounceStepper;
    private TextBox _defaultPromptTextBox;
    private NumericStepper _denoiseStepper;
    private NumericStepper _captureWidthStepper;
    private NumericStepper _captureHeightStepper;

    public GlimpseSettingsDialog()
    {
        Title = "Glimpse AI Settings";
        MinimumSize = new Size(420, 480);
        Resizable = true;

        Content = BuildUI();
        LoadCurrentSettings();
    }

    private Control BuildUI()
    {
        // ComfyUI URL
        _comfyUrlTextBox = new TextBox();

        // Default Preset
        _presetDropDown = new DropDown();
        foreach (var preset in Enum.GetValues(typeof(PresetType)))
            _presetDropDown.Items.Add(preset.ToString());

        // Auto Generate
        _autoGenerateCheckBox = new CheckBox { Text = "Auto-generate on viewport change" };

        // Debounce
        _debounceStepper = new NumericStepper { MinValue = 50, MaxValue = 2000, Increment = 50 };

        // Default Prompt
        _defaultPromptTextBox = new TextBox();

        // Denoise Strength
        _denoiseStepper = new NumericStepper { MinValue = 0.0, MaxValue = 1.0, Increment = 0.05, DecimalPlaces = 2 };

        // Capture dimensions
        _captureWidthStepper = new NumericStepper { MinValue = 128, MaxValue = 2048, Increment = 64 };
        _captureHeightStepper = new NumericStepper { MinValue = 128, MaxValue = 2048, Increment = 64 };

        // Buttons
        var saveButton = new Button { Text = "Save" };
        saveButton.Click += OnSaveClicked;

        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Click += (s, e) => Close();

        // Layout
        var layout = new DynamicLayout { DefaultSpacing = new Size(8, 8), Padding = new Padding(16) };

        layout.BeginVertical();
        layout.AddRow(new Label { Text = "ComfyUI URL:" });
        layout.AddRow(_comfyUrlTextBox);
        layout.AddRow(new Label { Text = "Default Preset:" });
        layout.AddRow(_presetDropDown);
        layout.AddRow(_autoGenerateCheckBox);
        layout.AddRow(new Label { Text = "Debounce (ms):" });
        layout.AddRow(_debounceStepper);
        layout.AddRow(new Label { Text = "Default Prompt:" });
        layout.AddRow(_defaultPromptTextBox);
        layout.AddRow(new Label { Text = "Denoise Strength:" });
        layout.AddRow(_denoiseStepper);
        layout.AddRow(new Label { Text = "Capture Width:" });
        layout.AddRow(_captureWidthStepper);
        layout.AddRow(new Label { Text = "Capture Height:" });
        layout.AddRow(_captureHeightStepper);
        layout.EndVertical();

        layout.Add(null); // spacer

        layout.BeginVertical();
        layout.BeginHorizontal();
        layout.Add(null, xscale: true);
        layout.Add(cancelButton);
        layout.Add(saveButton);
        layout.EndHorizontal();
        layout.EndVertical();

        return layout;
    }

    private void LoadCurrentSettings()
    {
        var settings = GlimpseAIPlugin.Instance?.GlimpseSettings ?? new GlimpseSettings();

        _comfyUrlTextBox.Text = settings.ComfyUIUrl;
        _presetDropDown.SelectedIndex = (int)settings.DefaultPreset;
        _autoGenerateCheckBox.Checked = settings.AutoGenerateEnabled;
        _debounceStepper.Value = settings.DebounceMs;
        _defaultPromptTextBox.Text = settings.DefaultPrompt;
        _denoiseStepper.Value = settings.DenoiseStrength;
        _captureWidthStepper.Value = settings.CaptureWidth;
        _captureHeightStepper.Value = settings.CaptureHeight;
    }

    private void OnSaveClicked(object sender, EventArgs e)
    {
        var settings = new GlimpseSettings
        {
            ComfyUIUrl = _comfyUrlTextBox.Text,
            DefaultPreset = (PresetType)_presetDropDown.SelectedIndex,
            AutoGenerateEnabled = _autoGenerateCheckBox.Checked ?? true,
            DebounceMs = (int)_debounceStepper.Value,
            DefaultPrompt = _defaultPromptTextBox.Text,
            DenoiseStrength = _denoiseStepper.Value,
            CaptureWidth = (int)_captureWidthStepper.Value,
            CaptureHeight = (int)_captureHeightStepper.Value
        };

        GlimpseAIPlugin.Instance?.UpdateGlimpseSettings(settings);
        RhinoApp.WriteLine("Glimpse AI: Settings saved.");
        Close();
    }
}
