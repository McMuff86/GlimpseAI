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
    /// Captures the viewport as an RGB PNG image with comprehensive error handling.
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

        // Validate capture dimensions
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Capture dimensions must be positive.");

        if (width > 4096 || height > 4096)
            throw new ArgumentException("Capture dimensions too large (max 4096x4096).");

        System.Drawing.Bitmap bitmap = null;
        try
        {
            bitmap = view.CaptureToBitmap(new System.Drawing.Size(width, height));
            if (bitmap == null)
                throw new InvalidOperationException("Failed to capture viewport to bitmap.");

            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"Glimpse AI: Viewport capture error: {ex.Message}");
            throw;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    /// <summary>
    /// Captures a depth-approximation image by temporarily switching to the Arctic display mode.
    /// Arctic mode produces a clean white-on-white shading that approximates depth/AO.
    /// Uses safe display mode switching to prevent race conditions during active rendering.
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
        bool displayModeChanged = false;

        try
        {
            // Only change display mode if it's actually different
            if (originalMode?.Id != arcticMode.Id)
            {
                // Switch to Arctic/Technical mode with safety checks
                viewport.DisplayMode = arcticMode;
                displayModeChanged = true;
                
                // Force complete redraw and wait for it to finish
                view.Redraw();
                
                // Small delay to ensure display mode transition is complete
                // This prevents capturing mid-transition artifacts
                System.Threading.Thread.Sleep(50);
            }

            // Capture in depth-like mode with error handling
            System.Drawing.Bitmap bitmap = null;
            try
            {
                bitmap = view.CaptureToBitmap(new System.Drawing.Size(width, height));
                if (bitmap == null)
                {
                    return null;
                }

                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine($"Glimpse AI: Depth capture error: {ex.Message}");
                return null;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"Glimpse AI: Display mode switch error: {ex.Message}");
            return null;
        }
        finally
        {
            // Always restore the original display mode if we changed it
            if (displayModeChanged && originalMode != null)
            {
                try
                {
                    viewport.DisplayMode = originalMode;
                    view.Redraw();
                }
                catch (Exception ex)
                {
                    Rhino.RhinoApp.WriteLine($"Glimpse AI: Display mode restore error: {ex.Message}");
                }
            }
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
            Models.PresetType.Balanced => (1024, 768),
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
