using System.Text.Json;

namespace GlimpseAI.Models;

/// <summary>
/// Persistent settings for the Glimpse AI plugin.
/// </summary>
public class GlimpseSettings
{
    /// <summary>ComfyUI base URL.</summary>
    public string ComfyUIUrl { get; set; } = "http://localhost:8188";

    /// <summary>Default positive prompt.</summary>
    public string DefaultPrompt { get; set; } = "photorealistic architectural rendering, realistic materials and textures, wood, concrete, glass, metal, natural lighting, warm colors, 8k uhd";

    /// <summary>Default negative prompt.</summary>
    public string DefaultNegativePrompt { get; set; } = "blurry, low quality, distorted, ugly, cartoon, sketch";

    /// <summary>Active quality preset.</summary>
    public PresetType ActivePreset { get; set; } = PresetType.Balanced;

    /// <summary>Denoise strength (0.0–1.0).</summary>
    public double DenoiseStrength { get; set; } = 0.75;

    /// <summary>Whether auto-generate on viewport change is enabled.</summary>
    public bool AutoGenerate { get; set; } = false;

    /// <summary>Debounce delay in milliseconds for viewport change detection.</summary>
    public int DebounceMs { get; set; } = 300;

    /// <summary>Viewport capture width in pixels.</summary>
    public int CaptureWidth { get; set; } = 512;

    /// <summary>Viewport capture height in pixels.</summary>
    public int CaptureHeight { get; set; } = 384;

    /// <summary>Whether to use ControlNet for depth-guided generation (Fast preset always uses img2img).</summary>
    public bool UseControlNet { get; set; } = true;

    /// <summary>ControlNet strength (0.0–1.0) - how much the depth structure influences generation.</summary>
    public double ControlNetStrength { get; set; } = 0.7;

    /// <summary>Preferred ControlNet model name (auto-detected if empty).</summary>
    public string ControlNetModel { get; set; } = "";

    /// <summary>Whether to use depth preprocessor node instead of raw viewport image.</summary>
    public bool UseDepthPreprocessor { get; set; } = false;

    /// <summary>Prompt mode for auto-prompt feature.</summary>
    public PromptMode PromptMode { get; set; } = PromptMode.Manual;

    /// <summary>Style preset for auto-prompt generation.</summary>
    public StylePreset StylePreset { get; set; } = StylePreset.Architecture;

    /// <summary>Custom style suffix when StylePreset is Custom.</summary>
    public string CustomStyleSuffix { get; set; } = "";

    /// <summary>Serialize to JSON string.</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Deserialize from JSON string.</summary>
    public static GlimpseSettings FromJson(string json)
    {
        return JsonSerializer.Deserialize<GlimpseSettings>(json);
    }
}
