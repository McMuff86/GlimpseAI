using System;
using System.IO;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;
using GlimpseAI.Models;
using GlimpseAI.Services;

namespace GlimpseAI.UI;

/// <summary>
/// Main dockable panel for Glimpse AI.
/// Displays the AI-rendered preview and provides controls for generation.
/// </summary>
[System.Runtime.InteropServices.Guid("B4E8A1C3-5D72-4F9E-A6B1-3C8D2E5F7A90")]
public class GlimpsePanel : Panel, IPanel
{
    private readonly uint _documentSerialNumber;

    // --- UI Controls ---
    // Preview area
    private ImageView _previewImageView;
    private Label _placeholderLabel;
    private Panel _previewContainer;

    // Prompt area
    private TextArea _promptTextArea;
    private DropDown _promptModeDropDown;
    private DropDown _stylePresetDropDown;
    private Label _stylePresetLabel;

    // Controls row
    private DropDown _presetDropDown;
    private Slider _denoiseSlider;
    private Label _denoiseValueLabel;
    private TextBox _seedTextBox;

    // Overlay controls
    private Button _overlayToggleButton;
    private Slider _opacitySlider;
    private Label _opacityValueLabel;

    // Progress display
    private ProgressBar _progressBar;
    private Label _progressLabel;

    // Buttons row
    private Button _generateButton;
    private Button _autoToggleButton;
    private Button _saveButton;

    // Status bar
    private Label _generationTimeLabel;
    private Label _resolutionLabel;
    private Label _modelNameLabel;
    private Label _connectionStatusLabel;

    // --- State ---
    private GlimpseOrchestrator _orchestrator;
    private bool _autoModeActive;
    private bool _overlayActive;
    private Bitmap _currentPreviewBitmap;

    /// <summary>
    /// Gets the panel GUID for registration.
    /// </summary>
    public static Guid PanelId => typeof(GlimpsePanel).GUID;

    /// <summary>
    /// Creates a new Glimpse AI panel.
    /// </summary>
    public GlimpsePanel(uint documentSerialNumber)
    {
        _documentSerialNumber = documentSerialNumber;
        Content = BuildUI();
        InitializeOrchestrator();
    }

    #region UI Construction

    /// <summary>
    /// Builds the complete panel UI layout.
    /// </summary>
    private Control BuildUI()
    {
        var layout = new DynamicLayout
        {
            DefaultSpacing = new Size(4, 4),
            Padding = new Padding(6)
        };

        layout.Add(BuildPreviewArea(), yscale: true);
        layout.Add(BuildProgressRow());
        layout.Add(BuildPromptArea());
        layout.Add(BuildControlsRow());
        layout.Add(BuildOverlayRow());
        layout.Add(BuildButtonsRow());
        layout.Add(BuildStatusBar());

        return layout;
    }

