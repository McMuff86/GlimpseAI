using System.Collections.Generic;
using GlimpseAI.Models;

namespace GlimpseAI.Services;

/// <summary>
/// Builds ComfyUI API-format workflow dictionaries for each rendering preset.
/// 
/// All workflows follow the img2img pattern:
///   LoadImage (viewport) → VAEEncode → KSampler (denoise &lt; 1.0) → VAEDecode → SaveImage
///   CLIPTextEncode (positive) ─┐
///   CLIPTextEncode (negative) ─┤→ KSampler
///   CheckpointLoaderSimple ────┘
/// </summary>
public static class WorkflowBuilder
{
    /// <summary>
    /// Builds a ComfyUI workflow dictionary for the given preset and parameters.
    /// </summary>
    /// <param name="preset">Quality preset.</param>
    /// <param name="viewportImageName">Filename of the uploaded viewport capture.</param>
    /// <param name="depthImageName">Filename of the uploaded depth image, or null.</param>
    /// <param name="prompt">Positive prompt text.</param>
    /// <param name="negativePrompt">Negative prompt text.</param>
    /// <param name="denoise">Denoise strength (0.0–1.0).</param>
    /// <param name="seed">Random seed.</param>
    /// <returns>Workflow dictionary ready for ComfyUI /prompt API.</returns>
    public static Dictionary<string, object> BuildWorkflow(
        PresetType preset,
        string viewportImageName,
        string depthImageName,
        string prompt,
        string negativePrompt,
        double denoise,
        int seed)
    {
        return preset switch
        {
            PresetType.Fast => BuildFastWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed),
            PresetType.Balanced => BuildBalancedWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed),
            PresetType.HighQuality => BuildHQWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed),
            PresetType.Export4K => BuildExport4KWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed),
            _ => BuildFastWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed)
        };
    }

    /// <summary>
    /// Fast Preview: ~1-2s, 8 steps, 512x384, DreamShaper XL Turbo.
    /// </summary>
    private static Dictionary<string, object> BuildFastWorkflow(
        string viewportImageName, string prompt, string negativePrompt, double denoise, int seed)
    {
        return new Dictionary<string, object>
        {
            // Node 1: CheckpointLoaderSimple
            ["1"] = MakeNode("CheckpointLoaderSimple", new Dictionary<string, object>
            {
                ["ckpt_name"] = "dreamshaperXL_turboDPMSDE.safetensors"
            }),

            // Node 2: CLIPTextEncode (positive prompt)
            ["2"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = prompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "1", 1 }
            }),

            // Node 3: CLIPTextEncode (negative prompt)
            ["3"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = negativePrompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "1", 1 }
            }),

            // Node 4: LoadImage (viewport capture)
            ["4"] = MakeNode("LoadImage", new Dictionary<string, object>
            {
                ["image"] = viewportImageName
            }),

            // Node 5: VAEEncode (viewport image → latent)
            ["5"] = MakeNode("VAEEncode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["pixels"] = new object[] { "4", 0 },
                    ["vae"] = new object[] { "1", 2 }
                }),

            // Node 6: KSampler (img2img)
            ["6"] = MakeNode("KSampler", new Dictionary<string, object>
            {
                ["seed"] = seed,
                ["steps"] = 8,
                ["cfg"] = 2.0,
                ["sampler_name"] = "dpmpp_sde",
                ["scheduler"] = "karras",
                ["denoise"] = denoise
            }, new Dictionary<string, object>
            {
                ["model"] = new object[] { "1", 0 },
                ["positive"] = new object[] { "2", 0 },
                ["negative"] = new object[] { "3", 0 },
                ["latent_image"] = new object[] { "5", 0 }
            }),

            // Node 7: VAEDecode (latent → image)
            ["7"] = MakeNode("VAEDecode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["samples"] = new object[] { "6", 0 },
                    ["vae"] = new object[] { "1", 2 }
                }),

            // Node 8: SaveImage
            ["8"] = MakeNode("SaveImage", new Dictionary<string, object>
            {
                ["filename_prefix"] = "GlimpseAI/fast"
            }, new Dictionary<string, object>
            {
                ["images"] = new object[] { "7", 0 }
            })
        };
    }

    /// <summary>
    /// Balanced: ~5-8s, 12 steps, 768x576, Juggernaut XL Lightning.
    /// </summary>
    private static Dictionary<string, object> BuildBalancedWorkflow(
        string viewportImageName, string prompt, string negativePrompt, double denoise, int seed)
    {
        return new Dictionary<string, object>
        {
            // Node 1: CheckpointLoaderSimple
            ["1"] = MakeNode("CheckpointLoaderSimple", new Dictionary<string, object>
            {
                ["ckpt_name"] = "juggernautXL_v9Rdphoto2Lightning.safetensors"
            }),

            // Node 2: CLIPTextEncode (positive prompt)
            ["2"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = prompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "1", 1 }
            }),

            // Node 3: CLIPTextEncode (negative prompt)
            ["3"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = negativePrompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "1", 1 }
            }),

            // Node 4: LoadImage (viewport capture)
            ["4"] = MakeNode("LoadImage", new Dictionary<string, object>
            {
                ["image"] = viewportImageName
            }),

            // Node 5: VAEEncode
            ["5"] = MakeNode("VAEEncode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["pixels"] = new object[] { "4", 0 },
                    ["vae"] = new object[] { "1", 2 }
                }),

            // Node 6: KSampler
            ["6"] = MakeNode("KSampler", new Dictionary<string, object>
            {
                ["seed"] = seed,
                ["steps"] = 12,
                ["cfg"] = 4.0,
                ["sampler_name"] = "dpmpp_2m",
                ["scheduler"] = "karras",
                ["denoise"] = denoise
            }, new Dictionary<string, object>
            {
                ["model"] = new object[] { "1", 0 },
                ["positive"] = new object[] { "2", 0 },
                ["negative"] = new object[] { "3", 0 },
                ["latent_image"] = new object[] { "5", 0 }
            }),

            // Node 7: VAEDecode
            ["7"] = MakeNode("VAEDecode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["samples"] = new object[] { "6", 0 },
                    ["vae"] = new object[] { "1", 2 }
                }),

            // Node 8: SaveImage
            ["8"] = MakeNode("SaveImage", new Dictionary<string, object>
            {
                ["filename_prefix"] = "GlimpseAI/balanced"
            }, new Dictionary<string, object>
            {
                ["images"] = new object[] { "7", 0 }
            })
        };
    }

    /// <summary>
    /// High Quality: ~20-30s, 30 steps, 1024x768, dvArch Exterior.
    /// </summary>
    private static Dictionary<string, object> BuildHQWorkflow(
        string viewportImageName, string prompt, string negativePrompt, double denoise, int seed)
    {
        return new Dictionary<string, object>
        {
            // Node 1: CheckpointLoaderSimple
            ["1"] = MakeNode("CheckpointLoaderSimple", new Dictionary<string, object>
            {
                ["ckpt_name"] = "dvarchMultiPrompt_dvarchExterior.safetensors"
            }),

            // Node 2: CLIPTextEncode (positive prompt)
            ["2"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = prompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "1", 1 }
            }),

            // Node 3: CLIPTextEncode (negative prompt)
            ["3"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = negativePrompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "1", 1 }
            }),

            // Node 4: LoadImage (viewport capture)
            ["4"] = MakeNode("LoadImage", new Dictionary<string, object>
            {
                ["image"] = viewportImageName
            }),

            // Node 5: VAEEncode
            ["5"] = MakeNode("VAEEncode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["pixels"] = new object[] { "4", 0 },
                    ["vae"] = new object[] { "1", 2 }
                }),

            // Node 6: KSampler
            ["6"] = MakeNode("KSampler", new Dictionary<string, object>
            {
                ["seed"] = seed,
                ["steps"] = 30,
                ["cfg"] = 7.5,
                ["sampler_name"] = "dpmpp_2m",
                ["scheduler"] = "karras",
                ["denoise"] = denoise
            }, new Dictionary<string, object>
            {
                ["model"] = new object[] { "1", 0 },
                ["positive"] = new object[] { "2", 0 },
                ["negative"] = new object[] { "3", 0 },
                ["latent_image"] = new object[] { "5", 0 }
            }),

            // Node 7: VAEDecode
            ["7"] = MakeNode("VAEDecode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["samples"] = new object[] { "6", 0 },
                    ["vae"] = new object[] { "1", 2 }
                }),

            // Node 8: SaveImage
            ["8"] = MakeNode("SaveImage", new Dictionary<string, object>
            {
                ["filename_prefix"] = "GlimpseAI/hq"
            }, new Dictionary<string, object>
            {
                ["images"] = new object[] { "7", 0 }
            })
        };
    }

    /// <summary>
    /// Export 4K: Like HQ + UltraSharp 4x upscale → 4096x3072 output.
    /// </summary>
    private static Dictionary<string, object> BuildExport4KWorkflow(
        string viewportImageName, string prompt, string negativePrompt, double denoise, int seed)
    {
        return new Dictionary<string, object>
        {
            // Node 1: CheckpointLoaderSimple
            ["1"] = MakeNode("CheckpointLoaderSimple", new Dictionary<string, object>
            {
                ["ckpt_name"] = "dvarchMultiPrompt_dvarchExterior.safetensors"
            }),

            // Node 2: CLIPTextEncode (positive prompt)
            ["2"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = prompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "1", 1 }
            }),

            // Node 3: CLIPTextEncode (negative prompt)
            ["3"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = negativePrompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "1", 1 }
            }),

            // Node 4: LoadImage (viewport capture)
            ["4"] = MakeNode("LoadImage", new Dictionary<string, object>
            {
                ["image"] = viewportImageName
            }),

            // Node 5: VAEEncode
            ["5"] = MakeNode("VAEEncode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["pixels"] = new object[] { "4", 0 },
                    ["vae"] = new object[] { "1", 2 }
                }),

            // Node 6: KSampler (HQ settings)
            ["6"] = MakeNode("KSampler", new Dictionary<string, object>
            {
                ["seed"] = seed,
                ["steps"] = 30,
                ["cfg"] = 7.5,
                ["sampler_name"] = "dpmpp_2m",
                ["scheduler"] = "karras",
                ["denoise"] = denoise
            }, new Dictionary<string, object>
            {
                ["model"] = new object[] { "1", 0 },
                ["positive"] = new object[] { "2", 0 },
                ["negative"] = new object[] { "3", 0 },
                ["latent_image"] = new object[] { "5", 0 }
            }),

            // Node 7: VAEDecode
            ["7"] = MakeNode("VAEDecode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["samples"] = new object[] { "6", 0 },
                    ["vae"] = new object[] { "1", 2 }
                }),

            // Node 9: UpscaleModelLoader (4x-UltraSharp)
            ["9"] = MakeNode("UpscaleModelLoader", new Dictionary<string, object>
            {
                ["model_name"] = "4x-UltraSharp.pth"
            }),

            // Node 10: ImageUpscaleWithModel
            ["10"] = MakeNode("ImageUpscaleWithModel", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["upscale_model"] = new object[] { "9", 0 },
                    ["image"] = new object[] { "7", 0 }
                }),

            // Node 8: SaveImage (saves the upscaled image)
            ["8"] = MakeNode("SaveImage", new Dictionary<string, object>
            {
                ["filename_prefix"] = "GlimpseAI/export4k"
            }, new Dictionary<string, object>
            {
                ["images"] = new object[] { "10", 0 }
            })
        };
    }

    #region Helper Methods

    /// <summary>
    /// Creates a ComfyUI node dictionary with only widget inputs (no linked inputs).
    /// </summary>
    private static Dictionary<string, object> MakeNode(string classType, Dictionary<string, object> inputs)
    {
        return new Dictionary<string, object>
        {
            ["class_type"] = classType,
            ["inputs"] = inputs
        };
    }

    /// <summary>
    /// Creates a ComfyUI node dictionary, merging widget values and linked inputs.
    /// Linked inputs use the ComfyUI format: ["nodeId", outputIndex].
    /// </summary>
    private static Dictionary<string, object> MakeNode(
        string classType,
        Dictionary<string, object> widgetInputs,
        Dictionary<string, object> linkedInputs)
    {
        var allInputs = new Dictionary<string, object>(widgetInputs);
        foreach (var kvp in linkedInputs)
        {
            allInputs[kvp.Key] = kvp.Value;
        }

        return new Dictionary<string, object>
        {
            ["class_type"] = classType,
            ["inputs"] = allInputs
        };
    }

    #endregion
}
