using System;
using System.Threading;
using System.Threading.Tasks;
using GlimpseAI.Models;
using Rhino;

namespace GlimpseAI.Services;

/// <summary>
/// Orchestrates the render pipeline: UI events → Viewport Capture → ComfyUI → Result.
/// This is the bridge between the GlimpsePanel (UI) and the backend services.
/// All long-running work runs on background threads; UI updates are marshalled via events.
/// </summary>
public class GlimpseOrchestrator : IDisposable
{
    private readonly ComfyUIClient _comfyClient;
    private readonly ViewportWatcher _watcher;
    private readonly GlimpseOverlayConduit _overlayConduit;
    private CancellationTokenSource _currentGenerationCts;
    private bool _disposed;

    // Auto-mode settings (updated from UI)
    private string _autoPrompt;
    private PresetType _autoPreset;
    private double _autoDenoise;
    private int _autoSeed;

    /// <summary>Raised when a render completes (success or failure).</summary>
    public event EventHandler<RenderResult> RenderCompleted;

    /// <summary>Raised when the status text should be updated in the UI.</summary>
    public event EventHandler<string> StatusChanged;

    /// <summary>Raised when the busy state changes (to enable/disable controls).</summary>
    public event EventHandler<bool> BusyChanged;

    /// <summary>Raised when ComfyUI reports sampling progress.</summary>
    public event EventHandler<ProgressEventArgs> ProgressChanged;

    /// <summary>Raised when a latent preview image is received during generation.</summary>
    public event EventHandler<PreviewImageEventArgs> PreviewImageReceived;

    /// <summary>The overlay conduit for viewport rendering.</summary>
    public GlimpseOverlayConduit OverlayConduit => _overlayConduit;

    public GlimpseOrchestrator(string comfyUrl, int debounceMs = 300)
    {
        _comfyClient = new ComfyUIClient(comfyUrl);
        _watcher = new ViewportWatcher(debounceMs);
        _watcher.ViewportChanged += OnViewportChanged;
        _overlayConduit = new GlimpseOverlayConduit();

        // Wire ComfyUI client events to orchestrator events
        _comfyClient.ProgressChanged += (s, e) => ProgressChanged?.Invoke(this, e);
        _comfyClient.PreviewImageReceived += OnPreviewImageFromComfy;

        // Connect WebSocket on a background thread
        Task.Run(ConnectWebSocketAsync);
    }

    /// <summary>
    /// Connects/reconnects the WebSocket to ComfyUI.
    /// </summary>
    private async Task ConnectWebSocketAsync()
    {
        try
        {
            await _comfyClient.ConnectWebSocketAsync();
            RhinoApp.WriteLine("Glimpse AI: WebSocket connected.");
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Glimpse AI: WebSocket connection failed ({ex.Message}), using HTTP polling.");
        }
    }

    /// <summary>
    /// Called by the Generate button (on the UI thread).
    /// Captures the viewport immediately, then runs generation on a background thread.
    /// </summary>
    public void RequestGenerate(string prompt, PresetType preset, double denoise, int seed = -1)
    {
        // Capture viewport on the current UI thread
        var capture = CaptureCurrentViewport(preset);
        if (capture.viewportImage == null)
        {
            RenderCompleted?.Invoke(this,
                RenderResult.Fail("Failed to capture viewport. Make sure a 3D view is active.", TimeSpan.Zero));
            return;
        }

        CancelCurrentGeneration();
        _currentGenerationCts = new CancellationTokenSource();
        var ct = _currentGenerationCts.Token;

        Task.Run(() => GenerateFromCaptureAsync(
            capture.viewportImage, capture.depthImage,
            prompt, preset, denoise, seed, ct));
    }

    /// <summary>
    /// Starts auto-mode: the ViewportWatcher listens for camera changes and triggers generation.
    /// </summary>
    public void StartAutoMode(string prompt, PresetType preset, double denoise, int seed = -1)
    {
        _autoPrompt = prompt;
        _autoPreset = preset;
        _autoDenoise = denoise;
        _autoSeed = seed;
        _watcher.Reset();
        _watcher.IsEnabled = true;

        StatusChanged?.Invoke(this, "Auto mode active – move the camera");
    }

    /// <summary>
    /// Stops auto-mode.
    /// </summary>
    public void StopAutoMode()
    {
        _watcher.IsEnabled = false;
        CancelCurrentGeneration();
        StatusChanged?.Invoke(this, "Auto mode stopped");
    }

    /// <summary>
    /// Updates the generation settings used in auto-mode without restarting the watcher.
    /// </summary>
    public void UpdateAutoSettings(string prompt, PresetType preset, double denoise, int seed = -1)
    {
        _autoPrompt = prompt;
        _autoPreset = preset;
        _autoDenoise = denoise;
        _autoSeed = seed;
    }

    /// <summary>
    /// Checks whether the ComfyUI server is reachable.
    /// </summary>
    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            return await _comfyClient.IsAvailableAsync();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enables or disables the viewport overlay conduit.
    /// </summary>
    public void SetOverlayEnabled(bool enabled)
    {
        _overlayConduit.Enabled = enabled;
        RhinoDoc.ActiveDoc?.Views.Redraw();
    }

    /// <summary>
    /// Sets the overlay opacity (0.0–1.0).
    /// </summary>
    public void SetOverlayOpacity(double opacity)
    {
        _overlayConduit.Opacity = Math.Clamp(opacity, 0.0, 1.0);
        RhinoDoc.ActiveDoc?.Views.Redraw();
    }