    /// <summary>
    /// Builds the large image preview area with placeholder text.
    /// </summary>
    private Control BuildPreviewArea()
    {
        _previewImageView = new ImageView();

        _placeholderLabel = new Label
        {
            Text = "Click Generate or enable Auto mode",
            TextColor = Colors.Gray,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _previewContainer = new Panel
        {
            BackgroundColor = Color.FromArgb(35, 35, 38),
            MinimumSize = new Size(200, 150),
            Content = _placeholderLabel
        };

        return _previewContainer;
    }

    /// <summary>
    /// Builds the progress row showing sampling step and progress bar.
    /// </summary>
    private Control BuildProgressRow()
    {
        _progressLabel = new Label
        {
            Text = "",
            TextColor = Colors.Gray,
            Font = SystemFonts.Default(8)
        };

        _progressBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 0
        };

        var layout = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _progressLabel,
                new StackLayoutItem(_progressBar, expand: true)
            }
        };

        return layout;
    }

    /// <summary>
    /// Builds the prompt input area (TextArea, 2-3 lines high) with auto-prompt controls.
    /// </summary>
    private Control BuildPromptArea()
    {
        var settings = GetSettings();

        // Prompt Mode dropdown
        _promptModeDropDown = new DropDown();
        _promptModeDropDown.Items.Add("Manual");
        _promptModeDropDown.Items.Add("Auto Basic");
        _promptModeDropDown.Items.Add("Auto Vision");
        _promptModeDropDown.SelectedIndex = (int)settings.PromptMode;
        _promptModeDropDown.SelectedIndexChanged += OnPromptModeChanged;

        // Style Preset dropdown
        _stylePresetDropDown = new DropDown();
        _stylePresetDropDown.Items.Add("Architecture");
        _stylePresetDropDown.Items.Add("Artistic");
        _stylePresetDropDown.Items.Add("Textured");
        _stylePresetDropDown.Items.Add("Dramatic");
        _stylePresetDropDown.Items.Add("Minimal");
        _stylePresetDropDown.Items.Add("Nature");
        _stylePresetDropDown.Items.Add("Interior");
        _stylePresetDropDown.Items.Add("Custom");
        _stylePresetDropDown.SelectedIndex = (int)settings.StylePreset;
        _stylePresetDropDown.SelectedIndexChanged += OnStylePresetChanged;
        _stylePresetDropDown.Visible = settings.PromptMode != PromptMode.Manual;

        _stylePresetLabel = new Label { Text = "Style:" };
        _stylePresetLabel.Visible = settings.PromptMode != PromptMode.Manual;

        _promptTextArea = new TextArea
        {
            Text = settings.DefaultPrompt,
            Height = 54, // ~3 lines
            Wrap = true,
            SpellCheck = false,
            ReadOnly = settings.PromptMode != PromptMode.Manual
        };

        // Update prompt text if auto-mode is active
        if (settings.PromptMode != PromptMode.Manual)
        {
            UpdateAutoPromptDisplay();
        }

        // Top row with prompt label and mode toggle
        var topRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                new Label { Text = "Prompt:", Font = SystemFonts.Bold() },
                new StackLayoutItem(null, expand: true),
                new Label { Text = "Mode:" },
                _promptModeDropDown
            }
        };

        // Style row (visible only in auto mode)
        var styleRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _stylePresetLabel,
                _stylePresetDropDown,
                new StackLayoutItem(null, expand: true)
            }
        };

        var layout = new DynamicLayout { DefaultSpacing = new Size(4, 2) };
        layout.Add(topRow);
        layout.Add(styleRow);
        layout.Add(_promptTextArea);
        return layout;
    }

    /// <summary>
    /// Builds the controls row: Preset dropdown, Denoise slider, Seed input.
    /// </summary>
    private Control BuildControlsRow()
    {
        var settings = GetSettings();

        // Preset dropdown
        _presetDropDown = new DropDown();
        _presetDropDown.Items.Add("Fast");
        _presetDropDown.Items.Add("Balanced");
        _presetDropDown.Items.Add("High Quality");
        _presetDropDown.Items.Add("4K Export");
        _presetDropDown.SelectedIndex = (int)settings.ActivePreset;
        _presetDropDown.SelectedIndexChanged += OnPresetChanged;

        // Denoise slider (0-100 mapped to 0.1-1.0)
        int sliderValue = (int)((settings.DenoiseStrength - 0.1) / 0.9 * 100);
        _denoiseSlider = new Slider
        {
            MinValue = 0,
            MaxValue = 100,
            Value = Math.Clamp(sliderValue, 0, 100),
            Width = 100
        };
        _denoiseSlider.ValueChanged += OnDenoiseChanged;

        _denoiseValueLabel = new Label
        {
            Text = settings.DenoiseStrength.ToString("F2"),
            Width = 32
        };

        // Seed text box
        _seedTextBox = new TextBox
        {
            PlaceholderText = "Random",
            Width = 70
        };

        var layout = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                new Label { Text = "Preset:" },
                _presetDropDown,
                new Label { Text = "Denoise:" },
                _denoiseSlider,
                _denoiseValueLabel,
                new Label { Text = "Seed:" },
                _seedTextBox
            }
        };

        return layout;
    }

    /// <summary>
    /// Builds the overlay controls row: Toggle button and opacity slider.
    /// </summary>
    private Control BuildOverlayRow()
    {
        _overlayToggleButton = new Button { Text = "Overlay: Off" };
        _overlayToggleButton.Click += OnOverlayToggleClicked;

        _opacitySlider = new Slider
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 85,
            Width = 100,
            Enabled = false
        };
        _opacitySlider.ValueChanged += OnOpacityChanged;

        _opacityValueLabel = new Label
        {
            Text = "85%",
            Width = 36,
            TextColor = Colors.Gray
        };

        var layout = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _overlayToggleButton,
                new Label { Text = "Opacity:" },
                _opacitySlider,
                _opacityValueLabel
            }
        };

        return layout;
    }

    /// <summary>
    /// Builds the buttons row: Generate, Auto toggle, Save.
    /// </summary>
    private Control BuildButtonsRow()
    {
        _generateButton = new Button { Text = "Generate" };
        _generateButton.Click += OnGenerateClicked;

        _autoToggleButton = new Button { Text = "Auto" };
        _autoToggleButton.Click += OnAutoToggleClicked;

        _saveButton = new Button { Text = "Save", Enabled = false };
        _saveButton.Click += OnSaveClicked;

        var layout = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Items =
            {
                new StackLayoutItem(_generateButton, expand: true),
                new StackLayoutItem(_autoToggleButton, expand: true),
                new StackLayoutItem(_saveButton, expand: true)
            }
        };

        return layout;
    }

    /// <summary>
    /// Builds the bottom status bar.
    /// </summary>
    private Control BuildStatusBar()
    {
        _generationTimeLabel = new Label
        {
            Text = "—",
            TextColor = Colors.Gray,
            Font = SystemFonts.Default(8)
        };
        _resolutionLabel = new Label
        {
            Text = "",
            TextColor = Colors.Gray,
            Font = SystemFonts.Default(8)
        };
        _modelNameLabel = new Label
        {
            Text = "",
            TextColor = Colors.Gray,
            Font = SystemFonts.Default(8)
        };
        _connectionStatusLabel = new Label
        {
            Text = "...",
            Font = SystemFonts.Default(8)
        };

        var layout = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Padding = new Padding(2, 4),
            Items =
            {
                _generationTimeLabel,
                _resolutionLabel,
                _modelNameLabel,
                new StackLayoutItem(null, expand: true),
                _connectionStatusLabel
            }
        };

        return layout;
    }

    #endregion

    #region Orchestrator Wiring

    /// <summary>
    /// Creates and wires the GlimpseOrchestrator that connects UI to Services.
    /// </summary>
    private void InitializeOrchestrator()
    {
        var settings = GetSettings();
        _orchestrator = new GlimpseOrchestrator(settings.ComfyUIUrl, settings.DebounceMs);

        _orchestrator.RenderCompleted += OnRenderCompleted;
        _orchestrator.StatusChanged += OnStatusChanged;
        _orchestrator.BusyChanged += OnBusyChanged;
        _orchestrator.ProgressChanged += OnProgressChanged;
        _orchestrator.PreviewImageReceived += OnPreviewImageReceived;
    }

    /// <summary>
    /// Called on the background thread when a render finishes. Marshals to UI.
    /// </summary>
    private void OnRenderCompleted(object sender, RenderResult result)
    {
        Application.Instance.Invoke(() =>
        {
            // Reset progress
            _progressBar.Value = 0;
            _progressLabel.Text = "";

            if (result.Success && result.ImageData != null)
            {
                ShowPreviewImage(result.ImageData);
                _generationTimeLabel.Text = $"{result.Elapsed.TotalSeconds:F1}s";
                _saveButton.Enabled = true;

                if (_currentPreviewBitmap != null)
                {
                    _resolutionLabel.Text = $"{_currentPreviewBitmap.Width}x{_currentPreviewBitmap.Height}";
                }

                _modelNameLabel.Text = result.Preset.ToString();
            }
            else
            {
                _generationTimeLabel.Text = $"Error ({result.Elapsed.TotalSeconds:F1}s)";
                RhinoApp.WriteLine($"Glimpse AI: {result.ErrorMessage}");
            }
        });
    }

    /// <summary>
    /// Called when the orchestrator status text changes. Marshals to UI.
    /// </summary>
    private void OnStatusChanged(object sender, string status)
    {
        Application.Instance.Invoke(() =>
        {
            _generationTimeLabel.Text = status;
        });
    }

    /// <summary>
    /// Called when the orchestrator busy state changes. Marshals to UI.
    /// </summary>
    private void OnBusyChanged(object sender, bool busy)
    {
        Application.Instance.Invoke(() =>
        {
            _generateButton.Enabled = !busy;
            _presetDropDown.Enabled = !busy;
            _denoiseSlider.Enabled = !busy;
            _seedTextBox.Enabled = !busy;

            if (busy)
            {
                _progressBar.Value = 0;
                _progressLabel.Text = "Generating...";
            }
        });
    }

    /// <summary>
    /// Called when ComfyUI reports sampling progress. Marshals to UI.
    /// </summary>
    private void OnProgressChanged(object sender, ProgressEventArgs e)
    {
        Application.Instance.Invoke(() =>
        {
            if (e.TotalSteps > 0)
            {
                _progressBar.MaxValue = e.TotalSteps;
                _progressBar.Value = Math.Min(e.Step, e.TotalSteps);
                _progressLabel.Text = $"Step {e.Step}/{e.TotalSteps}";
            }
        });
    }

    /// <summary>
    /// Called when a latent preview image is received. Shows it in the preview area.
    /// </summary>
    private void OnPreviewImageReceived(object sender, PreviewImageEventArgs e)
    {
        Application.Instance.Invoke(() =>
        {
            ShowPreviewImage(e.ImageData);
        });
    }

    #endregion

    #region Image Display

    /// <summary>
    /// Displays PNG bytes in the preview area, replacing the placeholder.
    /// </summary>
    private void ShowPreviewImage(byte[] imageData)
    {
        try
        {
            _currentPreviewBitmap?.Dispose();

            using var ms = new MemoryStream(imageData);
            _currentPreviewBitmap = new Bitmap(ms);

            _previewImageView.Image = _currentPreviewBitmap;
            _previewContainer.Content = _previewImageView;
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Glimpse AI: Failed to display image: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the preview and restores the placeholder.
    /// </summary>
    private void ClearPreviewImage()
    {
        _currentPreviewBitmap?.Dispose();
        _currentPreviewBitmap = null;
        _previewImageView.Image = null;
        _previewContainer.Content = _placeholderLabel;
        _saveButton.Enabled = false;
    }

    #endregion

    #region UI Event Handlers

    private void OnGenerateClicked(object sender, EventArgs e)
    {
        var prompt = _promptTextArea.Text;
        var preset = (PresetType)_presetDropDown.SelectedIndex;
        var denoise = GetCurrentDenoise();
        var seed = ParseSeed(_seedTextBox.Text);

        _orchestrator.RequestGenerate(prompt, preset, denoise, seed);
    }

    private void OnAutoToggleClicked(object sender, EventArgs e)
    {
        _autoModeActive = !_autoModeActive;

        if (_autoModeActive)
        {
            _autoToggleButton.Text = "Stop Auto";
            _orchestrator.StartAutoMode(
                _promptTextArea.Text,
                (PresetType)_presetDropDown.SelectedIndex,
                GetCurrentDenoise(),
                ParseSeed(_seedTextBox.Text));
        }
        else
        {
            _autoToggleButton.Text = "Auto";
            _orchestrator.StopAutoMode();
        }
    }

    private void OnOverlayToggleClicked(object sender, EventArgs e)
    {
        _overlayActive = !_overlayActive;

        if (_overlayActive)
        {
            _overlayToggleButton.Text = "Overlay: On";
            _opacitySlider.Enabled = true;
            _orchestrator.SetOverlayEnabled(true);
        }
        else
        {
            _overlayToggleButton.Text = "Overlay: Off";
            _opacitySlider.Enabled = false;
            _orchestrator.SetOverlayEnabled(false);
        }
    }

    private void OnOpacityChanged(object sender, EventArgs e)
    {
        var opacity = _opacitySlider.Value / 100.0;
        _opacityValueLabel.Text = $"{_opacitySlider.Value}%";
        _orchestrator.SetOverlayOpacity(opacity);
    }

    private void OnSaveClicked(object sender, EventArgs e)
    {
        if (_currentPreviewBitmap == null) return;

        var dialog = new Eto.Forms.SaveFileDialog
        {
            Title = "Save AI Preview",
            Filters = { new FileFilter("PNG Image", ".png") }
        };

        if (dialog.ShowDialog(this) == DialogResult.Ok)
        {
            try
            {
                _currentPreviewBitmap.Save(dialog.FileName, ImageFormat.Png);
                RhinoApp.WriteLine($"Glimpse AI: Image saved to {dialog.FileName}");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Glimpse AI: Error saving image: {ex.Message}");
            }
        }
    }

    private void OnPresetChanged(object sender, EventArgs e)
    {
        var preset = (PresetType)_presetDropDown.SelectedIndex;
        var settings = GetSettings();
        settings.ActivePreset = preset;
        GlimpseAIPlugin.Instance?.UpdateGlimpseSettings(settings);

        if (_autoModeActive)
        {
            _orchestrator.UpdateAutoSettings(
                _promptTextArea.Text,
                preset,
                GetCurrentDenoise(),
                ParseSeed(_seedTextBox.Text));
        }
    }

    private void OnDenoiseChanged(object sender, EventArgs e)
    {
        var denoise = GetCurrentDenoise();
        _denoiseValueLabel.Text = denoise.ToString("F2");

        var settings = GetSettings();
        settings.DenoiseStrength = denoise;
        GlimpseAIPlugin.Instance?.UpdateGlimpseSettings(settings);

        if (_autoModeActive)
        {
            _orchestrator.UpdateAutoSettings(
                _promptTextArea.Text,
                (PresetType)_presetDropDown.SelectedIndex,
                denoise,
                ParseSeed(_seedTextBox.Text));
        }
    }

    #endregion

    #region IPanel Implementation

    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
    {
        RhinoApp.WriteLine("Glimpse AI panel shown.");
        CheckComfyUIConnection();
    }

    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason)
    {
        if (_autoModeActive)
        {
            _orchestrator.StopAutoMode();
        }
    }

    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
    {
        try
        {
            // Stop auto mode first to prevent new captures
            if (_autoModeActive)
            {
                _autoModeActive = false;
                _orchestrator?.StopAutoMode();
            }

            // Dispose orchestrator (cancels running generations, disconnects WebSocket)
            _orchestrator?.Dispose();
            _orchestrator = null;

            _currentPreviewBitmap?.Dispose();
            _currentPreviewBitmap = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Glimpse AI: Error during panel close: {ex.Message}");
        }
    }

    #endregion

    #region Auto-Prompt Event Handlers

    private void OnPromptModeChanged(object sender, EventArgs e)
    {
        var promptMode = (PromptMode)_promptModeDropDown.SelectedIndex;
        var settings = GetSettings();
        settings.PromptMode = promptMode;
        GlimpseAIPlugin.Instance?.UpdateGlimpseSettings(settings);

        // Update UI visibility
        _stylePresetDropDown.Visible = promptMode != PromptMode.Manual;
        _stylePresetLabel.Visible = promptMode != PromptMode.Manual;
        _promptTextArea.ReadOnly = promptMode != PromptMode.Manual;

        // Update prompt display
        if (promptMode == PromptMode.Manual)
        {
            // Restore manual prompt
            _promptTextArea.Text = settings.DefaultPrompt;
        }
        else
        {
            // Show auto-generated prompt
            UpdateAutoPromptDisplay();
        }

        // Update auto-mode if active
        if (_autoModeActive)
        {
            _orchestrator.UpdateAutoSettings(
                _promptTextArea.Text,
                (PresetType)_presetDropDown.SelectedIndex,
                GetCurrentDenoise(),
                ParseSeed(_seedTextBox.Text));
        }
    }

    private void OnStylePresetChanged(object sender, EventArgs e)
    {
        var stylePreset = (StylePreset)_stylePresetDropDown.SelectedIndex;
        var settings = GetSettings();
        settings.StylePreset = stylePreset;
        GlimpseAIPlugin.Instance?.UpdateGlimpseSettings(settings);

        // Update prompt display if in auto mode
        if (settings.PromptMode != PromptMode.Manual)
        {
            UpdateAutoPromptDisplay();
        }

        // Update auto-mode if active
        if (_autoModeActive)
        {
            _orchestrator.UpdateAutoSettings(
                _promptTextArea.Text,
                (PresetType)_presetDropDown.SelectedIndex,
                GetCurrentDenoise(),
                ParseSeed(_seedTextBox.Text));
        }
    }

    /// <summary>
    /// Updates the prompt text area with an auto-generated prompt for preview.
    /// </summary>
    private void UpdateAutoPromptDisplay()
    {
        var settings = GetSettings();
        
        if (settings.PromptMode == PromptMode.Manual)
            return;

        try
        {
            string previewPrompt;
            
            if (settings.PromptMode == PromptMode.AutoBasic)
            {
                var doc = RhinoDoc.ActiveDoc;
                previewPrompt = AutoPromptBuilder.BuildFromScene(
                    doc, 
                    settings.StylePreset, 
                    settings.CustomStyleSuffix);
            }
            else // AutoVision
            {
                // For vision mode, show a placeholder until actual generation
                var doc = RhinoDoc.ActiveDoc;
                var basicPrompt = AutoPromptBuilder.BuildFromScene(
                    doc, 
                    settings.StylePreset, 
                    settings.CustomStyleSuffix);
                previewPrompt = $"[Vision Analysis + Style] Preview: {basicPrompt}";
            }

            _promptTextArea.Text = previewPrompt;
        }
        catch (Exception ex)
        {
            _promptTextArea.Text = $"[Auto-Prompt Error: {ex.Message}]";
            RhinoApp.WriteLine($"Glimpse AI: Auto-prompt preview failed: {ex.Message}");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Maps the slider's 0-100 integer range to 0.10-1.00.
    /// </summary>
    private double GetCurrentDenoise()
    {
        return 0.1 + (_denoiseSlider.Value / 100.0) * 0.9;
    }

    /// <summary>
    /// Parses the seed text box. Returns -1 (random) if empty or invalid.
    /// </summary>
    private static int ParseSeed(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        return int.TryParse(text, out var seed) ? seed : -1;
    }

    /// <summary>
    /// Returns the current plugin settings (never null).
    /// </summary>
    private static GlimpseSettings GetSettings()
    {
        return GlimpseAIPlugin.Instance?.GlimpseSettings ?? new GlimpseSettings();
    }

    /// <summary>
    /// Checks ComfyUI connectivity and updates the status indicator.
    /// </summary>
    private void CheckComfyUIConnection()
    {
        _connectionStatusLabel.Text = "...";

        System.Threading.Tasks.Task.Run(async () =>
        {
            var available = await _orchestrator.CheckConnectionAsync();
            Application.Instance.Invoke(() =>
            {
                _connectionStatusLabel.Text = available ? "ComfyUI OK" : "ComfyUI offline";
                _connectionStatusLabel.TextColor = available ? Colors.Green : Colors.Red;
            });
        });
    }

    #endregion
}
