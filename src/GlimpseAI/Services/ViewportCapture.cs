using System;
using System.IO;
using Rhino.Display;

namespace GlimpseAI.Services;

/// <summary>
/// Utility for capturing Rhino viewport images as PNG byte arrays.
/// </summary>
public static class ViewportCapture
{
    /// <summary>
    /// Captures the viewport as an RGB PNG image.
    /// </summary>
    /// <param name="viewport">The Rhino viewport to capture.</param>
    /// <param name="width">Capture width in pixels.</param>
    /// <param name="height">Capture height in pixels.</param>
    /// <returns>PNG image as byte array.</returns>
    public static byte[] CaptureViewport(RhinoViewport viewport, int width = 512, int height = 384)
    {
        if (viewport == null)
            throw new ArgumentNullException(nameof(viewport));

        var view = viewport.ParentView;
        if (view == null)
            throw new InvalidOperationException("Viewport has no parent view.");

        var bitmap = view.CaptureToBitmap(new System.Drawing.Size(width, height));
        if (bitmap == null)
            throw new InvalidOperationException("Failed to capture viewport to bitmap.");

        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>
    /// Captures a depth-approximation image by temporarily switching to the Arctic display mode.
    /// Arctic mode produces a clean white-on-white shading that approximates depth/AO.
    /// </summary>
    /// <param name="viewport">The Rhino viewport to capture.</param>
    /// <param name="width">Capture width in pixels.</param>
    /// <param name="height">Capture height in pixels.</param>
    /// <returns>PNG image as byte array, or null if Arctic mode is not available.</returns>
    public static byte[] CaptureDepthApprox(RhinoViewport viewport, int width = 512, int height = 384)
    {
        if (viewport == null)
            throw new ArgumentNullException(nameof(viewport));

        var view = viewport.ParentView;
        if (view == null)
            throw new InvalidOperationException("Viewport has no parent view.");

        // Find the Arctic display mode
        var arcticMode = FindDisplayMode("Arctic");
        if (arcticMode == null)
        {
            // Fallback: try Technical mode
            arcticMode = FindDisplayMode("Technical");
        }

        if (arcticMode == null)
        {
            // No suitable depth-like display mode available
            return null;
        }

        // Remember the current display mode to restore later
        var originalMode = viewport.DisplayMode;

        try
        {
            // Switch to Arctic/Technical mode
            viewport.DisplayMode = arcticMode;
            view.Redraw();

            // Capture in depth-like mode
            var bitmap = view.CaptureToBitmap(new System.Drawing.Size(width, height));
            if (bitmap == null)
                return null;

            try
            {
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
            finally
            {
                bitmap.Dispose();
            }
        }
        finally
        {
            // Restore the original display mode
            viewport.DisplayMode = originalMode;
            view.Redraw();
        }
    }

    /// <summary>
    /// Gets the resolution for a given preset.
    /// </summary>
    public static (int width, int height) GetResolutionForPreset(Models.PresetType preset)
    {
        return preset switch
        {
            Models.PresetType.Fast => (512, 384),
            Models.PresetType.Balanced => (768, 576),
            Models.PresetType.HighQuality => (1024, 768),
            Models.PresetType.Export4K => (1024, 768), // Upscaled to 4K by ComfyUI
            _ => (512, 384)
        };
    }

    /// <summary>
    /// Finds a display mode by name.
    /// </summary>
    private static DisplayModeDescription FindDisplayMode(string name)
    {
        var modes = DisplayModeDescription.GetDisplayModes();
        foreach (var mode in modes)
        {
            if (string.Equals(mode.EnglishName, name, StringComparison.OrdinalIgnoreCase))
                return mode;
        }
        return null;
    }
}