    /// <summary>
    /// Captures the current viewport on the calling thread (must be UI thread).
    /// </summary>
    private (byte[] viewportImage, byte[] depthImage) CaptureCurrentViewport(PresetType preset)
    {
        try
        {
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                RhinoApp.WriteLine("Glimpse AI: No active document.");
                return (null, null);
            }

            var view = doc.Views.ActiveView;
            if (view == null)
            {
                RhinoApp.WriteLine("Glimpse AI: No active view.");
                return (null, null);
            }

            var viewport = view.ActiveViewport;
            if (viewport == null)
            {
                RhinoApp.WriteLine("Glimpse AI: No active viewport.");
                return (null, null);
            }

            var resolution = ViewportCapture.GetResolutionForPreset(preset);
            RhinoApp.WriteLine($"Glimpse AI: Capturing viewport at {resolution.width}x{resolution.height}...");

            var viewportImage = ViewportCapture.CaptureViewport(viewport, resolution.width, resolution.height);
            byte[] depthImage = null;
            try
            {
                depthImage = ViewportCapture.CaptureDepthApprox(viewport, resolution.width, resolution.height);
            }
            catch
            {
                // Depth capture is optional
            }

            RhinoApp.WriteLine($"Glimpse AI: Viewport captured ({viewportImage?.Length ?? 0} bytes).");
            return (viewportImage, depthImage);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Glimpse AI: Capture error: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// Core generation pipeline with pre-captured images.
    /// Runs entirely on a background thread; raises events for UI updates.
    /// </summary>
    private async Task GenerateFromCaptureAsync(
        byte[] viewportImage, byte[] depthImage,
        string prompt, PresetType preset, double denoise, int seed,
        CancellationToken ct)
    {
        try
        {
            BusyChanged?.Invoke(this, true);
            StatusChanged?.Invoke(this, "Sending to ComfyUI…");

            // Ensure WebSocket is connected (reconnect if needed)
            if (!_comfyClient.IsWebSocketConnected)
            {
                try
                {
                    await _comfyClient.ConnectWebSocketAsync(ct);
                }
                catch
                {
                    // Will fall back to HTTP polling
                }
            }

            var settings = GlimpseAIPlugin.Instance?.GlimpseSettings ?? new GlimpseSettings();
            var request = new RenderRequest
            {
                ViewportImage = viewportImage,
                DepthImage = depthImage,
                Prompt = prompt,
                NegativePrompt = settings.DefaultNegativePrompt,
                Preset = preset,
                DenoiseStrength = denoise,
                Seed = seed
            };

            var result = await _comfyClient.GenerateAsync(request, ct);

            // Update the overlay conduit with the final image
            if (result.Success && result.ImageData != null)
            {
                _overlayConduit.UpdateImage(result.ImageData);
                RhinoDoc.ActiveDoc?.Views.Redraw();
            }

            RenderCompleted?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke(this, "Cancelled");
        }
        catch (Exception ex)
        {
            RenderCompleted?.Invoke(this,
                RenderResult.Fail($"Unexpected error: {ex.Message}", TimeSpan.Zero));
        }
        finally
        {
            BusyChanged?.Invoke(this, false);
        }
    }

    /// <summary>
    /// Handles latent preview images from ComfyUI and forwards to overlay + event.
    /// </summary>
    private void OnPreviewImageFromComfy(object sender, PreviewImageEventArgs e)
    {
        // Update overlay with preview image during generation
        if (_overlayConduit.Enabled)
        {
            _overlayConduit.UpdateImage(e.ImageData);
            RhinoDoc.ActiveDoc?.Views.Redraw();
        }

        PreviewImageReceived?.Invoke(this, e);
    }

    /// <summary>
    /// Fired by the ViewportWatcher after debounce when the camera moves.
    /// Captures viewport on UI thread, then runs generation on background.
    /// </summary>
    private void OnViewportChanged(object sender, ViewportChangedEventArgs e)
    {
        if (_disposed) return;

        CancelCurrentGeneration();
        _currentGenerationCts = new CancellationTokenSource();
        var ct = _currentGenerationCts.Token;

        Task.Run(async () =>
        {
            byte[] viewportImage = null;
            byte[] depthImage = null;

            var tcs = new TaskCompletionSource<bool>();
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try
                {
                    var capture = CaptureCurrentViewport(_autoPreset);
                    viewportImage = capture.viewportImage;
                    depthImage = capture.depthImage;
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            }));

            await tcs.Task;

            if (viewportImage == null || viewportImage.Length == 0) return;

            await GenerateFromCaptureAsync(
                viewportImage, depthImage,
                _autoPrompt, _autoPreset, _autoDenoise, _autoSeed, ct);
        });
    }

    /// <summary>
    /// Cancels any in-flight generation request.
    /// </summary>
    private void CancelCurrentGeneration()
    {
        try
        {
            _currentGenerationCts?.Cancel();
            _currentGenerationCts?.Dispose();
        }
        catch
        {
            // ignore
        }
        _currentGenerationCts = null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            CancelCurrentGeneration();
            _watcher?.Dispose();
            _overlayConduit?.Dispose();
            try { _comfyClient.DisconnectWebSocketAsync().GetAwaiter().GetResult(); } catch { }
            _comfyClient?.Dispose();
        }
    }
}
