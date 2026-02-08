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

    public GlimpseOrchestrator(string comfyUrl, int debounceMs = 300)
    {
        _comfyClient = new ComfyUIClient(comfyUrl);
        _watcher = new ViewportWatcher(debounceMs);
        _watcher.ViewportChanged += OnViewportChanged;
    }

    /// <summary>
    /// Called by the Generate button. Starts a one-shot generation on a background thread.
    /// Cancels any previously running generation.
    /// </summary>
    public void RequestGenerate(string prompt, PresetType preset, double denoise, int seed = -1)
    {
        CancelCurrentGeneration();
        _currentGenerationCts = new CancellationTokenSource();
        var ct = _currentGenerationCts.Token;

        Task.Run(() => GenerateAsync(prompt, preset, denoise, seed, ct));
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
    /// Core generation pipeline: capture → upload → generate → return result.
    /// Runs entirely on a background thread; raises events for UI updates.
    /// </summary>
    private async Task GenerateAsync(string prompt, PresetType preset, double denoise, int seed, CancellationToken ct)
    {
        try
        {
            BusyChanged?.Invoke(this, true);
            StatusChanged?.Invoke(this, "Capturing viewport…");

            // 1. Capture viewport image (must run on UI thread for Rhino API)
            byte[] viewportImage = null;
            byte[] depthImage = null;
            var resolution = ViewportCapture.GetResolutionForPreset(preset);

            // RhinoApp.InvokeOnUiThread is synchronous from caller's perspective
            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try
                {
                    var viewport = RhinoDoc.ActiveDoc?.Views?.ActiveView?.ActiveViewport;
                    if (viewport == null) return;

                    viewportImage = ViewportCapture.CaptureViewport(viewport, resolution.width, resolution.height);
                    depthImage = ViewportCapture.CaptureDepthApprox(viewport, resolution.width, resolution.height);
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"Glimpse AI: Capture error: {ex.Message}");
                }
            }));

            ct.ThrowIfCancellationRequested();

            if (viewportImage == null || viewportImage.Length == 0)
            {
                RenderCompleted?.Invoke(this,
                    RenderResult.Fail("Failed to capture viewport.", TimeSpan.Zero));
                return;
            }

            StatusChanged?.Invoke(this, "Generating…");

            // 2. Build render request
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

            // 3. Send to ComfyUI and wait for result
            var result = await _comfyClient.GenerateAsync(request, ct);

            // 4. Notify UI
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
    /// Fired by the ViewportWatcher after debounce when the camera moves.
    /// Cancels any in-flight generation and starts a new one.
    /// </summary>
    private void OnViewportChanged(object sender, ViewportChangedEventArgs e)
    {
        if (_disposed) return;

        CancelCurrentGeneration();
        _currentGenerationCts = new CancellationTokenSource();
        var ct = _currentGenerationCts.Token;

        Task.Run(() => GenerateAsync(_autoPrompt, _autoPreset, _autoDenoise, _autoSeed, ct));
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
            _comfyClient?.Dispose();
        }
    }
}
