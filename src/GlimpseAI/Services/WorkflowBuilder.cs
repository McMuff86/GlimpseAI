using System;
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
    /// Supports both SDXL/SD1.5 (CheckpointLoaderSimple) and Flux (UNETLoader + DualCLIPLoader) pipelines.
    /// </summary>
    /// <param name="preset">Quality preset.</param>
    /// <param name="viewportImageName">Filename of the uploaded viewport capture.</param>
    /// <param name="depthImageName">Filename of the uploaded depth image, or null.</param>
    /// <param name="prompt">Positive prompt text.</param>
    /// <param name="negativePrompt">Negative prompt text.</param>
    /// <param name="denoise">Denoise strength (0.0–1.0).</param>
    /// <param name="seed">Random seed.</param>
    /// <param name="checkpointName">Checkpoint model filename (auto-detected), or null for Flux.</param>
    /// <param name="cfgScale">CFG Scale for prompt guidance (1.0–20.0).</param>
    /// <param name="controlNetModel">ControlNet model filename (for non-Fast presets), or null to disable ControlNet.</param>
    /// <param name="controlNetStrength">ControlNet strength (0.0–1.0).</param>
    /// <param name="useDepthPreprocessor">Whether to use depth preprocessor node instead of raw viewport image.</param>
    /// <param name="useWebSocketOutput">If true, uses SaveImageWebsocket instead of SaveImage for streaming output.</param>
    /// <param name="useFlux">Whether to use Flux pipeline instead of SDXL.</param>
    /// <param name="fluxUnetModel">Flux UNet model name (required if useFlux=true).</param>
    /// <param name="fluxClip1">Flux CLIP model 1 (required if useFlux=true).</param>
    /// <param name="fluxClip2">Flux CLIP model 2 (required if useFlux=true).</param>
    /// <param name="fluxVae">Flux VAE model (required if useFlux=true).</param>
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
        double cfgScale = 7.0,
        string controlNetModel = null,
        double controlNetStrength = 0.7,
        bool useDepthPreprocessor = false,
        bool useWebSocketOutput = false,
        bool useFlux = false,
        string fluxUnetModel = null,
        string fluxClip1 = null,
        string fluxClip2 = null,
        string fluxVae = null)
    {
        if (useFlux)
        {
            // Validate Flux parameters
            if (string.IsNullOrEmpty(fluxUnetModel) || string.IsNullOrEmpty(fluxClip1) || 
                string.IsNullOrEmpty(fluxClip2) || string.IsNullOrEmpty(fluxVae))
            {
                throw new ArgumentException("Flux models (UNet, CLIP1, CLIP2, VAE) are required when useFlux=true");
            }

            // Flux uses different CFG and denoise ranges than SDXL
            // Override user values with Flux-appropriate defaults per preset
            var fluxCfg = preset switch
            {
                PresetType.Fast => 1.5,
                _ => 3.5
            };
            var fluxDenoise = preset switch
            {
                PresetType.Fast => 0.70,
                _ => 1.0  // Full denoise for ControlNet presets
            };

            return preset switch
            {
                PresetType.Fast => BuildFluxImg2ImgWorkflow(viewportImageName, prompt, fluxDenoise, seed, 
                    fluxUnetModel, fluxClip1, fluxClip2, fluxVae, fluxCfg, useWebSocketOutput),
                PresetType.Balanced => BuildFluxControlNetWorkflow(viewportImageName, prompt, seed,
                    fluxUnetModel, fluxClip1, fluxClip2, fluxVae, controlNetModel, controlNetStrength,
                    steps: 20, cfg: fluxCfg, width: 1024, height: 768, filenamePrefix: "GlimpseAI/flux_balanced", useWebSocketOutput),
                PresetType.HighQuality => BuildFluxControlNetWorkflow(viewportImageName, prompt, seed,
                    fluxUnetModel, fluxClip1, fluxClip2, fluxVae, controlNetModel, controlNetStrength,
                    steps: 28, cfg: fluxCfg, width: 1024, height: 768, filenamePrefix: "GlimpseAI/flux_hq", useWebSocketOutput),
                PresetType.Export4K => BuildFluxControlNetWorkflow(viewportImageName, prompt, seed,
                    fluxUnetModel, fluxClip1, fluxClip2, fluxVae, controlNetModel, controlNetStrength,
                    steps: 28, cfg: fluxCfg, width: 1024, height: 768, filenamePrefix: "GlimpseAI/flux_4k", useWebSocketOutput: false),
                _ => BuildFluxImg2ImgWorkflow(viewportImageName, prompt, fluxDenoise, seed, 
                    fluxUnetModel, fluxClip1, fluxClip2, fluxVae, fluxCfg, useWebSocketOutput)
            };
        }
        else
        {
            // Original SDXL/SD1.5 workflow
            return preset switch
            {
                PresetType.Fast => BuildFastWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName, cfgScale, useWebSocketOutput),
                PresetType.Balanced => BuildBalancedWorkflow(viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName, cfgScale, controlNetModel, controlNetStrength, useDepthPreprocessor, useWebSocketOutput),
                PresetType.HighQuality => BuildHQWorkflow(viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName, cfgScale, controlNetModel, controlNetStrength, useDepthPreprocessor, useWebSocketOutput),
                PresetType.Export4K => BuildExport4KWorkflow(viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName, cfgScale, controlNetModel, controlNetStrength, useDepthPreprocessor, useWebSocketOutput),
                _ => BuildFastWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName, cfgScale, useWebSocketOutput)
            };
        }
    }

    /// <summary>
    /// Fast Preview: ~1-2s, 8 steps, 512x384, img2img with higher denoise.
    /// </summary>
    private static Dictionary<string, object> BuildFastWorkflow(
        string viewportImageName, string prompt, string negativePrompt,
        double denoise, int seed, string checkpointName, double cfgScale, bool useWebSocketOutput)
    {
        // Fast preset keeps img2img for speed, but uses higher denoise
        var actualDenoise = Math.Max(denoise, 0.8); // Ensure minimum 0.8 denoise
        return BuildImg2ImgWorkflow(
            viewportImageName, prompt, negativePrompt, actualDenoise, seed, checkpointName,
            steps: 8, cfg: cfgScale, samplerName: "dpmpp_sde", scheduler: "karras",
            filenamePrefix: "GlimpseAI/fast", useWebSocketOutput: useWebSocketOutput);
    }

    /// <summary>
    /// Balanced: ~5-8s, 20 steps, 1024x768, ControlNet Depth with denoise 1.0.
    /// </summary>
    private static Dictionary<string, object> BuildBalancedWorkflow(
        string viewportImageName, string depthImageName, string prompt, string negativePrompt,
        int seed, string checkpointName, double cfgScale, string controlNetModel, double controlNetStrength,
        bool useDepthPreprocessor, bool useWebSocketOutput)
    {
        // If no ControlNet model available, fallback to img2img
        if (string.IsNullOrEmpty(controlNetModel))
        {
            return BuildImg2ImgWorkflow(
                viewportImageName, prompt, negativePrompt, denoise: 0.85, seed, checkpointName,
                steps: 20, cfg: cfgScale, samplerName: "dpmpp_2m", scheduler: "karras",
                filenamePrefix: "GlimpseAI/balanced", useWebSocketOutput: useWebSocketOutput);
        }

        return BuildControlNetWorkflow(
            viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName,
            controlNetModel, controlNetStrength, useDepthPreprocessor,
            steps: 20, cfg: cfgScale, samplerName: "dpmpp_2m", scheduler: "karras",
            width: 1024, height: 768, filenamePrefix: "GlimpseAI/balanced", useWebSocketOutput: useWebSocketOutput);
    }

    /// <summary>
    /// High Quality: ~20-30s, 30 steps, 1024x768, ControlNet Depth with denoise 1.0.
    /// </summary>
    private static Dictionary<string, object> BuildHQWorkflow(
        string viewportImageName, string depthImageName, string prompt, string negativePrompt,
        int seed, string checkpointName, double cfgScale, string controlNetModel, double controlNetStrength,
        bool useDepthPreprocessor, bool useWebSocketOutput)
    {
        // If no ControlNet model available, fallback to img2img
        if (string.IsNullOrEmpty(controlNetModel))
        {
            return BuildImg2ImgWorkflow(
                viewportImageName, prompt, negativePrompt, denoise: 0.9, seed, checkpointName,
                steps: 30, cfg: cfgScale, samplerName: "dpmpp_2m", scheduler: "karras",
                filenamePrefix: "GlimpseAI/hq", useWebSocketOutput: useWebSocketOutput);
        }

        return BuildControlNetWorkflow(
            viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName,
            controlNetModel, controlNetStrength, useDepthPreprocessor,
            steps: 30, cfg: cfgScale, samplerName: "dpmpp_2m", scheduler: "karras",
            width: 1024, height: 768, filenamePrefix: "GlimpseAI/hq", useWebSocketOutput: useWebSocketOutput);
    }

    /// <summary>
    /// Export 4K: ControlNet Depth + UltraSharp 4x upscale → 4096x3072 output.
    /// Always uses SaveImage (no WebSocket output for large files).
    /// </summary>
    private static Dictionary<string, object> BuildExport4KWorkflow(
        string viewportImageName, string depthImageName, string prompt, string negativePrompt,
        int seed, string checkpointName, double cfgScale, string controlNetModel, double controlNetStrength,
        bool useDepthPreprocessor, bool useWebSocketOutput)
    {
        Dictionary<string, object> workflow;

        // If no ControlNet model available, fallback to img2img
        if (string.IsNullOrEmpty(controlNetModel))
        {
            workflow = BuildImg2ImgWorkflow(
                viewportImageName, prompt, negativePrompt, denoise: 0.9, seed, checkpointName,
                steps: 30, cfg: cfgScale, samplerName: "dpmpp_2m", scheduler: "karras",
                filenamePrefix: "GlimpseAI/export4k", useWebSocketOutput: false);
        }
        else
        {
            // 4K export always saves to disk — too large for WebSocket streaming
            workflow = BuildControlNetWorkflow(
                viewportImageName, depthImageName, prompt, negativePrompt, seed, checkpointName,
                controlNetModel, controlNetStrength, useDepthPreprocessor,
                steps: 30, cfg: cfgScale, samplerName: "dpmpp_2m", scheduler: "karras",
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

    #region Flux Workflows

    /// <summary>
    /// Builds a Flux img2img workflow for Fast preset.
    /// Uses VAEEncode on viewport image with lower denoise for speed.
    /// Flux CFG is much lower (1.0-3.5) and uses euler + simple scheduler.
    /// </summary>
    private static Dictionary<string, object> BuildFluxImg2ImgWorkflow(
        string viewportImageName, string prompt, double denoise, int seed,
        string fluxUnetModel, string fluxClip1, string fluxClip2, string fluxVae,
        double cfgScale = 1.5, bool useWebSocketOutput = false)
    {
        // Flux Fast preset settings
        var steps = 6;
        var actualDenoise = Math.Max(denoise, 0.70); // Minimum 0.70 for Fast
        var actualCfg = Math.Min(cfgScale, 2.0); // Cap CFG at 2.0 for Fast

        var workflow = new Dictionary<string, object>
        {
            // Node 1: UNETLoader (Flux UNet)
            ["1"] = MakeNode("UNETLoader", new Dictionary<string, object>
            {
                ["unet_name"] = fluxUnetModel,
                ["weight_dtype"] = "fp8_e4m3fn"
            }),

            // Node 2: DualCLIPLoader (Flux CLIP)
            ["2"] = MakeNode("DualCLIPLoader", new Dictionary<string, object>
            {
                ["clip_name1"] = fluxClip1,
                ["clip_name2"] = fluxClip2,
                ["type"] = "flux"
            }),

            // Node 3: VAELoader (Flux VAE)
            ["3"] = MakeNode("VAELoader", new Dictionary<string, object>
            {
                ["vae_name"] = fluxVae
            }),

            // Node 4: CLIPTextEncode (positive prompt only - Flux doesn't use negative)
            ["4"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = prompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "2", 0 }
            }),

            // Node 5: LoadImage (viewport capture)
            ["5"] = MakeNode("LoadImage", new Dictionary<string, object>
            {
                ["image"] = viewportImageName
            }),

            // Node 6: VAEEncode (viewport image → latent)
            ["6"] = MakeNode("VAEEncode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["pixels"] = new object[] { "5", 0 },
                    ["vae"] = new object[] { "3", 0 }
                }),

            // Node 7: Empty conditioning for negative (Flux needs it as input but empty)
            ["7"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = ""
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "2", 0 }
            }),

            // Node 8: KSampler (Flux img2img)
            ["8"] = MakeNode("KSampler", new Dictionary<string, object>
            {
                ["seed"] = seed,
                ["steps"] = steps,
                ["cfg"] = actualCfg,
                ["sampler_name"] = "euler",
                ["scheduler"] = "simple",
                ["denoise"] = actualDenoise
            }, new Dictionary<string, object>
            {
                ["model"] = new object[] { "1", 0 },
                ["positive"] = new object[] { "4", 0 },
                ["negative"] = new object[] { "7", 0 }, // Empty negative
                ["latent_image"] = new object[] { "6", 0 }
            }),

            // Node 9: VAEDecode (latent → image)
            ["9"] = MakeNode("VAEDecode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["samples"] = new object[] { "8", 0 },
                    ["vae"] = new object[] { "3", 0 }
                })
        };

        // Node 10: Output
        if (useWebSocketOutput)
        {
            workflow["10"] = MakeNode("SaveImageWebsocket", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["images"] = new object[] { "9", 0 }
                });
        }
        else
        {
            workflow["10"] = MakeNode("SaveImage", new Dictionary<string, object>
            {
                ["filename_prefix"] = "GlimpseAI/flux_fast"
            }, new Dictionary<string, object>
            {
                ["images"] = new object[] { "9", 0 }
            });
        }

        return workflow;
    }

    /// <summary>
    /// Builds a Flux ControlNet workflow using InstantX Union ControlNet.
    /// Uses EmptyLatentImage + denoise 1.0 for complete generation guided by depth.
    /// </summary>
    private static Dictionary<string, object> BuildFluxControlNetWorkflow(
        string viewportImageName, string prompt, int seed,
        string fluxUnetModel, string fluxClip1, string fluxClip2, string fluxVae,
        string controlNetModel, double controlNetStrength,
        int steps, double cfg, int width, int height, string filenamePrefix, bool useWebSocketOutput = false)
    {
        // Flux settings
        var actualCfg = Math.Min(cfg, 3.5); // Cap CFG at 3.5 for Flux
        var actualStrength = Math.Min(controlNetStrength, 0.8); // Cap ControlNet strength

        var workflow = new Dictionary<string, object>
        {
            // Node 1: UNETLoader (Flux UNet)
            ["1"] = MakeNode("UNETLoader", new Dictionary<string, object>
            {
                ["unet_name"] = fluxUnetModel,
                ["weight_dtype"] = "fp8_e4m3fn"
            }),

            // Node 2: DualCLIPLoader (Flux CLIP)
            ["2"] = MakeNode("DualCLIPLoader", new Dictionary<string, object>
            {
                ["clip_name1"] = fluxClip1,
                ["clip_name2"] = fluxClip2,
                ["type"] = "flux"
            }),

            // Node 3: VAELoader (Flux VAE)
            ["3"] = MakeNode("VAELoader", new Dictionary<string, object>
            {
                ["vae_name"] = fluxVae
            }),

            // Node 4: CLIPTextEncode (positive prompt)
            ["4"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = prompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "2", 0 }
            }),

            // Node 5: LoadImage (viewport)
            ["5"] = MakeNode("LoadImage", new Dictionary<string, object>
            {
                ["image"] = viewportImageName
            }),

            // Node 6: ControlNetLoader (InstantX Union)
            ["6"] = MakeNode("ControlNetLoader", new Dictionary<string, object>
            {
                ["control_net_name"] = controlNetModel
            }),

            // Node 13: SetUnionControlNetType - set to depth mode for viewport structure preservation
            ["13"] = MakeNode("SetUnionControlNetType", new Dictionary<string, object>
            {
                ["type"] = "depth"
            }, new Dictionary<string, object>
            {
                ["control_net"] = new object[] { "6", 0 }
            }),

            // Node 7: Empty conditioning for negative (required by Flux)
            ["7"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = ""
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "2", 0 }
            }),

            // Node 8: ControlNetApplySD3 (InstantX Union requires VAE as required input)
            ["8"] = MakeNode("ControlNetApplySD3", new Dictionary<string, object>
            {
                ["strength"] = actualStrength,
                ["start_percent"] = 0.0,
                ["end_percent"] = 0.8
            }, new Dictionary<string, object>
            {
                ["positive"] = new object[] { "4", 0 },
                ["negative"] = new object[] { "7", 0 },
                ["control_net"] = new object[] { "13", 0 },  // Use typed ControlNet
                ["image"] = new object[] { "5", 0 },
                ["vae"] = new object[] { "3", 0 }
            }),

            // Node 9: EmptyLatentImage (Flux generates completely new content)
            ["9"] = MakeNode("EmptyLatentImage", new Dictionary<string, object>
            {
                ["width"] = width,
                ["height"] = height,
                ["batch_size"] = 1
            }),

            // Node 10: KSampler
            ["10"] = MakeNode("KSampler", new Dictionary<string, object>
            {
                ["seed"] = seed,
                ["steps"] = steps,
                ["cfg"] = actualCfg,
                ["sampler_name"] = "euler",
                ["scheduler"] = "simple",
                ["denoise"] = 1.0 // Full denoise for Flux ControlNet
            }, new Dictionary<string, object>
            {
                ["model"] = new object[] { "1", 0 },
                ["positive"] = new object[] { "8", 0 }, // ControlNet positive
                ["negative"] = new object[] { "8", 1 }, // ControlNet negative
                ["latent_image"] = new object[] { "9", 0 }
            }),

            // Node 11: VAEDecode
            ["11"] = MakeNode("VAEDecode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["samples"] = new object[] { "10", 0 },
                    ["vae"] = new object[] { "3", 0 }
                })
        };

        // Node 12: Output
        if (useWebSocketOutput)
        {
            workflow["12"] = MakeNode("SaveImageWebsocket", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["images"] = new object[] { "11", 0 }
                });
        }
        else
        {
            workflow["12"] = MakeNode("SaveImage", new Dictionary<string, object>
            {
                ["filename_prefix"] = filenamePrefix
            }, new Dictionary<string, object>
            {
                ["images"] = new object[] { "11", 0 }
            });
        }

        return workflow;
    }

    #endregion

    #region Flux Kontext Monochrome Workflow

    /// <summary>
    /// Default prompt for monochrome architectural model conversion.
    /// </summary>
    public const string DefaultMonochromePrompt =
        "convert image into a clean monochrome architectural model with no textures, ambient lighting and clean smooth geometry while removing details such as people, railings, background, water and trees.";

    /// <summary>
    /// Builds a Flux Kontext workflow that converts an input image into a clean,
    /// untextured monochrome architectural model.
    /// Uses ImageStitch (self-reference) → FluxKontextImageScale → KSampler pipeline.
    /// </summary>
    public static Dictionary<string, object> BuildFluxKontextMonochromeWorkflow(
        string inputImageName, string prompt, long seed, bool useWebSocketOutput = true)
    {
        if (string.IsNullOrEmpty(prompt))
            prompt = DefaultMonochromePrompt;

        var workflow = new Dictionary<string, object>
        {
            // Node 37: UNETLoader (Kontext model)
            ["37"] = MakeNode("UNETLoader", new Dictionary<string, object>
            {
                ["unet_name"] = "flux1-dev-kontext_fp8_scaled.safetensors",
                ["weight_dtype"] = "default"
            }),

            // Node 38: DualCLIPLoader (Flux CLIP)
            ["38"] = MakeNode("DualCLIPLoader", new Dictionary<string, object>
            {
                ["clip_name1"] = "clip_l.safetensors",
                ["clip_name2"] = "t5xxl_fp8_e4m3fn.safetensors",
                ["type"] = "flux",
                ["dual_clip_mode"] = "default"
            }),

            // Node 39: VAELoader
            ["39"] = MakeNode("VAELoader", new Dictionary<string, object>
            {
                ["vae_name"] = "ae.safetensors"
            }),

            // Node 6: CLIPTextEncode (prompt)
            ["6"] = MakeNode("CLIPTextEncode", new Dictionary<string, object>
            {
                ["text"] = prompt
            }, new Dictionary<string, object>
            {
                ["clip"] = new object[] { "38", 0 }
            }),

            // Node 135: ConditioningZeroOut (negative)
            ["135"] = MakeNode("ConditioningZeroOut", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["conditioning"] = new object[] { "6", 0 }
                }),

            // Node 142: LoadImage (input image)
            ["142"] = MakeNode("LoadImage", new Dictionary<string, object>
            {
                ["image"] = inputImageName
            }),

            // Node 146: ImageStitch (stitch input with itself for Kontext reference)
            // Only image1 is connected; the node duplicates it as both reference and target
            ["146"] = MakeNode("ImageStitch", new Dictionary<string, object>
            {
                ["direction"] = "right",
                ["match_image_size"] = true,
                ["spacing_width"] = 0,
                ["spacing_color"] = "white"
            }, new Dictionary<string, object>
            {
                ["image1"] = new object[] { "142", 0 }
            }),

            // Node 42: FluxKontextImageScale
            ["42"] = MakeNode("FluxKontextImageScale", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["image"] = new object[] { "146", 0 }
                }),

            // Node 124: VAEEncode
            ["124"] = MakeNode("VAEEncode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["pixels"] = new object[] { "42", 0 },
                    ["vae"] = new object[] { "39", 0 }
                }),

            // Node 177: ReferenceLatent
            ["177"] = MakeNode("ReferenceLatent", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["conditioning"] = new object[] { "6", 0 },
                    ["latent"] = new object[] { "124", 0 }
                }),

            // Node 35: FluxGuidance
            ["35"] = MakeNode("FluxGuidance", new Dictionary<string, object>
            {
                ["guidance"] = 2.5
            }, new Dictionary<string, object>
            {
                ["conditioning"] = new object[] { "177", 0 }
            }),

            // Node 31: KSampler
            ["31"] = MakeNode("KSampler", new Dictionary<string, object>
            {
                ["seed"] = seed,
                ["control_after_generate"] = "randomize",
                ["steps"] = 20,
                ["cfg"] = 1.0,
                ["sampler_name"] = "euler",
                ["scheduler"] = "simple",
                ["denoise"] = 1.0
            }, new Dictionary<string, object>
            {
                ["model"] = new object[] { "37", 0 },
                ["positive"] = new object[] { "35", 0 },
                ["negative"] = new object[] { "135", 0 },
                ["latent_image"] = new object[] { "124", 0 }
            }),

            // Node 8: VAEDecode
            ["8"] = MakeNode("VAEDecode", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["samples"] = new object[] { "31", 0 },
                    ["vae"] = new object[] { "39", 0 }
                })
        };

        // Node 136: Output
        if (useWebSocketOutput)
        {
            workflow["136"] = MakeNode("SaveImageWebsocket", new Dictionary<string, object>(),
                new Dictionary<string, object>
                {
                    ["images"] = new object[] { "8", 0 }
                });
        }
        else
        {
            workflow["136"] = MakeNode("SaveImage", new Dictionary<string, object>
            {
                ["filename_prefix"] = "GlimpseAI/monochrome"
            }, new Dictionary<string, object>
            {
                ["images"] = new object[] { "8", 0 }
            });
        }

        return workflow;
    }

    #endregion

    #region Hunyuan3D Mesh Workflow

    /// <summary>
    /// Builds a Hunyuan3D v2 mesh generation workflow that takes an input image
    /// and produces a textured 3D GLB mesh through a multi-stage pipeline:
    /// background removal → delight → mesh generation → UV wrap → texture baking → export.
    /// </summary>
    public static Dictionary<string, object> BuildHunyuan3DMeshWorkflow(
        string inputImageName, long seed, bool textured = true)
    {
        var workflow = new Dictionary<string, object>();

        // === Stage 1: Load Models ===
        workflow["10"] = MakeNode("Hy3DModelLoader", new Dictionary<string, object>
        {
            ["model"] = "flux1-dev-fp8.safetensors",
            ["attention_mode"] = "sdpa",
            ["cublas_ops"] = false
        });

        workflow["28"] = MakeNode("DownloadAndLoadHy3DDelightModel", new Dictionary<string, object>
        {
            ["model"] = "hunyuan3d-delight-v2-0"
        });

        workflow["85"] = MakeNode("DownloadAndLoadHy3DPaintModel", new Dictionary<string, object>
        {
            ["model"] = "hunyuan3d-paint-v2-0"
        });

        // === Stage 2: Preprocess Input Image ===
        workflow["50"] = MakeNode("LoadImage", new Dictionary<string, object>
        {
            ["image"] = inputImageName
        });

        workflow["55"] = MakeNode("TransparentBGSession+", new Dictionary<string, object>
        {
            ["mode"] = "base",
            ["use_jit"] = true
        });

        workflow["52"] = MakeNode("ImageResize+", new Dictionary<string, object>
        {
            ["width"] = 518,
            ["height"] = 518,
            ["interpolation"] = "lanczos",
            ["method"] = "pad",
            ["conditions"] = "always",
            ["multiple_of"] = 2,
            ["image"] = new object[] { "50", 0 }
        });

        workflow["56"] = MakeNode("ImageRemoveBackground+", new Dictionary<string, object>
        {
            ["image"] = new object[] { "52", 0 },
            ["rembg_session"] = new object[] { "55", 0 }
        });

        workflow["202"] = MakeNode("InvertMask", new Dictionary<string, object>
        {
            ["mask"] = new object[] { "56", 1 }
        });

        workflow["195"] = MakeNode("JoinImageWithAlpha", new Dictionary<string, object>
        {
            ["image"] = new object[] { "56", 0 },
            ["alpha"] = new object[] { "202", 0 }
        });

        // === Stage 3: Delight (remove lighting) ===
        workflow["35"] = MakeNode("Hy3DDelightImage", new Dictionary<string, object>
        {
            ["steps"] = 50,
            ["width"] = 512,
            ["height"] = 512,
            ["cfg_image"] = 1,
            ["seed"] = 0,
            ["delight_pipe"] = new object[] { "28", 0 },
            ["image"] = new object[] { "195", 0 }
        });

        // === Stage 4: Camera Config ===
        workflow["61"] = MakeNode("Hy3DCameraConfig", new Dictionary<string, object>
        {
            ["camera_azimuths"] = "0, 90, 180, 270, 0, 180",
            ["camera_elevations"] = "0, 0, 0, 0, 90, -90",
            ["view_weights"] = "1, 0.1, 0.5, 0.1, 0.05, 0.05",
            ["camera_distance"] = 1.45,
            ["ortho_scale"] = 1.2
        });

        // === Stage 5: Generate Mesh from Multi-View ===
        workflow["148"] = MakeNode("Hy3DDiffusersSchedulerConfig", new Dictionary<string, object>
        {
            ["scheduler"] = "Euler A",
            ["sigmas"] = "default",
            ["pipeline"] = new object[] { "10", 0 }
        });

        workflow["166"] = MakeNode("Hy3DGenerateMeshMultiView", new Dictionary<string, object>
        {
            ["guidance_scale"] = 5.5,
            ["steps"] = 50,
            ["seed"] = seed,
            ["pipeline"] = new object[] { "148", 0 },
            ["front"] = new object[] { "195", 0 },
            ["scheduler"] = new object[] { "148", 1 }
        });

        // === Stage 6: VAE Decode (latent → mesh) ===
        workflow["140"] = MakeNode("Hy3DVAEDecode", new Dictionary<string, object>
        {
            ["box_v"] = 1.01,
            ["octree_resolution"] = 512,
            ["num_chunks"] = 32000,
            ["mc_level"] = 0,
            ["mc_algo"] = "mc",
            ["enable_flash_vdm"] = true,
            ["force_offload"] = true,
            ["vae"] = new object[] { "10", 1 },
            ["latents"] = new object[] { "166", 0 }
        });

        // === Stage 7: Post-process Mesh ===
        workflow["203"] = MakeNode("Hy3DPostprocessMesh", new Dictionary<string, object>
        {
            ["remove_floaters"] = true,
            ["remove_degenerate_faces"] = true,
            ["reduce_faces"] = true,
            ["max_facenum"] = 50000,
            ["smooth_normals"] = false,
            ["trimesh"] = new object[] { "140", 0 }
        });

        // === Stage 8: UV Wrap ===
        workflow["83"] = MakeNode("Hy3DMeshUVWrap", new Dictionary<string, object>
        {
            ["trimesh"] = new object[] { "203", 0 }
        });

        // === Stage 13a: Export untextured mesh ===
        workflow["17"] = MakeNode("Hy3DExportMesh", new Dictionary<string, object>
        {
            ["filename_prefix"] = "3D/Hy3D",
            ["file_format"] = "glb",
            ["save_file"] = true,
            ["trimesh"] = new object[] { "83", 0 }
        });

        if (textured)
        {
            // === Stage 9: Render views for texture baking ===
            workflow["79"] = MakeNode("Hy3DRenderMultiView", new Dictionary<string, object>
            {
                ["render_size"] = 1024,
                ["texture_size"] = 2048,
                ["normal_space"] = "world",
                ["trimesh"] = new object[] { "83", 0 },
                ["camera_config"] = new object[] { "61", 0 }
            });

            // === Stage 10: Paint/Sample Multi-View textures ===
            workflow["149"] = MakeNode("Hy3DDiffusersSchedulerConfig", new Dictionary<string, object>
            {
                ["scheduler"] = "Euler A",
                ["sigmas"] = "default",
                ["pipeline"] = new object[] { "85", 0 }
            });

            workflow["132"] = MakeNode("SolidMask", new Dictionary<string, object>
            {
                ["value"] = 0.8,
                ["width"] = 512,
                ["height"] = 512
            });

            workflow["133"] = MakeNode("MaskToImage", new Dictionary<string, object>
            {
                ["mask"] = new object[] { "132", 0 }
            });

            workflow["184"] = MakeNode("RepeatImageBatch", new Dictionary<string, object>
            {
                ["amount"] = 3,
                ["image"] = new object[] { "133", 0 }
            });

            workflow["64"] = MakeNode("ImageCompositeMasked", new Dictionary<string, object>
            {
                ["x"] = 0,
                ["y"] = 0,
                ["resize_source"] = false,
                ["destination"] = new object[] { "79", 1 },
                ["source"] = new object[] { "184", 0 },
                ["mask"] = new object[] { "79", 2 }
            });

            workflow["88"] = MakeNode("Hy3DSampleMultiView", new Dictionary<string, object>
            {
                ["view_size"] = 512,
                ["steps"] = 50,
                ["seed"] = 1027,
                ["denoise_strength"] = 1,
                ["pipeline"] = new object[] { "149", 0 },
                ["ref_image"] = new object[] { "35", 0 },
                ["normal_maps"] = new object[] { "64", 0 },
                ["position_maps"] = new object[] { "79", 3 },
                ["camera_config"] = new object[] { "61", 0 }
            });

            // === Stage 11: Upscale & Bake Texture ===
            workflow["117"] = MakeNode("ImageResize+", new Dictionary<string, object>
            {
                ["width"] = 2048,
                ["height"] = 2048,
                ["interpolation"] = "lanczos",
                ["method"] = "stretch",
                ["conditions"] = "always",
                ["multiple_of"] = 0,
                ["image"] = new object[] { "88", 0 }
            });

            workflow["92"] = MakeNode("Hy3DBakeFromMultiview", new Dictionary<string, object>
            {
                ["images"] = new object[] { "117", 0 },
                ["renderer"] = new object[] { "79", 0 },
                ["camera_config"] = new object[] { "61", 0 }
            });

            // === Stage 12: Inpaint & Apply Texture ===
            workflow["129"] = MakeNode("Hy3DMeshVerticeInpaintTexture", new Dictionary<string, object>
            {
                ["texture"] = new object[] { "92", 0 },
                ["mask"] = new object[] { "92", 1 },
                ["renderer"] = new object[] { "79", 0 }
            });

            workflow["104"] = MakeNode("CV2InpaintTexture", new Dictionary<string, object>
            {
                ["inpaint_radius"] = 3,
                ["inpaint_method"] = "ns",
                ["texture"] = new object[] { "129", 0 },
                ["mask"] = new object[] { "129", 1 }
            });

            workflow["98"] = MakeNode("Hy3DApplyTexture", new Dictionary<string, object>
            {
                ["texture"] = new object[] { "104", 0 },
                ["renderer"] = new object[] { "79", 0 }
            });

            // === Stage 13b: Export textured mesh ===
            workflow["99"] = MakeNode("Hy3DExportMesh", new Dictionary<string, object>
            {
                ["filename_prefix"] = "3D/Hy3D_textured",
                ["file_format"] = "glb",
                ["save_file"] = true,
                ["trimesh"] = new object[] { "98", 0 }
            });
        }

        return workflow;
    }

    #endregion

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
