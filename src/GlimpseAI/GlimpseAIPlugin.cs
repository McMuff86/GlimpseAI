using System;
using Rhino;
using Rhino.PlugIns;
using GlimpseAI.Models;

namespace GlimpseAI;

/// <summary>
/// Main plugin class for Glimpse AI - Real-time AI Preview Rendering for Rhino 8.
/// </summary>
[System.Runtime.InteropServices.Guid("A3F7B2E1-9C84-4D6F-B5E3-1A2D8F4C6E90")]
public class GlimpseAIPlugin : PlugIn
{
    /// <summary>
    /// Gets the singleton instance of the plugin.
    /// </summary>
    public static GlimpseAIPlugin Instance { get; private set; }

    /// <summary>
    /// Current plugin settings.
    /// </summary>
    private GlimpseSettings _glimpseSettings;

    public GlimpseAIPlugin()
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current Glimpse AI settings.
    /// </summary>
    public GlimpseSettings GlimpseSettings
    {
        get
        {
            if (_glimpseSettings == null)
                _glimpseSettings = LoadGlimpseSettings();
            return _glimpseSettings;
        }
    }

    /// <summary>
    /// Called when the plugin is being loaded.
    /// </summary>
    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        RhinoApp.WriteLine("Glimpse AI loaded.");
        _glimpseSettings = LoadGlimpseSettings();
        return LoadReturnCode.Success;
    }

    /// <summary>
    /// Loads settings from the Rhino persistent settings store.
    /// </summary>
    private GlimpseSettings LoadGlimpseSettings()
    {
        try
        {
            var json = Settings.GetString("GlimpseSettingsJson", null);
            if (!string.IsNullOrEmpty(json))
            {
                var loaded = GlimpseSettings.FromJson(json);
                if (loaded != null)
                    return loaded;
            }
        }
        catch
        {
            // Fall through to defaults
        }
        return new GlimpseSettings();
    }

    /// <summary>
    /// Saves the current settings to the Rhino persistent settings store.
    /// </summary>
    public void SaveGlimpseSettings()
    {
        try
        {
            var json = _glimpseSettings?.ToJson();
            if (json != null)
            {
                Settings.SetString("GlimpseSettingsJson", json);
            }
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Glimpse AI: Error saving settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates settings and persists them.
    /// </summary>
    public void UpdateGlimpseSettings(GlimpseSettings newSettings)
    {
        _glimpseSettings = newSettings;
        SaveGlimpseSettings();
    }
}
