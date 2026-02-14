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
    private bool _isFluxAvailable;
    private bool _isKontextAvailable;
    private bool _isHunyuan3DAvailable;
    private bool _useFlux;
    public bool IsUsingFlux => _useFlux;
    public bool IsKontextAvailable => _isKontextAvailable;
    public bool IsHunyuan3DAvailable => _isHunyuan3DAvailable;

    /// <summary>Available checkpoint models (SDXL/SD1.5) from ComfyUI.</summary>
    public System.Collections.Generic.List<string> AvailableCheckpoints { get; private set; } = new();

    /// <summary>Available Flux UNet models from ComfyUI.</summary>
    public System.Collections.Generic.List<string> AvailableFluxUnets { get; private set; } = new();

    /// <summary>Raised when available model lists have been refreshed.</summary>
    public event EventHandler ModelsRefreshed;

    // Auto-mode settings (updated from UI)
    private string _autoPrompt;
    private PresetType _autoPreset;
    private double _autoDenoise;
    private double _autoCfgScale;
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

        // Connect WebSocket and detect models on a background thread with error handling
        _ = Task.Run(async () =>
        {
            try
            {
                await ConnectWebSocketAsync();
                await DetectAvailableModelsAsync();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Glimpse AI: Initial WebSocket connection failed: {ex.Message}");
            }
        });
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
    /// Detects available models (Flux vs SDXL) and sets the pipeline mode.
    /// </summary>
    public async Task DetectAvailableModelsAsync()
    {
        try
        {
            // Fetch all available models
            AvailableCheckpoints = await _comfyClient.GetAvailableCheckpointsAsync();
            AvailableFluxUnets = await _comfyClient.GetAvailableFluxUNetsAsync();
            RhinoApp.WriteLine($"Glimpse AI: Found {AvailableCheckpoints.Count} checkpoints, {AvailableFluxUnets.Count} Flux UNets");
            ModelsRefreshed?.Invoke(this, EventArgs.Empty);

            _isFluxAvailable = await _comfyClient.IsFluxAvailableAsync();
            
            // Detect Hunyuan3D availability
            _isHunyuan3DAvailable = await _comfyClient.CheckHunyuan3DAvailableAsync();
            _comfyClient.IsHunyuan3DAvailable = _isHunyuan3DAvailable;
            RhinoApp.WriteLine($"Glimpse AI: Hunyuan3D available: {_isHunyuan3DAvailable}");

            // Detect Kontext model availability
            _isKontextAvailable = await _comfyClient.CheckKontextAvailableAsync();
            _comfyClient.IsKontextAvailable = _isKontextAvailable;
            RhinoApp.WriteLine($"Glimpse AI: Kontext model available: {_isKontextAvailable}");
            var settings = GlimpseAIPlugin.Instance?.GlimpseSettings ?? new GlimpseSettings();
            var preferred = settings.PreferredPipeline ?? "auto";

            // Handle explicit pipeline selection
            if (preferred == "sdxl")
            {
                RhinoApp.WriteLine("Glimpse AI: Pipeline forced to SDXL by user preference");
                _useFlux = false;
                return;
            }

            bool shouldUseFlux;
            if (preferred == "flux")
            {
                if (!_isFluxAvailable)
                {
                    RhinoApp.WriteLine("Glimpse AI: ERROR - Flux pipeline selected but Flux models not available! Falling back to SDXL.");
                    _useFlux = false;
                    return;
                }
                RhinoApp.WriteLine("Glimpse AI: Pipeline forced to Flux by user preference");
                shouldUseFlux = true;
            }
            else
            {
                // Auto mode: prefer Flux if available and PreferFlux is true
                shouldUseFlux = _isFluxAvailable && settings.PreferFlux;
            }

            if (shouldUseFlux)
            {
                RhinoApp.WriteLine("Glimpse AI: Using Flux pipeline");
                _useFlux = true;
                
                // Auto-detect and save Flux models if not already configured
                if (string.IsNullOrEmpty(settings.FluxUNetModel))
                {
                    var detectedUnet = await _comfyClient.GetFluxUNetAsync();
                    if (!string.IsNullOrEmpty(detectedUnet))
                    {
                        settings.FluxUNetModel = detectedUnet;
                        RhinoApp.WriteLine($"Glimpse AI: Auto-detected Flux UNet: {detectedUnet}");
                    }
                }

                // Always re-detect Flux ControlNet (model paths may change between sessions)
                {
                    var detectedControlNet = await _comfyClient.GetFluxControlNetAsync();
                    if (!string.IsNullOrEmpty(detectedControlNet))
                    {
                        settings.FluxControlNetModel = detectedControlNet;
                        RhinoApp.WriteLine($"Glimpse AI: Auto-detected Flux ControlNet: {detectedControlNet}");
                    }
                    else
                    {
                        settings.FluxControlNetModel = "";
                        RhinoApp.WriteLine("Glimpse AI: No Flux ControlNet found");
                    }
                }

                // Save updated settings
                GlimpseAIPlugin.Instance?.SaveGlimpseSettings();
            }
            else
            {
                RhinoApp.WriteLine("Glimpse AI: Using SDXL pipeline");
                _useFlux = false;
            }
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Glimpse AI: Model detection failed: {ex.Message}");
            _useFlux = false; // Fallback to SDXL
        }
    }

    /// <summary>
    /// Called by the Generate button (on the UI thread).
    /// Captures the viewport immediately, then runs generation on a background thread.
    /// Supports auto-prompt generation based on the current settings.
    /// </summary>
    public void RequestGenerate(string prompt, PresetType preset, double denoise, double cfgScale, int seed = -1)
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
            prompt, preset, denoise, cfgScale, seed, ct));
    }

    /// <summary>
    /// Generates a monochrome architectural model from an input image using Flux Kontext.
    /// If no input image is provided, captures the current viewport.
    /// </summary>
    public void RequestMonochromeGeneration(byte[] inputImage, string prompt = null)
    {
        if (!_isKontextAvailable)
        {
            RenderCompleted?.Invoke(this,
                RenderResult.Fail("Kontext model (flux1-dev-kontext_fp8_scaled.safetensors) not found. Install it in ComfyUI/models/unet/.", TimeSpan.Zero));
            return;
        }

        // If no input image, capture viewport
        if (inputImage == null || inputImage.Length == 0)
        {
            var capture = CaptureCurrentViewport(PresetType.HighQuality);
            inputImage = capture.viewportImage;
            if (inputImage == null)
            {
                RenderCompleted?.Invoke(this,
                    RenderResult.Fail("No preview image available and failed to capture viewport.", TimeSpan.Zero));
                return;
            }
        }

        CancelCurrentGeneration();
        _currentGenerationCts = new CancellationTokenSource();
        var ct = _currentGenerationCts.Token;
        var imageToProcess = inputImage;
        var monoPrompt = prompt;

        Task.Run(async () =>
        {
            try
            {
                BusyChanged?.Invoke(this, true);
                StatusChanged?.Invoke(this, "Generating monochrome model…");

                // Ensure WebSocket is connected
                if (!_comfyClient.IsWebSocketConnected)
                {
                    try { await _comfyClient.ConnectWebSocketAsync(ct); } catch { }
                }

                var seed = Random.Shared.NextInt64();
                var result = await _comfyClient.GenerateMonochromeAsync(
                    imageToProcess, monoPrompt, seed, ct);

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
                    RenderResult.Fail($"Monochrome generation error: {ex.Message}", TimeSpan.Zero));
            }
            finally
            {
                BusyChanged?.Invoke(this, false);
            }
        }, ct);
    }

    /// <summary>
    /// Generates a 3D mesh from an input image using Hunyuan3D v2.
    /// If no input image is provided, captures the current viewport.
    /// After generation, auto-imports the GLB into Rhino.
    /// </summary>
    public void RequestMeshGeneration(byte[] inputImage)
    {
        if (!_isHunyuan3DAvailable)
        {
            RenderCompleted?.Invoke(this,
                RenderResult.Fail("Hunyuan3D nodes not found. Install ComfyUI-Hunyuan3D custom nodes.", TimeSpan.Zero));
            return;
        }

        // If no input image, capture viewport
        if (inputImage == null || inputImage.Length == 0)
        {
            var capture = CaptureCurrentViewport(PresetType.HighQuality);
            inputImage = capture.viewportImage;
            if (inputImage == null)
            {
                RenderCompleted?.Invoke(this,
                    RenderResult.Fail("No preview image available and failed to capture viewport.", TimeSpan.Zero));
                return;
            }
        }

        CancelCurrentGeneration();
        _currentGenerationCts = new CancellationTokenSource();
        var ct = _currentGenerationCts.Token;
        var imageToProcess = inputImage;

        Task.Run(async () =>
        {
            try
            {
                BusyChanged?.Invoke(this, true);
                StatusChanged?.Invoke(this, "Generating 3D mesh… (this may take several minutes)");

                // Ensure WebSocket is connected
                if (!_comfyClient.IsWebSocketConnected)
                {
                    try { await _comfyClient.ConnectWebSocketAsync(ct); } catch { }
                }

                var seed = Random.Shared.NextInt64();
                var result = await _comfyClient.GenerateMeshAsync(imageToProcess, seed, ct);

                if (result.Success)
                {
                    var settings = GlimpseAIPlugin.Instance?.GlimpseSettings ?? new GlimpseSettings();
                    var glbFilename = result.TexturedGlbFilename ?? result.UntexturedGlbFilename;

                    if (!string.IsNullOrEmpty(glbFilename))
                    {
                        // Construct full Windows path for Rhino import
                        var glbPath = System.IO.Path.Combine(
                            settings.ComfyUIOutputPath,
                            result.Subfolder ?? "3D",
                            glbFilename);

                        RhinoApp.WriteLine($"Glimpse AI: Mesh generated: {glbPath}");
                        RhinoApp.WriteLine($"Glimpse AI: Mesh generation completed in {result.Elapsed.TotalSeconds:F1}s");

                        // Auto-import GLB into Rhino on UI thread
                        RhinoApp.InvokeOnUiThread(new Action(() =>
                        {
                            try
                            {
                                RhinoApp.RunScript($"_-Import \"{glbPath}\" _Enter", false);
                                RhinoApp.WriteLine($"Glimpse AI: GLB imported into Rhino: {glbFilename}");
                            }
                            catch (Exception ex)
                            {
                                RhinoApp.WriteLine($"Glimpse AI: Failed to import GLB: {ex.Message}");
                            }
                        }));

                        StatusChanged?.Invoke(this, $"Mesh generated in {result.Elapsed.TotalSeconds:F0}s — imported into Rhino");
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, $"Mesh generated in {result.Elapsed.TotalSeconds:F0}s (no output file found)");
                    }

                    // Signal completion (no image data for mesh, but report success)
                    RenderCompleted?.Invoke(this,
                        RenderResult.Ok(null, glbFilename ?? "mesh", result.Elapsed, PresetType.HighQuality, 0, "Hunyuan3D"));
                }
                else
                {
                    RhinoApp.WriteLine($"Glimpse AI: Mesh generation failed: {result.ErrorMessage}");
                    RenderCompleted?.Invoke(this,
                        RenderResult.Fail(result.ErrorMessage, result.Elapsed));
                }
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "Mesh generation cancelled");
            }
            catch (Exception ex)
            {
                RenderCompleted?.Invoke(this,
                    RenderResult.Fail($"Mesh generation error: {ex.Message}", TimeSpan.Zero));
            }
            finally
            {
                BusyChanged?.Invoke(this, false);
            }
        }, ct);
    }

    /// <summary>
    /// Generates an auto-prompt based on current settings and scene analysis.
    /// Returns the generated prompt or the manual prompt if auto-prompt is disabled.
    /// </summary>
    public async Task<string> GenerateAutoPromptAsync(string manualPrompt, byte[] viewportImage)
    {
        var settings = GlimpseAIPlugin.Instance?.GlimpseSettings ?? new GlimpseSettings();
        
        switch (settings.PromptMode)
        {
            case PromptMode.Manual:
                return manualPrompt;
                
            case PromptMode.AutoBasic:
                try
                {
                    var doc = RhinoDoc.ActiveDoc;
                    var generatedPrompt = AutoPromptBuilder.BuildFromScene(
                        doc, 
                        settings.StylePreset, 
                        settings.CustomStyleSuffix);
                    
                    RhinoApp.WriteLine($"Glimpse AI: Auto-prompt (Basic): {generatedPrompt}");
                    return generatedPrompt;
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"Glimpse AI: Auto-prompt (Basic) failed: {ex.Message}");
                    return manualPrompt; // Fallback to manual
                }
                
            case PromptMode.AutoVision:
                try
                {
                    // Check if Florence2 is available
                    var florenceAvailable = await _comfyClient.IsFlorence2AvailableAsync();
                    if (!florenceAvailable)
                    {
                        RhinoApp.WriteLine("Glimpse AI: Florence2 not available, falling back to AutoBasic");
                        var doc = RhinoDoc.ActiveDoc;
                        return AutoPromptBuilder.BuildFromScene(
                            doc, 
                            settings.StylePreset, 
                            settings.CustomStyleSuffix);
                    }
                    
                    // Upload viewport image for Florence2 analysis
                    var filename = await _comfyClient.UploadImageAsync(
                        viewportImage, 
                        $"glimpse_autoprompt_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    
                    // Get Florence2 caption
                    var caption = await _comfyClient.GetFlorence2CaptionAsync(filename, CancellationToken.None);
                    
                    if (string.IsNullOrEmpty(caption))
                    {
                        RhinoApp.WriteLine("Glimpse AI: Florence2 caption failed, falling back to AutoBasic");
                        var doc = RhinoDoc.ActiveDoc;
                        return AutoPromptBuilder.BuildFromScene(
                            doc, 
                            settings.StylePreset, 
                            settings.CustomStyleSuffix);
                    }
                    
                    // Combine caption with style preset
                    var generatedPrompt = AutoPromptBuilder.CombineVisionCaption(
                        caption, 
                        settings.StylePreset, 
                        settings.CustomStyleSuffix);
                    
                    RhinoApp.WriteLine($"Glimpse AI: Auto-prompt (Vision): {generatedPrompt}");
                    return generatedPrompt;
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"Glimpse AI: Auto-prompt (Vision) failed: {ex.Message}");
                    return manualPrompt; // Fallback to manual
                }
                
            default:
                return manualPrompt;
        }
    }

    /// <summary>
    /// Starts auto-mode: the ViewportWatcher listens for camera changes and triggers generation.
    /// </summary>
    public void StartAutoMode(string prompt, PresetType preset, double denoise, double cfgScale, int seed = -1)
    {
        _autoPrompt = prompt;
        _autoPreset = preset;
        _autoDenoise = denoise;
        _autoCfgScale = cfgScale;
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
    public void UpdateAutoSettings(string prompt, PresetType preset, double denoise, double cfgScale, int seed = -1)
    {
        _autoPrompt = prompt;
        _autoPreset = preset;
        _autoDenoise = denoise;
        _autoCfgScale = cfgScale;
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
    /// Generates auto-prompt if enabled, otherwise uses the provided prompt.
    /// </summary>
    private async Task GenerateFromCaptureAsync(
        byte[] viewportImage, byte[] depthImage,
        string prompt, PresetType preset, double denoise, double cfgScale, int seed,
        CancellationToken ct)
    {
        var startMemory = GC.GetTotalMemory(false);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            BusyChanged?.Invoke(this, true);
            RhinoApp.WriteLine("Glimpse AI: Starting generation...");
            if (_useFlux)
            {
                var fluxCfg = preset == PresetType.Fast ? 1.5 : 3.5;
                var fluxDenoise = preset == PresetType.Fast ? 0.70 : 1.0;
                RhinoApp.WriteLine($"Glimpse AI: Preset: {preset} | Pipeline: Flux | CFG: {fluxCfg:F1} | Denoise: {fluxDenoise:F2}");
            }
            else
            {
                RhinoApp.WriteLine($"Glimpse AI: Preset: {preset} | Pipeline: SDXL | CFG: {cfgScale:F1} | Denoise: {denoise:F2}");
            }
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
            
            // Generate auto-prompt if enabled
            var finalPrompt = prompt;
            if (settings.PromptMode != PromptMode.Manual)
            {
                StatusChanged?.Invoke(this, "Generating auto-prompt…");
                finalPrompt = await GenerateAutoPromptAsync(prompt, viewportImage);
                StatusChanged?.Invoke(this, "Sending to ComfyUI…");
            }
            // Determine selected model
            string selectedCheckpoint = null;
            string selectedFluxUnet = _useFlux ? settings.FluxUNetModel : null;
            var selectedModel = settings.SelectedModel;
            if (!string.IsNullOrEmpty(selectedModel) && selectedModel != "auto")
            {
                if (_useFlux)
                    selectedFluxUnet = selectedModel;
                else
                    selectedCheckpoint = selectedModel;
            }

            var request = new RenderRequest
            {
                ViewportImage = viewportImage,
                DepthImage = depthImage,
                Prompt = finalPrompt,
                NegativePrompt = settings.DefaultNegativePrompt,
                Preset = preset,
                DenoiseStrength = denoise,
                CfgScale = cfgScale,
                Seed = seed,
                CheckpointModel = selectedCheckpoint,
                UseFlux = _useFlux,
                FluxUnetModel = _useFlux ? selectedFluxUnet : null,
                FluxClip1 = _useFlux ? settings.FluxClipModel1 : null,
                FluxClip2 = _useFlux ? settings.FluxClipModel2 : null,
                FluxVae = _useFlux ? settings.FluxVaeModel : null,
                FluxControlNet = _useFlux ? settings.FluxControlNetModel : null
            };

            var result = await _comfyClient.GenerateAsync(request, ct);
            stopwatch.Stop();

            // Detailed logging
            if (result.Success)
            {
                var doc = RhinoDoc.ActiveDoc;
                var resolution = "Unknown";
                var logSettings = GlimpseAIPlugin.Instance?.GlimpseSettings ?? new GlimpseSettings();
                if (doc?.Views.ActiveView?.ActiveViewport != null)
                {
                    var res = ViewportCapture.GetResolutionForPreset(preset);
                    resolution = $"{res.width}x{res.height}";
                }

                if (_useFlux)
                {
                    RhinoApp.WriteLine($"Glimpse AI: Flux UNet: {logSettings.FluxUNetModel ?? "Unknown"}");
                    if (preset != PresetType.Fast && !string.IsNullOrEmpty(logSettings.FluxControlNetModel))
                    {
                        RhinoApp.WriteLine($"Glimpse AI: Flux ControlNet: {logSettings.FluxControlNetModel} (strength: {logSettings.ControlNetStrength:F1})");
                    }
                }
                else
                {
                    RhinoApp.WriteLine($"Glimpse AI: Model: {result.CheckpointName ?? "Unknown"}");
                    if (logSettings.UseControlNet && preset != PresetType.Fast)
                    {
                        RhinoApp.WriteLine($"Glimpse AI: ControlNet: {logSettings.ControlNetModel ?? "Auto"} (strength: {logSettings.ControlNetStrength:F1})");
                    }
                }
                var pipelineType = _useFlux ? "Flux" : "SDXL";
                RhinoApp.WriteLine($"Glimpse AI: Pipeline: {pipelineType} | Resolution: {resolution} | Steps: {GetStepsForPreset(preset)}");
                RhinoApp.WriteLine($"Glimpse AI: Generation complete in {stopwatch.Elapsed.TotalSeconds:F1}s");
                RhinoApp.WriteLine($"Glimpse AI: Memory: {GC.GetTotalMemory(false) / 1024 / 1024}MB managed | Model: {result.CheckpointName ?? "Unknown"} | Preset: {preset}");
            }
            else
            {
                RhinoApp.WriteLine($"Glimpse AI: Generation failed in {stopwatch.Elapsed.TotalSeconds:F1}s");
            }

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
            
            // Memory management
            var settings = GlimpseAIPlugin.Instance?.GlimpseSettings ?? new GlimpseSettings();
            
            // Force garbage collection after large operations
            GC.Collect(2, GCCollectionMode.Optimized, false);
            
            if (settings.AggressiveMemoryCleanup)
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
            }
            
            // Memory usage tracking for leak detection
            var endMemory = GC.GetTotalMemory(false);
            var memoryIncrease = endMemory - startMemory;
            if (memoryIncrease > 10 * 1024 * 1024) // > 10 MB increase
            {
                var afterGcMemory = GC.GetTotalMemory(true);
                var actualIncrease = afterGcMemory - startMemory;
                
                if (actualIncrease > 5 * 1024 * 1024) // > 5 MB after GC
                {
                    RhinoApp.WriteLine($"Glimpse AI: Memory usage increased by {actualIncrease / (1024 * 1024)}MB after generation");
                }
            }
        }
    }

    /// <summary>
    /// Gets the number of steps for a given preset (for logging purposes).
    /// </summary>
    private int GetStepsForPreset(PresetType preset)
    {
        if (_useFlux)
        {
            return preset switch
            {
                PresetType.Fast => 6,
                PresetType.Balanced => 20,
                PresetType.HighQuality => 28,
                PresetType.Export4K => 28,
                _ => 20
            };
        }
        else
        {
            return preset switch
            {
                PresetType.Fast => 8,
                PresetType.Balanced => 20,
                PresetType.HighQuality => 30,
                PresetType.Export4K => 30,
                _ => 20
            };
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

        try
        {
            CancelCurrentGeneration();
            _currentGenerationCts = new CancellationTokenSource();
            var ct = _currentGenerationCts.Token;

            // Start background task with proper error handling
            _ = Task.Run(async () =>
            {
                try
                {
                    byte[] viewportImage = null;
                    byte[] depthImage = null;

                    var tcs = new TaskCompletionSource<bool>();
                    RhinoApp.InvokeOnUiThread(new Action(() =>
                    {
                        try
                        {
                            if (_disposed || ct.IsCancellationRequested)
                            {
                                tcs.TrySetResult(false);
                                return;
                            }

                            var capture = CaptureCurrentViewport(_autoPreset);
                            viewportImage = capture.viewportImage;
                            depthImage = capture.depthImage;
                            tcs.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            RhinoApp.WriteLine($"Glimpse AI: Viewport capture error in auto-mode: {ex.Message}");
                            tcs.TrySetException(ex);
                        }
                    }));

                    var captureSucceeded = await tcs.Task;
                    if (!captureSucceeded || ct.IsCancellationRequested)
                        return;

                    if (viewportImage == null || viewportImage.Length == 0)
                    {
                        StatusChanged?.Invoke(this, "Auto mode: Failed to capture viewport");
                        return;
                    }

                    await GenerateFromCaptureAsync(
                        viewportImage, depthImage,
                        _autoPrompt, _autoPreset, _autoDenoise, _autoCfgScale, _autoSeed, ct);
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation, no error logging needed
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"Glimpse AI: Auto-generation error: {ex.Message}");
                    
                    try
                    {
                        StatusChanged?.Invoke(this, $"Auto mode error: {ex.Message}");
                        BusyChanged?.Invoke(this, false);
                    }
                    catch
                    {
                        // Prevent recursive exceptions in event handlers
                    }
                }
            }, ct);
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"Glimpse AI: Error starting auto-generation: {ex.Message}");
        }
    }

    /// <summary>
    /// Cancels any in-flight generation request, including server-side interruption.
    /// </summary>
    private void CancelCurrentGeneration()
    {
        var ctsToDispose = _currentGenerationCts;
        _currentGenerationCts = null;
        
        if (ctsToDispose != null)
        {
            try
            {
                if (!ctsToDispose.IsCancellationRequested)
                {
                    ctsToDispose.Cancel();
                    
                    // Also interrupt server-side generation (fire and forget)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _comfyClient.InterruptGenerationAsync();
                        }
                        catch (Exception ex)
                        {
                            RhinoApp.WriteLine($"Glimpse AI: Error interrupting server generation: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Glimpse AI: Error cancelling generation: {ex.Message}");
            }
            
            try
            {
                ctsToDispose.Dispose();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Glimpse AI: Error disposing cancellation token: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            try
            {
                // Cancel any running generation first
                CancelCurrentGeneration();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Glimpse AI: Error cancelling generation during dispose: {ex.Message}");
            }
            
            try
            {
                // Stop viewport watcher
                _watcher?.Dispose();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Glimpse AI: Error disposing viewport watcher: {ex.Message}");
            }
            
            try
            {
                // Dispose overlay conduit
                _overlayConduit?.Dispose();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Glimpse AI: Error disposing overlay conduit: {ex.Message}");
            }
            
            try
            {
                // Disconnect WebSocket with timeout to prevent blocking Rhino close
                if (_comfyClient != null)
                {
                    var disconnectTask = _comfyClient.DisconnectWebSocketAsync();
                    if (!disconnectTask.Wait(TimeSpan.FromSeconds(2)))
                    {
                        RhinoApp.WriteLine("Glimpse AI: WebSocket disconnect timed out, forcing close");
                    }
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Glimpse AI: Error disconnecting WebSocket: {ex.Message}");
            }
            
            try
            {
                // Dispose ComfyUI client
                _comfyClient?.Dispose();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Glimpse AI: Error disposing ComfyUI client: {ex.Message}");
            }
        }
    }
}
