using System;
using System.IO;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace GlimpseAI.Services;

/// <summary>
/// DisplayConduit that draws the AI-generated image as an overlay in the Rhino viewport.
/// Uses DrawSprite with DisplayBitmap for performant, viewport-filling rendering.
/// </summary>
public class GlimpseOverlayConduit : DisplayConduit, IDisposable
{
    private DisplayBitmap _displayBitmap;
    private int _imageWidth;
    private int _imageHeight;
    private bool _disposed;
    private readonly object _lock = new();

    /// <summary>
    /// Opacity of the overlay (0.0 = fully transparent, 1.0 = fully opaque).
    /// </summary>
    public double Opacity { get; set; } = 0.85;

    /// <summary>
    /// Updates the overlay image. Call from any thread — thread-safe.
    /// Uses DisplayBitmap.SetBitmap for performant updates without re-creating the bitmap.
    /// </summary>
    public void UpdateImage(byte[] pngData)
    {
        if (pngData == null || pngData.Length == 0) return;

        lock (_lock)
        {
            try
            {
                using var ms = new MemoryStream(pngData);
                var bitmap = new System.Drawing.Bitmap(ms);

                _imageWidth = bitmap.Width;
                _imageHeight = bitmap.Height;

                _displayBitmap?.Dispose();
                _displayBitmap = new DisplayBitmap(bitmap);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Glimpse AI: Overlay update error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clears the overlay image.
    /// </summary>
    public void ClearImage()
    {
        lock (_lock)
        {
            _displayBitmap?.Dispose();
            _displayBitmap = null;
            _imageWidth = 0;
            _imageHeight = 0;
        }
    }

    /// <summary>
    /// Draws the overlay image over the viewport (HUD-style).
    /// </summary>
    protected override void DrawForeground(DrawEventArgs e)
    {
        lock (_lock)
        {
            if (_displayBitmap == null || _imageWidth == 0 || _imageHeight == 0) return;

            var vp = e.Viewport;
            var vpRect = vp.Bounds;
            int vpWidth = vpRect.Width;
            int vpHeight = vpRect.Height;

            if (vpWidth <= 0 || vpHeight <= 0) return;

            // Calculate the size and position to fill the viewport while maintaining aspect ratio
            double imgAspect = (double)_imageWidth / _imageHeight;
            double vpAspect = (double)vpWidth / vpHeight;

            int drawWidth, drawHeight;
            if (vpAspect > imgAspect)
            {
                // Viewport is wider — fit to width
                drawWidth = vpWidth;
                drawHeight = (int)(vpWidth / imgAspect);
            }
            else
            {
                // Viewport is taller — fit to height
                drawHeight = vpHeight;
                drawWidth = (int)(vpHeight * imgAspect);
            }

            // Center the image in the viewport
            int x = (vpWidth - drawWidth) / 2;
            int y = (vpHeight - drawHeight) / 2;

            // Apply opacity via color alpha
            int alpha = (int)(Opacity * 255);
            alpha = Math.Clamp(alpha, 0, 255);

            // Draw the sprite as a 2D overlay filling the viewport
            e.Display.DrawSprite(
                _displayBitmap,
                new Point2d(vpWidth / 2.0, vpHeight / 2.0),
                drawWidth,
                System.Drawing.Color.FromArgb(alpha, 255, 255, 255));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Enabled = false;

            lock (_lock)
            {
                _displayBitmap?.Dispose();
                _displayBitmap = null;
            }
        }
    }
}
