using System.Collections.Generic;
using GlimpseAI.Models;

namespace GlimpseAI.Services;

/// <summary>
/// Builds ComfyUI API-format workflow dictionaries for each rendering preset.
///
/// Fast preset uses img2img:
///   LoadImage (viewport) → VAEEncode → KSampler (denoise ~0.8) → VAEDecode → SaveImage
///
/// Other presets use ControlNet Depth for better structure preservation:
///   LoadImage (viewport) ──→ ControlNetApply (depth_strength ~0.7)
///                              ↓
///   EmptyLatentImage ────→ KSampler (denoise 1.0!) → VAEDecode → SaveImage
///   CLIPTextEncode (pos) ─┘
///   CLIPTextEncode (neg) ─┘
///   CheckpointLoader ─────┘
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
    /// <param name="checkpointName">Checkpoint model filename (auto-detected).</param>
    /// <param name="controlNetModel">ControlNet model filename (for non-Fast presets), or null to disable ControlNet.</param>
    /// <param name="controlNetStrength">ControlNet strength (0.0–1.0).</param>
    /// <param name="useDepthPreprocessor">Whether to use depth preprocessor node instead of raw viewport image.</param>
    /// <param name="useWebSocketOutput">If true, uses SaveImageWebsocket instead of SaveImage for streaming output.</param>
    /// <returns>Workflow dictionary ready for ComfyUI /prompt API.</returns>
    public static Dictionary<string, object> BuildWorkflow(
        PresetType preset,
        string viewportImageName,
        string depthImageName,
        string prompt,
        string negativePrompt,
        double denoise,
        int seed,
        string checkpointName,
        string controlNetModel = null,
        double controlNetStrength = 0.7,
        bool useDepthPreprocessor = false,
        bool useWebSocketOutput = false)
    {
        return preset switch
        {
            PresetType.Fast => BuildFastWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName, useWebSocketOutput),
            PresetType.Balanced => BuildBalancedWorkflow(viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName, controlNetModel, controlNetStrength, useDepthPreprocessor, useWebSocketOutput),
            PresetType.HighQuality => BuildHQWorkflow(viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName, controlNetModel, controlNetStrength, useDepthPreprocessor, useWebSocketOutput),
            PresetType.Export4K => BuildExport4KWorkflow(viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName, controlNetModel, controlNetStrength, useDepthPreprocessor, useWebSocketOutput),
            _ => BuildFastWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName, useWebSocketOutput)
        };
    }

    /// <summary>
    /// Fast Preview: ~1-2s, 8 steps, 512x384, img2img with higher denoise.
    /// </summary>
    private static Dictionary<string, object> BuildFastWorkflow(
        string viewportImageName, string prompt, string negativePrompt,
        double denoise, int seed, string checkpointName, bool useWebSocketOutput)
    {
        // Fast preset keeps img2img for speed, but uses higher denoise and cfg
        var actualDenoise = Math.Max(denoise, 0.8); // Ensure minimum 0.8 denoise
        return BuildImg2ImgWorkflow(
            viewportImageName, prompt, negativePrompt, actualDenoise, seed, checkpointName,
            steps: 8, cfg: 5.0, samplerName: "dpmpp_sde", scheduler: "karras",
            filenamePrefix: "GlimpseAI/fast", useWebSocketOutput: useWebSocketOutput);
    }

    /// <summary>
    /// Balanced: ~5-8s, 20 steps, 1024x768, ControlNet Depth with denoise 1.0.
    /// </summary>
    private static Dictionary<string, object> BuildBalancedWorkflow(
        string viewportImageName, string depthImageName, string prompt, string negativePrompt,
        int seed, string checkpointName, string controlNetModel, double controlNetStrength,
        bool useDepthPreprocessor, bool useWebSocketOutput)
    {
        // If no ControlNet model available, fallback to img2img
        if (string.IsNullOrEmpty(controlNetModel))
        {
            return BuildImg2ImgWorkflow(
                viewportImageName, prompt, negativePrompt, denoise: 0.85, seed, checkpointName,
                steps: 20, cfg: 7.0, samplerName: "dpmpp_2m", scheduler: "karras",
                filenamePrefix: "GlimpseAI/balanced", useWebSocketOutput: useWebSocketOutput);
        }

        return BuildControlNetWorkflow(
            viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName,
            controlNetModel, controlNetStrength, useDepthPreprocessor,
            steps: 20, cfg: 7.0, samplerName: "dpmpp_2m", scheduler: "karras",
            width: 1024, height: 768, filenamePrefix: "GlimpseAI/balanced", useWebSocketOutput: useWebSocketOutput);
    }

    /// <summary>
    /// High Quality: ~20-30s, 30 steps, 1024x768, ControlNet Depth with denoise 1.0.
    /// </summary>
    private static Dictionary<string, object> BuildHQWorkflow(
        string viewportImageName, string depthImageName, string prompt, string negativePrompt,
        int seed, string checkpointName, string controlNetModel, double controlNetStrength,
        bool useDepthPreprocessor, bool useWebSocketOutput)
    {
        // If no ControlNet model available, fallback to img2img
        if (string.IsNullOrEmpty(controlNetModel))
        {
            return BuildImg2ImgWorkflow(
                viewportImageName, prompt, negativePrompt, denoise: 0.9, seed, checkpointName,
                steps: 30, cfg: 7.5, samplerName: "dpmpp_2m", scheduler: "karras",
                filenamePrefix: "GlimpseAI/hq", useWebSocketOutput: useWebSocketOutput);
        }

        return BuildControlNetWorkflow(
            viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName,
            controlNetModel, controlNetStrength, useDepthPreprocessor,
            steps: 30, cfg: 7.5, samplerName: "dpmpp_2m", scheduler: "karras",
            width: 1024, height: 768, filenamePrefix: "GlimpseAI/hq", useWebSocketOutput: useWebSocketOutput);
    }

    /// <summary>
    /// Export 4K: ControlNet Depth + UltraSharp 4x upscale → 4096x3072 output.
    /// Always uses SaveImage (no WebSocket output for large files).
    /// </summary>
    private static Dictionary<string, object> BuildExport4KWorkflow(
        string viewportImageName, string depthImageName, string prompt, string negativePrompt,
        int seed, string checkpointName, string controlNetModel, double controlNetStrength,
        bool useDepthPreprocessor, bool useWebSocketOutput)
    {
        Dictionary<string, object> workflow;

        // If no ControlNet model available, fallback to img2img
        if (string.IsNullOrEmpty(controlNetModel))
        {
            workflow = BuildImg2ImgWorkflow(
                viewportImageName, prompt, negativePrompt, denoise: 0.9, seed, checkpointName,
                steps: 30, cfg: 7.5, samplerName: "dpmpp_2m", scheduler: "karras",
                filenamePrefix: "GlimpseAI/export4k", useWebSocketOutput: false);
        }
        else
        {
            // 4K export always saves to disk — too large for WebSocket streaming
            workflow = BuildControlNetWorkflow(
                viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName,
                controlNetModel, controlNetStrength, useDepthPreprocessor,
                steps: 30, cfg: 7.5, samplerName: "dpmpp_2m", scheduler: "karras",
                width: 1024, height: 768, filenamePrefix: "GlimpseAI/export4k", useWebSocketOutput: false);
        }

        // Add upscale nodes — save the upscaled output instead
        workflow["20"] = MakeNode("UpscaleModelLoader", new Dictionary<string, object>
        {
            ["model_name"] = "4x-UltraSharp.pth"
        });

        workflow["21"] = MakeNode("ImageUpscaleWithModel", new Dictionary<string, object>(),
            new Dictionary<string, object>
            {
                ["upscale_model"] = new object[] { "20", 0 },
                ["image"] = new object[] { "7", 0 }  // VAEDecode output
            });

        // Redirect SaveImage to use upscaled output
        workflow["8"] = MakeNode("SaveImage", new Dictionary<string, object>
        {
            ["filename_prefix"] = "GlimpseAI/export4k"
        }, new Dictionary<string, object>
        {
            ["images"] = new object[] { "21", 0 }
        });

        return workflow;
    }

    /// <summary>
    /// Builds a standard img2img workflow with the given parameters.
    /// When useWebSocketOutput is true, uses SaveImageWebsocket to stream the result
    /// back via WebSocket instead of saving to disk.
    /// </summary>
    private static Dictionary<string, object> BuildImg2ImgWorkflow(
        string viewportImageName, string prompt, string negativePrompt,
        double denoise, int seed, string checkpointName,
        int steps, double cfg, string samplerName, string scheduler,
        string filenamePrefix, bool useWebSocketOutput = false)
    {
        var workflow = new Dictionary<string, object>
        {
            // Node 1: CheckpointLoaderSimple
            ["1"] = MakeNode("CheckpointLoaderSimple", new Dictionary<string, object>
            {
                ["ckpt_name"] = checkpointName
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
                ["steps"] = steps,
                ["cfg"] = cfg,
                ["sampler_name"] = samplerName,
                ["scheduler"] = scheduler,
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
                })
        };

        // Node 8: Output — SaveImageWebsocket streams via WS, SaveImage writes to disk
        if (useWebSocketOutput)
        {
            workflow["8"] = MakeNode("SaveImageWebsocket", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["images"] = new object[] { "7", 0 }
                });
        }
        else
        {
            workflow["8"] = MakeNode("SaveImage", new Dictionary<string, object>
            {
                ["filename_prefix"] = filenamePrefix
            }, new Dictionary<string, object>
            {
                ["images"] = new object[] { "7", 0 }
            });
        }

        return workflow;
    }

    /// <summary>
    /// Builds a ControlNet Depth workflow with EmptyLatentImage + denoise 1.0 for architectural rendering.
    /// The viewport image provides depth structure, but KSampler generates completely new content.
    /// </summary>
    private static Dictionary<string, object> BuildControlNetWorkflow(
        string viewportImageName, string depthImageName, string prompt, string negativePrompt,
        int seed, string checkpointName, string controlNetModel, double controlNetStrength,
        bool useDepthPreprocessor, int steps, double cfg, string samplerName, string scheduler,
        int width, int height, string filenamePrefix, bool useWebSocketOutput = false)
    {
        var workflow = new Dictionary<string, object>
        {
            // Node 1: CheckpointLoaderSimple
            ["1"] = MakeNode("CheckpointLoaderSimple", new Dictionary<string, object>
            {
                ["ckpt_name"] = checkpointName
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

            // Node 11: ControlNetLoader
            ["11"] = MakeNode("ControlNetLoader", new Dictionary<string, object>
            {
                ["control_net_name"] = controlNetModel
            })
        };

        string depthImageInput;
        
        if (useDepthPreprocessor)
        {
            // Node 14: Depth preprocessor (DepthAnything_V2 or MiDaS-DepthMapPreprocessor)
            workflow["14"] = MakeNode("DepthAnything_V2", new Dictionary<string, object>
            {
                ["resolution"] = Math.Max(width, height)
            }, new Dictionary<string, object>
            {
                ["image"] = new object[] { "4", 0 }
            });
            depthImageInput = "14";
        }
        else
        {
            // Use viewport image directly for ControlNet (it will extract depth internally)
            depthImageInput = "4";
        }

        // Node 12: ControlNetApplyAdvanced
        workflow["12"] = MakeNode("ControlNetApplyAdvanced", new Dictionary<string, object>
        {
            ["strength"] = controlNetStrength,
            ["start_percent"] = 0.0,
            ["end_percent"] = 1.0
        }, new Dictionary<string, object>
        {
            ["positive"] = new object[] { "2", 0 },
            ["negative"] = new object[] { "3", 0 },
            ["control_net"] = new object[] { "11", 0 },
            ["image"] = new object[] { depthImageInput, 0 }
        });

        // Node 13: EmptyLatentImage (instead of VAEEncode from viewport!)
        workflow["13"] = MakeNode("EmptyLatentImage", new Dictionary<string, object>
        {
            ["width"] = width,
            ["height"] = height,
            ["batch_size"] = 1
        });

        // Node 6: KSampler (uses ControlNet conditioning + EmptyLatentImage + denoise 1.0)
        workflow["6"] = MakeNode("KSampler", new Dictionary<string, object>
        {
            ["seed"] = seed,
            ["steps"] = steps,
            ["cfg"] = cfg,
            ["sampler_name"] = samplerName,
            ["scheduler"] = scheduler,
            ["denoise"] = 1.0  // Full denoise! AI generates completely new content with depth structure
        }, new Dictionary<string, object>
        {
            ["model"] = new object[] { "1", 0 },
            ["positive"] = new object[] { "12", 0 },  // ControlNet positive
            ["negative"] = new object[] { "12", 1 },  // ControlNet negative
            ["latent_image"] = new object[] { "13", 0 }  // EmptyLatentImage, not VAEEncoded viewport!
        });

        // Node 7: VAEDecode (latent → image)
        workflow["7"] = MakeNode("VAEDecode", new Dictionary<string, object>(),
            new Dictionary<string, object>
            {
                ["samples"] = new object[] { "6", 0 },
                ["vae"] = new object[] { "1", 2 }
            });

        // Node 8: Output — SaveImageWebsocket streams via WS, SaveImage writes to disk
        if (useWebSocketOutput)
        {
            workflow["8"] = MakeNode("SaveImageWebsocket", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["images"] = new object[] { "7", 0 }
                });
        }
        else
        {
            workflow["8"] = MakeNode("SaveImage", new Dictionary<string, object>
            {
                ["filename_prefix"] = filenamePrefix
            }, new Dictionary<string, object>
            {
                ["images"] = new object[] { "7", 0 }
            });
        }

        return workflow;
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
