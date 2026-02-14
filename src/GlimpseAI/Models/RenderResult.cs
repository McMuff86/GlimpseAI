using System;

namespace GlimpseAI.Models;

/// <summary>
/// DTO for an AI rendering result.
/// </summary>
public class RenderResult
{
    /// <summary>Whether the rendering completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>The rendered output image as PNG bytes. Null on failure.</summary>
    public byte[] ImageData { get; set; }

    /// <summary>Output filename on the ComfyUI server.</summary>
    public string OutputFilename { get; set; }

    /// <summary>Error message if rendering failed.</summary>
    public string ErrorMessage { get; set; }

    /// <summary>Total wall-clock time for the generation.</summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>The preset used for this render.</summary>
    public PresetType Preset { get; set; }

    /// <summary>The seed that was actually used (resolved from -1 → random).</summary>
    public int Seed { get; set; }

    /// <summary>The checkpoint model name used for this render.</summary>
    public string CheckpointName { get; set; }

    /// <summary>Create a success result.</summary>
    public static RenderResult Ok(byte[] imageData, string filename, TimeSpan elapsed, PresetType preset, int seed, string checkpointName = null)
    {
        return new RenderResult
        {
            Success = true,
            ImageData = imageData,
            OutputFilename = filename,
            Elapsed = elapsed,
            Preset = preset,
            Seed = seed,
            CheckpointName = checkpointName
        };
    }

    /// <summary>Create a failure result.</summary>
    public static RenderResult Fail(string error, TimeSpan elapsed)
    {
        return new RenderResult
        {
            Success = false,
            ErrorMessage = error,
            Elapsed = elapsed
        };
    }
}
