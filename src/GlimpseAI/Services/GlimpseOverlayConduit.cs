using System;
using System.IO;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace GlimpseAI.Services;

/// <summary>
/// DisplayConduit that draws the AI-generated image as an overlay in the Rhino viewport.
/// Uses DrawSprite with DisplayBitmap for performant, viewport-filling rendering.
/// Thread-safe with double-buffering to prevent crashes during concurrent updates.
/// </summary>
public class GlimpseOverlayConduit : DisplayConduit, IDisposable
{
    private DisplayBitmap _displayBitmap;
    private DisplayBitmap _stagingBitmap; // Double buffer for thread-safe swapping
    private int _imageWidth;
    private int _imageHeight;
    private bool _disposed;
    private readonly object _lock = new();

    // Finalizer to ensure cleanup even if Dispose() isn't called
    ~GlimpseOverlayConduit()
    {
        Dispose(false);
    }

    /// <summary>
    /// Opacity of the overlay (0.0 = fully transparent, 1.0 = fully opaque).
    /// </summary>
    public double Opacity { get; set; } = 0.85;

    /// <summary>
    /// Updates the overlay image. Call from any thread — thread-safe.
    /// Uses double-buffering to prevent crashes during viewport rendering.
    /// </summary>
    public void UpdateImage(byte[] pngData)
    {
        if (pngData == null || pngData.Length == 0) return;

        DisplayBitmap newDisplayBitmap = null;
        DisplayBitmap oldStagingBitmap = null;
        int newWidth = 0, newHeight = 0;

        try
        {
            // Create new bitmap outside the lock to minimize lock time
            using var ms = new MemoryStream(pngData);
            using var bitmap = new System.Drawing.Bitmap(ms);
            
            newWidth = bitmap.Width;
            newHeight = bitmap.Height;
            newDisplayBitmap = new DisplayBitmap(bitmap);

            // Critical section: atomically swap buffers
            lock (_lock)
            {
                if (_disposed) 
                {
                    newDisplayBitmap?.Dispose();
                    return;
                }

                // Store old staging bitmap for disposal outside lock
                oldStagingBitmap = _stagingBitmap;
                
                // Update staging buffer with new bitmap
                _stagingBitmap = newDisplayBitmap;
                _imageWidth = newWidth;
                _imageHeight = newHeight;
                
                // Clear reference so it won't be disposed in the finally block
                newDisplayBitmap = null;
            }
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Glimpse AI: Overlay update error: {ex.Message}");
            newDisplayBitmap?.Dispose();
            return;
        }
        finally
        {
            // Dispose old staging bitmap outside lock to prevent blocking
            oldStagingBitmap?.Dispose();
        }
        
        // Request viewport redraw to show new image
        try
        {
            RhinoDoc.ActiveDoc?.Views.Redraw();
        }
        catch
        {
            // Redraw request is best-effort
        }
    }

    /// <summary>
    /// Clears the overlay image.
    /// </summary>
    public void ClearImage()
    {
        DisplayBitmap oldDisplayBitmap = null;
        DisplayBitmap oldStagingBitmap = null;
        
        lock (_lock)
        {
            oldDisplayBitmap = _displayBitmap;
            oldStagingBitmap = _stagingBitmap;
            
            _displayBitmap = null;
            _stagingBitmap = null;
            _imageWidth = 0;
            _imageHeight = 0;
        }
        
        // Dispose outside lock to prevent deadlock
        try { oldDisplayBitmap?.Dispose(); } catch { }
        try { oldStagingBitmap?.Dispose(); } catch { }
        
        // Request redraw to clear the overlay
        try
        {
            RhinoDoc.ActiveDoc?.Views.Redraw();
        }
        catch
        {
            // Redraw request is best-effort
        }
    }

    /// <summary>
    /// Draws the overlay image over the viewport (HUD-style).
    /// Thread-safe with atomic bitmap swapping.
    /// </summary>
    protected override void DrawForeground(DrawEventArgs e)
    {
        DisplayBitmap currentBitmap = null;
        int currentWidth = 0, currentHeight = 0;
        
        // Critical section: atomically get current display state
        lock (_lock)
        {
            if (_disposed) return;
            
            // Swap staging to display if we have a new image
            if (_stagingBitmap != null)
            {
                var oldDisplayBitmap = _displayBitmap;
                _displayBitmap = _stagingBitmap;
                _stagingBitmap = null;
                
                // Dispose old display bitmap outside lock (in finally block)
                if (oldDisplayBitmap != null && oldDisplayBitmap != _displayBitmap)
                {
                    try { oldDisplayBitmap.Dispose(); } catch { }
                }
            }
            
            // Get current display state
            currentBitmap = _displayBitmap;
            currentWidth = _imageWidth;
            currentHeight = _imageHeight;
        }

        // Render outside the lock to minimize blocking
        if (currentBitmap == null || currentWidth == 0 || currentHeight == 0) 
            return;

        try
        {
            var vp = e.Viewport;
            var vpRect = vp.Bounds;
            int vpWidth = vpRect.Width;
            int vpHeight = vpRect.Height;

            if (vpWidth <= 0 || vpHeight <= 0) return;

            // Calculate the size and position to fill the viewport while maintaining aspect ratio
            double imgAspect = (double)currentWidth / currentHeight;
            double vpAspect = (double)vpWidth / vpHeight;

            int drawWidth, drawHeight;
            if (vpAspect > imgAspect)
            {
                // Viewport is wider — fit to height
                drawHeight = vpHeight;
                drawWidth = (int)(vpHeight * imgAspect);
            }
            else
            {
                // Viewport is taller — fit to width
                drawWidth = vpWidth;
                drawHeight = (int)(vpWidth / imgAspect);
            }

            // Center the image in the viewport
            var centerX = vpWidth / 2.0;
            var centerY = vpHeight / 2.0;

            // Apply opacity via color alpha
            int alpha = (int)(Opacity * 255);
            alpha = Math.Clamp(alpha, 0, 255);

            // Draw the sprite as a 2D overlay filling the viewport
            // DrawSprite uses size as full width/height, not radius
            e.Display.DrawSprite(
                currentBitmap,
                new Point2d(centerX, centerY),
                Math.Max(drawWidth, drawHeight), // Use larger dimension for proper scaling
                System.Drawing.Color.FromArgb(alpha, 255, 255, 255));
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Glimpse AI: Overlay render error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            DisplayBitmap oldDisplayBitmap = null;
            DisplayBitmap oldStagingBitmap = null;
            
            lock (_lock)
            {
                _disposed = true;
                if (disposing)
                {
                    Enabled = false; // Only disable if explicitly disposing
                }
                
                oldDisplayBitmap = _displayBitmap;
                oldStagingBitmap = _stagingBitmap;
                
                _displayBitmap = null;
                _stagingBitmap = null;
            }
            
            // Dispose bitmaps outside lock to prevent potential deadlock
            try { oldDisplayBitmap?.Dispose(); } catch { }
            try { oldStagingBitmap?.Dispose(); } catch { }
        }
    }
}
