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

    /// <summary>CFG Scale for prompt guidance (1.0–20.0).</summary>
    public double CfgScale { get; set; } = 7.0;

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

    /// <summary>Whether to use aggressive memory cleanup after generation.</summary>
    public bool AggressiveMemoryCleanup { get; set; } = false;

    /// <summary>Preferred pipeline: "auto", "flux", or "sdxl".</summary>
    public string PreferredPipeline { get; set; } = "auto";

    /// <summary>Selected model name ("auto" = let pipeline decide, or specific model filename).</summary>
    public string SelectedModel { get; set; } = "auto";

    /// <summary>Whether to prefer Flux models over SDXL when available.</summary>
    public bool PreferFlux { get; set; } = true;

    /// <summary>Preferred Flux UNet model name (auto-detected if empty).</summary>
    public string FluxUNetModel { get; set; } = "";

    /// <summary>Flux CLIP model 1 (typically clip_l.safetensors).</summary>
    public string FluxClipModel1 { get; set; } = "clip_l.safetensors";

    /// <summary>Flux CLIP model 2 (typically t5xxl_fp8_e4m3fn.safetensors).</summary>
    public string FluxClipModel2 { get; set; } = "t5xxl_fp8_e4m3fn.safetensors";

    /// <summary>Flux VAE model (typically ae.safetensors).</summary>
    public string FluxVaeModel { get; set; } = "ae.safetensors";

    /// <summary>Flux ControlNet model (typically InstantX_FLUX.1-dev-Controlnet-Union.safetensors).</summary>
    public string FluxControlNetModel { get; set; } = "";

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
