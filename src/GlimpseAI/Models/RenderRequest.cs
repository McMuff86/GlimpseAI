namespace GlimpseAI.Models;

/// <summary>
/// Represents a request to generate an AI-rendered image from viewport data.
/// </summary>
public class RenderRequest
{
    /// <summary>
    /// Captured viewport image as PNG bytes.
    /// </summary>
    public byte[] ViewportImage { get; set; }

    /// <summary>
    /// Optional depth pass image as PNG bytes.
    /// </summary>
    public byte[] DepthImage { get; set; }

    /// <summary>
    /// Positive prompt describing desired output.
    /// </summary>
    public string Prompt { get; set; }

    /// <summary>
    /// Negative prompt describing what to avoid.
    /// </summary>
    public string NegativePrompt { get; set; }

    /// <summary>
    /// Rendering preset controlling quality/speed.
    /// </summary>
    public PresetType Preset { get; set; }

    /// <summary>
    /// Denoise strength (0.0 = keep original, 1.0 = full generation).
    /// </summary>
    public double DenoiseStrength { get; set; }

    /// <summary>
    /// Random seed for reproducible generation. -1 for random.
    /// </summary>
    public int Seed { get; set; } = -1;

    /// <summary>
    /// CFG Scale for prompt guidance (1.0-20.0).
    /// </summary>
    public double CfgScale { get; set; } = 7.0;

    /// <summary>
    /// Whether to use Flux pipeline instead of SDXL/SD1.5.
    /// </summary>
    public bool UseFlux { get; set; } = false;

    /// <summary>
    /// Flux UNet model name (required if UseFlux=true).
    /// </summary>
    public string FluxUnetModel { get; set; }

    /// <summary>
    /// Flux CLIP model 1 (required if UseFlux=true).
    /// </summary>
    public string FluxClip1 { get; set; }

    /// <summary>
    /// Flux CLIP model 2 (required if UseFlux=true).
    /// </summary>
    public string FluxClip2 { get; set; }

    /// <summary>
    /// Flux VAE model (required if UseFlux=true).
    /// </summary>
    public string FluxVae { get; set; }

    /// <summary>
    /// Flux ControlNet model (for non-Fast presets).
    /// </summary>
    public string FluxControlNet { get; set; }
}
