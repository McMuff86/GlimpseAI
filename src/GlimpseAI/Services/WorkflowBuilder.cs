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
    /// <param name="checkpointName">Checkpoint model filename (auto-detected).</param>
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
        bool useWebSocketOutput = false)
    {
        return preset switch
        {
            PresetType.Fast => BuildFastWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName, useWebSocketOutput),
            PresetType.Balanced => BuildBalancedWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName, useWebSocketOutput),
            PresetType.HighQuality => BuildHQWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName, useWebSocketOutput),
            PresetType.Export4K => BuildExport4KWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName, useWebSocketOutput),
            _ => BuildFastWorkflow(viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName, useWebSocketOutput)
        };
    }

    /// <summary>
    /// Fast Preview: ~1-2s, 8 steps, 512x384.
    /// </summary>
    private static Dictionary<string, object> BuildFastWorkflow(
        string viewportImageName, string prompt, string negativePrompt,
        double denoise, int seed, string checkpointName, bool useWebSocketOutput)
    {
        return BuildImg2ImgWorkflow(
            viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName,
            steps: 8, cfg: 2.0, samplerName: "dpmpp_sde", scheduler: "karras",
            filenamePrefix: "GlimpseAI/fast", useWebSocketOutput: useWebSocketOutput);
    }

    /// <summary>
    /// Balanced: ~5-8s, 12 steps, 1024x768.
    /// </summary>
    private static Dictionary<string, object> BuildBalancedWorkflow(
        string viewportImageName, string prompt, string negativePrompt,
        double denoise, int seed, string checkpointName, bool useWebSocketOutput)
    {
        return BuildImg2ImgWorkflow(
            viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName,
            steps: 12, cfg: 4.0, samplerName: "dpmpp_2m", scheduler: "karras",
            filenamePrefix: "GlimpseAI/balanced", useWebSocketOutput: useWebSocketOutput);
    }

    /// <summary>
    /// High Quality: ~20-30s, 30 steps, 1024x768.
    /// </summary>
    private static Dictionary<string, object> BuildHQWorkflow(
        string viewportImageName, string prompt, string negativePrompt,
        double denoise, int seed, string checkpointName, bool useWebSocketOutput)
    {
        return BuildImg2ImgWorkflow(
            viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName,
            steps: 30, cfg: 7.5, samplerName: "dpmpp_2m", scheduler: "karras",
            filenamePrefix: "GlimpseAI/hq", useWebSocketOutput: useWebSocketOutput);
    }

    /// <summary>
    /// Export 4K: Like HQ + UltraSharp 4x upscale → 4096x3072 output.
    /// Always uses SaveImage (no WebSocket output for large files).
    /// </summary>
    private static Dictionary<string, object> BuildExport4KWorkflow(
        string viewportImageName, string prompt, string negativePrompt,
        double denoise, int seed, string checkpointName, bool useWebSocketOutput)
    {
        // 4K export always saves to disk — too large for WebSocket streaming
        var workflow = BuildImg2ImgWorkflow(
            viewportImageName, prompt, negativePrompt, denoise, seed, checkpointName,
            steps: 30, cfg: 7.5, samplerName: "dpmpp_2m", scheduler: "karras",
            filenamePrefix: "GlimpseAI/export4k", useWebSocketOutput: false);

        // Add upscale nodes — save the upscaled output instead
        workflow["9"] = MakeNode("UpscaleModelLoader", new Dictionary<string, object>
        {
            ["model_name"] = "4x-UltraSharp.pth"
        });

        workflow["10"] = MakeNode("ImageUpscaleWithModel", new Dictionary<string, object>(),
            new Dictionary<string, object>
            {
                ["upscale_model"] = new object[] { "9", 0 },
                ["image"] = new object[] { "7", 0 }
            });

        // Redirect SaveImage to use upscaled output
        workflow["8"] = MakeNode("SaveImage", new Dictionary<string, object>
        {
            ["filename_prefix"] = "GlimpseAI/export4k"
        }, new Dictionary<string, object>
        {
            ["images"] = new object[] { "10", 0 }
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
