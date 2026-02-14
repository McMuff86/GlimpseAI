using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GlimpseAI.Models;

namespace GlimpseAI.Services;

/// <summary>
/// Client for the ComfyUI API using HTTP for uploads/queueing and WebSocket for
/// real-time progress, latent previews, and completion detection.
/// </summary>
public class ComfyUIClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly string _clientId;
    private ClientWebSocket _ws;
    private bool _disposed;

    /// <summary>Maximum time to wait for a single generation before timing out.</summary>
    private static readonly TimeSpan MaxWaitTime = TimeSpan.FromMinutes(5);

    /// <summary>Raised when ComfyUI reports sampling progress (step/totalSteps).</summary>
    public event EventHandler<ProgressEventArgs> ProgressChanged;

    /// <summary>Raised when a latent preview image is received via WebSocket.</summary>
    public event EventHandler<PreviewImageEventArgs> PreviewImageReceived;

    public ComfyUIClient(string baseUrl = "http://localhost:8188")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _clientId = Guid.NewGuid().ToString("N");
        _client = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    /// <summary>
    /// The unique client ID used for WebSocket subscription.
    /// </summary>
    public string ClientId => _clientId;

    #region HTTP API Methods

    /// <summary>
    /// Checks whether the ComfyUI server is reachable and responding.
    /// </summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _client.GetAsync("/system_stats");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Queries ComfyUI for available checkpoint models.
    /// </summary>
    public async Task<List<string>> GetAvailableCheckpointsAsync()
    {
        try
        {
            var response = await _client.GetAsync("/object_info/CheckpointLoaderSimple");
            if (!response.IsSuccessStatusCode)
                return new List<string>();

            var json = await response.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(json);

            var ckptNames = root?["CheckpointLoaderSimple"]?["input"]?["required"]?["ckpt_name"];
            if (ckptNames is JsonArray outerArr && outerArr.Count > 0 && outerArr[0] is JsonArray namesArr)
            {
                return namesArr
                    .Select(n => n?.GetValue<string>())
                    .Where(n => n != null)
                    .ToList();
            }

            return new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Queries ComfyUI for available ControlNet models.
    /// </summary>
    public async Task<List<string>> GetAvailableControlNetsAsync()
    {
        try
        {
            var response = await _client.GetAsync("/object_info/ControlNetLoader");
            if (!response.IsSuccessStatusCode)
                return new List<string>();

            var json = await response.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(json);

            var controlNetNames = root?["ControlNetLoader"]?["input"]?["required"]?["control_net_name"];
            if (controlNetNames is JsonArray outerArr && outerArr.Count > 0 && outerArr[0] is JsonArray namesArr)
            {
                return namesArr
                    .Select(n => n?.GetValue<string>())
                    .Where(n => n != null)
                    .ToList();
            }

            return new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Finds the best available ControlNet model for depth control.
    /// Returns null if no suitable ControlNet model is found.
    /// </summary>
    public async Task<string> GetBestDepthControlNetAsync(string checkpointName = null)
    {
        var available = await GetAvailableControlNetsAsync();
        if (available.Count == 0)
            return null;

        // Check if the checkpoint is SDXL
        bool isSDXL = !string.IsNullOrEmpty(checkpointName) && 
                      (checkpointName.Contains("xl", StringComparison.OrdinalIgnoreCase) ||
                       checkpointName.Contains("sdxl", StringComparison.OrdinalIgnoreCase));

        // Preferred depth ControlNet models in order of preference
        // For SDXL checkpoints, prefer SDXL ControlNets first
        var preferred = isSDXL
            ? new[]
            {
                "diffusers_xl_depth_full",
                "diffusion_pytorch_model.fp16",
                "controlnet-depth-sdxl-1.0",
                "controlnet-depth-sdxl",
                "t2i-adapter_diffusers_xl_depth_midas",
                "control-lora-depth-rank256",
            }
            : new[]
            {
                "control_v11f1p_sd15_depth",
                "controlnet_depth",
                "control-lora-depth-rank256",
            };

        foreach (var pref in preferred)
        {
            var match = available.FirstOrDefault(a =>
                a.Contains(pref, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                // Check for potential ControlNet/Checkpoint mismatch
                bool controlNetIsSDXL = match.Contains("xl", StringComparison.OrdinalIgnoreCase) ||
                                        match.Contains("sdxl", StringComparison.OrdinalIgnoreCase);
                
                if (isSDXL && !controlNetIsSDXL)
                {
                    Rhino.RhinoApp.WriteLine($"Glimpse AI: WARNING - ControlNet model '{match}' may not be compatible with checkpoint '{checkpointName}'. For SDXL checkpoints, use an SDXL ControlNet.");
                }
                else if (!isSDXL && controlNetIsSDXL && !string.IsNullOrEmpty(checkpointName))
                {
                    Rhino.RhinoApp.WriteLine($"Glimpse AI: WARNING - ControlNet model '{match}' may not be compatible with checkpoint '{checkpointName}'. For SD 1.5 checkpoints, use an SD 1.5 ControlNet.");
                }

                return match;
            }
        }

        // If no depth-specific model found, return the first available
        var firstMatch = available.FirstOrDefault();
        if (firstMatch != null && !string.IsNullOrEmpty(checkpointName))
        {
            bool controlNetIsSDXL = firstMatch.Contains("xl", StringComparison.OrdinalIgnoreCase) ||
                                    firstMatch.Contains("sdxl", StringComparison.OrdinalIgnoreCase);
            
            if (isSDXL && !controlNetIsSDXL)
            {
                Rhino.RhinoApp.WriteLine($"Glimpse AI: WARNING - ControlNet model '{firstMatch}' may not be compatible with checkpoint '{checkpointName}'. For SDXL checkpoints, use an SDXL ControlNet.");
            }
            else if (!isSDXL && controlNetIsSDXL)
            {
                Rhino.RhinoApp.WriteLine($"Glimpse AI: WARNING - ControlNet model '{firstMatch}' may not be compatible with checkpoint '{checkpointName}'. For SD 1.5 checkpoints, use an SD 1.5 ControlNet.");
            }
        }

        return firstMatch;
    }

    /// <summary>
    /// Finds the best available checkpoint for the given preset.
    /// </summary>
    public async Task<string> GetCheckpointForPresetAsync(PresetType preset)
    {
        var available = await GetAvailableCheckpointsAsync();
        if (available.Count == 0)
            return null;

        var preferred = preset switch
        {
            PresetType.Fast => new[] { "dreamshaperXL_turboDPMSDE", "dreamshaper", "realvis", "juggernaut", "sd_xl" },
            PresetType.Balanced => new[] { "juggernautXL", "juggernaut", "realvis", "dreamshaper", "sd_xl" },
            PresetType.HighQuality => new[] { "dvarch", "juggernautXL", "realvis", "sd_xl" },
            PresetType.Export4K => new[] { "dvarch", "juggernautXL", "realvis", "sd_xl" },
            _ => new[] { "sd_xl", "dreamshaper", "juggernaut" }
        };

        foreach (var pref in preferred)
        {
            var match = available.FirstOrDefault(a =>
                a.Contains(pref, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        return available[0];
    }

    /// <summary>
    /// Uploads an image to the ComfyUI input folder.
    /// </summary>
    public async Task<string> UploadImageAsync(byte[] imageData, string filename)
    {
        using var content = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(imageData);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(imageContent, "image", filename);
        content.Add(new StringContent("true"), "overwrite");

        var response = await _client.PostAsync("/upload/image", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        return node["name"]?.GetValue<string>()
               ?? throw new InvalidOperationException("ComfyUI upload response missing 'name' field.");
    }

    /// <summary>
    /// Queues a workflow prompt for execution, including the client ID for WebSocket routing.
    /// </summary>
    public async Task<string> QueuePromptAsync(Dictionary<string, object> workflow)
    {
        var payload = new Dictionary<string, object>
        {
            ["prompt"] = workflow,
            ["client_id"] = _clientId
        };

        var jsonContent = JsonSerializer.Serialize(payload);
        var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/prompt", httpContent);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"ComfyUI rejected the prompt ({response.StatusCode}): {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(responseJson);
        return node["prompt_id"]?.GetValue<string>()
               ?? throw new InvalidOperationException("ComfyUI prompt response missing 'prompt_id' field.");
    }

    /// <summary>
    /// Interrupts the currently running generation on the ComfyUI server.
    /// This actually stops the server-side processing, not just client-side waiting.
    /// </summary>
    public async Task<bool> InterruptGenerationAsync()
    {
        try
        {
            var response = await _client.PostAsync("/interrupt", null);
            if (response.IsSuccessStatusCode)
            {
                Rhino.RhinoApp.WriteLine("Glimpse AI: Server-side generation interrupted");
                return true;
            }
            else
            {
                Rhino.RhinoApp.WriteLine($"Glimpse AI: Failed to interrupt generation: {response.StatusCode}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"Glimpse AI: Error interrupting generation: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Downloads an output image from the ComfyUI server.
    /// </summary>
    public async Task<byte[]> DownloadImageAsync(string filename, string subfolder = "")
    {
        var url = $"/view?filename={Uri.EscapeDataString(filename)}&type=output";
        if (!string.IsNullOrEmpty(subfolder))
            url += $"&subfolder={Uri.EscapeDataString(subfolder)}";

        var response = await _client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// Extracts the output image info from a completed prompt's history.
    /// Retries several times because the history may not be immediately available
    /// after WebSocket signals completion.
    /// </summary>
    private async Task<(string filename, string subfolder)?> GetOutputImageInfoAsync(string promptId)
    {
        // Retry up to 10 times with 300ms delay — history may lag behind WebSocket
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(300);

            try
            {
                var response = await _client.GetAsync($"/history/{promptId}");
                if (!response.IsSuccessStatusCode)
                    continue;

                var json = await response.Content.ReadAsStringAsync();
                var root = JsonNode.Parse(json);

                if (root is not JsonObject obj || !obj.ContainsKey(promptId))
                    continue;

                var outputs = root[promptId]?["outputs"];
                if (outputs is not JsonObject outputNodes || outputNodes.Count == 0)
                    continue;

                foreach (var kvp in outputNodes)
                {
                    var images = kvp.Value?["images"];
                    if (images is JsonArray arr && arr.Count > 0)
                    {
                        var firstImage = arr[0];
                        var filename = firstImage?["filename"]?.GetValue<string>();
                        var subfolder = firstImage?["subfolder"]?.GetValue<string>() ?? "";
                        if (filename != null)
                            return (filename, subfolder);
                    }
                }
            }
            catch
            {
                // Retry on any error
            }
        }

        return null;
    }

    #endregion

    #region WebSocket Connection

    private int _reconnectAttempts = 0;
    private const int MaxReconnectAttempts = 3;
    private static readonly TimeSpan[] ReconnectDelays = {
        TimeSpan.FromMilliseconds(500),  // First retry: 500ms
        TimeSpan.FromSeconds(2),         // Second retry: 2s  
        TimeSpan.FromSeconds(5)          // Third retry: 5s
    };

    /// <summary>
    /// Connects to the ComfyUI WebSocket endpoint with automatic retry logic.
    /// </summary>
    public async Task ConnectWebSocketAsync(CancellationToken ct = default)
    {
        if (_ws != null && _ws.State == WebSocketState.Open)
            return;

        var wsUrl = _baseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        var uri = new Uri($"{wsUrl}/ws?clientId={_clientId}");

        for (int attempt = 0; attempt <= MaxReconnectAttempts; attempt++)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(10)); // 10s connection timeout

                await _ws.ConnectAsync(uri, timeoutCts.Token);
                _reconnectAttempts = 0; // Reset counter on successful connection
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Don't retry if externally cancelled
            }
            catch (Exception ex)
            {
                _ws?.Dispose();
                _ws = null;

                if (attempt == MaxReconnectAttempts)
                {
                    Rhino.RhinoApp.WriteLine($"Glimpse AI: WebSocket connection failed after {MaxReconnectAttempts + 1} attempts: {ex.Message}");
                    throw;
                }

                var delay = ReconnectDelays[Math.Min(attempt, ReconnectDelays.Length - 1)];
                Rhino.RhinoApp.WriteLine($"Glimpse AI: WebSocket connection failed (attempt {attempt + 1}/{MaxReconnectAttempts + 1}), retrying in {delay.TotalSeconds}s: {ex.Message}");
                
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    throw; // Don't continue retrying if cancelled
                }
            }
        }
    }

    /// <summary>
    /// Disconnects the WebSocket if connected.
    /// </summary>
    public async Task DisconnectWebSocketAsync()
    {
        if (_ws == null) return;

        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
            }
        }
        catch
        {
            // Best-effort close
        }
        finally
        {
            _ws.Dispose();
            _ws = null;
        }
    }

    /// <summary>
    /// Whether the WebSocket is currently connected.
    /// </summary>
    public bool IsWebSocketConnected => _ws?.State == WebSocketState.Open;

    #endregion

    #region Generation Pipeline

    /// <summary>
    /// Full generation pipeline using WebSocket for real-time progress and completion.
    /// Falls back to HTTP polling if WebSocket is unavailable.
    /// </summary>
    public async Task<RenderResult> GenerateAsync(RenderRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var seed = request.Seed < 0 ? Random.Shared.Next() : request.Seed;

        try
        {
            // 1. Upload viewport capture
            var viewportFilename = await UploadImageAsync(
                request.ViewportImage,
                $"glimpse_viewport_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            ct.ThrowIfCancellationRequested();

            // 2. Upload depth image if available
            string depthFilename = null;
            if (request.DepthImage != null && request.DepthImage.Length > 0)
            {
                depthFilename = await UploadImageAsync(
                    request.DepthImage,
                    $"glimpse_depth_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            }

            // 3. Auto-detect checkpoint
            var checkpointName = await GetCheckpointForPresetAsync(request.Preset);
            if (checkpointName == null)
            {
                return RenderResult.Fail(
                    "No checkpoint models found in ComfyUI. Please install at least one SDXL checkpoint in ComfyUI/models/checkpoints/.",
                    stopwatch.Elapsed);
            }

            // 4. Get ControlNet settings from GlimpseSettings
            var settings = GlimpseAI.GlimpseAIPlugin.Instance?.GlimpseSettings ?? new GlimpseSettings();
            string controlNetModel = null;
            
            // Auto-detect ControlNet model (for non-Fast presets) if enabled
            if (request.Preset != PresetType.Fast && settings.UseControlNet)
            {
                if (!string.IsNullOrEmpty(settings.ControlNetModel))
                {
                    controlNetModel = settings.ControlNetModel;
                }
                else
                {
                    controlNetModel = await GetBestDepthControlNetAsync(checkpointName);
                    if (controlNetModel == null)
                    {
                        Rhino.RhinoApp.WriteLine("Glimpse AI: No ControlNet models found - falling back to img2img for this generation");
                    }
                    else
                    {
                        // Save the auto-detected model for next time
                        settings.ControlNetModel = controlNetModel;
                        GlimpseAI.GlimpseAIPlugin.Instance?.SaveGlimpseSettings();
                    }
                }
            }

            // 5. Build workflow — always use SaveImage for reliable output retrieval
            // WebSocket is used for progress/previews only, not for final image delivery
            var workflow = WorkflowBuilder.BuildWorkflow(
                request.Preset,
                viewportFilename,
                depthFilename,
                request.Prompt,
                request.NegativePrompt,
                request.DenoiseStrength,
                seed,
                checkpointName,
                request.CfgScale,
                controlNetModel,
                controlNetStrength: settings.ControlNetStrength,
                useDepthPreprocessor: settings.UseDepthPreprocessor,
                useWebSocketOutput: false);

            // 5. Queue prompt
            var promptId = await QueuePromptAsync(workflow);

            // 6. Wait for completion via WebSocket or HTTP polling
            if (IsWebSocketConnected)
            {
                var result = await WaitForCompletionWebSocketAsync(promptId, request, seed, checkpointName, stopwatch, ct);
                return result;
            }
            else
            {
                return await WaitForCompletionPollingAsync(promptId, request, seed, checkpointName, stopwatch, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Attempt to interrupt server-side generation
            try
            {
                _ = InterruptGenerationAsync(); // Fire and forget
            }
            catch
            {
                // Best effort interrupt, don't fail if it doesn't work
            }
            
            return RenderResult.Fail("Generation was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            return RenderResult.Fail($"Generation failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Waits for completion using WebSocket messages for instant detection.
    /// Progress and latent previews are delivered via WebSocket; final image is downloaded via HTTP.
    /// Uses dynamic buffer allocation to handle large preview images safely.
    /// </summary>
    private async Task<RenderResult> WaitForCompletionWebSocketAsync(
        string promptId, RenderRequest request, int seed, string checkpointName, Stopwatch stopwatch, CancellationToken ct)
    {
        const int InitialBufferSize = 4 * 1024 * 1024; // 4 MB initial buffer
        const int MaxBufferSize = 32 * 1024 * 1024;    // 32 MB max buffer
        
        var buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        var currentBufferSize = InitialBufferSize;

        try
        {
            var deadline = DateTime.UtcNow + MaxWaitTime;

            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                if (_ws == null || _ws.State != WebSocketState.Open)
                {
                    return await WaitForCompletionPollingAsync(promptId, request, seed, checkpointName, stopwatch, ct);
                }

                WebSocketReceiveResult wsResult;
                byte[] messageBuffer = buffer;
                
                try
                {
                    // Start receiving with current buffer
                    var segment = new ArraySegment<byte>(buffer, 0, currentBufferSize);
                    wsResult = await _ws.ReceiveAsync(segment, ct);
                    
                    // Check if message was truncated and we need a larger buffer
                    if (!wsResult.EndOfMessage)
                    {
                        // Message is larger than current buffer - need to receive in chunks
                        using var ms = new System.IO.MemoryStream();
                        ms.Write(buffer, 0, wsResult.Count);
                        
                        while (!wsResult.EndOfMessage && !ct.IsCancellationRequested)
                        {
                            wsResult = await _ws.ReceiveAsync(segment, ct);
                            ms.Write(buffer, 0, wsResult.Count);
                            
                            // Prevent excessive memory usage
                            if (ms.Length > MaxBufferSize)
                            {
                                Rhino.RhinoApp.WriteLine($"Glimpse AI: WebSocket message too large ({ms.Length} bytes), skipping");
                                ms.SetLength(0); // Clear the stream
                                break;
                            }
                        }
                        
                        if (ms.Length > 0 && ms.Length <= MaxBufferSize)
                        {
                            messageBuffer = ms.ToArray();
                            wsResult = new WebSocketReceiveResult(
                                (int)ms.Length, 
                                wsResult.MessageType, 
                                true,
                                wsResult.CloseStatus,
                                wsResult.CloseStatusDescription);
                        }
                        else
                        {
                            continue; // Skip this oversized message
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return RenderResult.Fail("Generation was cancelled.", stopwatch.Elapsed);
                }
                catch (WebSocketException ex)
                {
                    Rhino.RhinoApp.WriteLine($"Glimpse AI: WebSocket error: {ex.Message}");
                    
                    // Attempt to reconnect once before falling back to polling
                    if (_reconnectAttempts < 1)
                    {
                        _reconnectAttempts++;
                        Rhino.RhinoApp.WriteLine("Glimpse AI: Attempting WebSocket reconnection...");
                        
                        try
                        {
                            await ConnectWebSocketAsync(ct);
                            continue; // Retry the receive loop
                        }
                        catch
                        {
                            Rhino.RhinoApp.WriteLine("Glimpse AI: WebSocket reconnection failed, falling back to HTTP polling");
                        }
                    }
                    
                    return await WaitForCompletionPollingAsync(promptId, request, seed, checkpointName, stopwatch, ct);
                }

                if (wsResult.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(messageBuffer, 0, wsResult.Count);
                    HandleTextMessage(text, promptId, out bool completed, out bool error);

                    if (error)
                    {
                        return RenderResult.Fail(
                            "ComfyUI reported an error during generation. Check ComfyUI console for details.",
                            stopwatch.Elapsed);
                    }

                    if (completed)
                    {
                        // Download the final image via HTTP (SaveImage wrote it to disk)
                        var imageInfo = await GetOutputImageInfoAsync(promptId);
                        if (imageInfo == null)
                        {
                            return RenderResult.Fail("Generation completed but no output image found.", stopwatch.Elapsed);
                        }

                        var imageData = await DownloadImageAsync(imageInfo.Value.filename, imageInfo.Value.subfolder);
                        stopwatch.Stop();
                        return RenderResult.Ok(imageData, imageInfo.Value.filename, stopwatch.Elapsed, request.Preset, seed, checkpointName);
                    }
                }
                else if (wsResult.MessageType == WebSocketMessageType.Binary)
                {
                    // Binary messages from ComfyUI: 4-byte big-endian type header + image data
                    // Type 1 = latent preview image (JPEG/PNG)
                    if (wsResult.Count > 8)
                    {
                        // Read type as big-endian uint32
                        int eventType = (messageBuffer[0] << 24) | (messageBuffer[1] << 16) | (messageBuffer[2] << 8) | messageBuffer[3];

                        if (eventType == 1 || eventType == 2)
                        {
                            // Skip 8-byte header (4 bytes type + 4 bytes format)
                            var pngData = new byte[wsResult.Count - 8];
                            Array.Copy(messageBuffer, 8, pngData, 0, pngData.Length);
                            PreviewImageReceived?.Invoke(this, new PreviewImageEventArgs { ImageData = pngData });
                        }
                    }
                }
                else if (wsResult.MessageType == WebSocketMessageType.Close)
                {
                    return await WaitForCompletionPollingAsync(promptId, request, seed, checkpointName, stopwatch, ct);
                }
            }

            if (ct.IsCancellationRequested)
                return RenderResult.Fail("Generation was cancelled.", stopwatch.Elapsed);

            return RenderResult.Fail("Generation timed out.", stopwatch.Elapsed);
        }
        finally
        {
            // Return rented buffer to pool
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Parses a text WebSocket message and updates progress/completion state.
    /// </summary>
    private void HandleTextMessage(string text, string promptId, out bool completed, out bool error)
    {
        completed = false;
        error = false;

        try
        {
            var root = JsonNode.Parse(text);
            var type = root?["type"]?.GetValue<string>();
            var data = root?["data"];

            switch (type)
            {
                case "progress":
                    var step = data?["value"]?.GetValue<int>() ?? 0;
                    var maxSteps = data?["max"]?.GetValue<int>() ?? 0;
                    ProgressChanged?.Invoke(this, new ProgressEventArgs { Step = step, TotalSteps = maxSteps });
                    break;

                case "executing":
                    var msgPromptId = data?["prompt_id"]?.GetValue<string>();
                    if (msgPromptId != promptId) break;

                    // Check if node is null (JSON null = execution finished)
                    // data["node"] returns C# null for JSON null, but we also need
                    // to handle it being a JsonValue containing null
                    var nodeToken = data?["node"];
                    bool nodeIsNull = nodeToken == null ||
                        (nodeToken is JsonValue jv && jv.GetValueKind() == System.Text.Json.JsonValueKind.Null);

                    if (nodeIsNull)
                    {
                        Rhino.RhinoApp.WriteLine($"Glimpse AI: WS execution complete for {promptId}");
                        completed = true;
                    }
                    break;

                // "executed" fires per-node — don't treat as completion
                // Only "executing {node: null}" signals full prompt completion

                case "execution_error":
                    var errPromptId = data?["prompt_id"]?.GetValue<string>();
                    if (errPromptId == promptId)
                    {
                        error = true;
                    }
                    break;
            }
        }
        catch
        {
            // Non-JSON message, ignore
        }
    }

    /// <summary>
    /// Fallback: HTTP polling for completion (used when WebSocket is unavailable).
    /// </summary>
    private async Task<RenderResult> WaitForCompletionPollingAsync(
        string promptId, RenderRequest request, int seed, string checkpointName, Stopwatch stopwatch, CancellationToken ct)
    {
        const int pollIntervalMs = 150;
        var deadline = DateTime.UtcNow + MaxWaitTime;

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync($"/history/{promptId}", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var root = JsonNode.Parse(json);

                if (root is JsonObject obj && obj.ContainsKey(promptId))
                {
                    var promptData = obj[promptId];
                    var status = promptData?["status"];
                    bool statusCompleted = false;
                    bool statusError = false;

                    if (status != null)
                    {
                        statusCompleted = status["completed"]?.GetValue<bool>() ?? false;
                        statusError = status["status_str"]?.GetValue<string>() == "error";
                    }
                    else
                    {
                        var outputs = promptData?["outputs"];
                        if (outputs is JsonObject outputsObj && outputsObj.Count > 0)
                            statusCompleted = true;
                    }

                    if (statusError)
                    {
                        return RenderResult.Fail(
                            "ComfyUI reported an error during generation. Check ComfyUI console for details.",
                            stopwatch.Elapsed);
                    }

                    if (statusCompleted)
                    {
                        var imageInfo = await GetOutputImageInfoAsync(promptId);
                        if (imageInfo == null)
                        {
                            return RenderResult.Fail("Generation completed but no output image found.", stopwatch.Elapsed);
                        }

                        var imageData = await DownloadImageAsync(imageInfo.Value.filename, imageInfo.Value.subfolder);
                        stopwatch.Stop();
                        return RenderResult.Ok(imageData, imageInfo.Value.filename, stopwatch.Elapsed, request.Preset, seed, checkpointName);
                    }
                }
            }

            await Task.Delay(pollIntervalMs, ct);
        }

        if (ct.IsCancellationRequested)
            return RenderResult.Fail("Generation was cancelled.", stopwatch.Elapsed);

        return RenderResult.Fail("Generation timed out.", stopwatch.Elapsed);
    }

    #endregion

    #region Florence2 Vision Support

    /// <summary>
    /// Checks if Florence2 node is available in ComfyUI.
    /// </summary>
    public async Task<bool> IsFlorence2AvailableAsync()
    {
        try
        {
            var response = await _client.GetAsync("/object_info/Florence2Run");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Runs Florence2 caption on an uploaded image.
    /// Queues a minimal workflow: LoadImage → Florence2Run → outputs caption text.
    /// </summary>
    /// <param name="uploadedImageName">Name of previously uploaded image.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generated caption or null if failed.</returns>
    public async Task<string> GetFlorence2CaptionAsync(string uploadedImageName, CancellationToken ct)
    {
        var workflow = new Dictionary<string, object>
        {
            ["1"] = new Dictionary<string, object>
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new Dictionary<string, object> { ["image"] = uploadedImageName }
            },
            ["2"] = new Dictionary<string, object>
            {
                ["class_type"] = "DownloadAndLoadFlorence2Model",
                ["inputs"] = new Dictionary<string, object>
                {
                    ["model"] = "microsoft/Florence-2-base",
                    ["precision"] = "fp16",
                    ["attention"] = "sdpa"
                }
            },
            ["3"] = new Dictionary<string, object>
            {
                ["class_type"] = "Florence2Run",
                ["inputs"] = new Dictionary<string, object>
                {
                    ["text"] = "",
                    ["task"] = "detailed_caption",
                    ["fill_mask"] = true,
                    ["keep_model_loaded"] = true,
                    ["max_new_tokens"] = 256,
                    ["num_beams"] = 3,
                    ["do_sample"] = false,
                    ["image"] = new object[] { "1", 0 },
                    ["florence2_model"] = new object[] { "2", 0 }
                }
            },
            // Dummy SaveImage node to ensure workflow executes properly
            ["4"] = new Dictionary<string, object>
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new Dictionary<string, object>
                {
                    ["filename_prefix"] = "GlimpseAI/vision_dummy",
                    ["images"] = new object[] { "1", 0 }
                }
            }
        };

        try
        {
            // Queue the Florence2 workflow
            var promptId = await QueuePromptAsync(workflow);

            // Wait for completion using WebSocket or polling
            var deadline = DateTime.UtcNow.Add(TimeSpan.FromMinutes(2)); // Florence2 timeout
            
            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                try
                {
                    // Check history for completion and text output
                    var response = await _client.GetAsync($"/history/{promptId}", ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(ct);
                        var root = JsonNode.Parse(json);

                        if (root is JsonObject obj && obj.ContainsKey(promptId))
                        {
                            var outputs = root[promptId]?["outputs"];
                            if (outputs is JsonObject outputNodes && outputNodes.ContainsKey("3"))
                            {
                                // Florence2Run outputs text in the "text" field
                                var textOutput = outputs["3"]?["text"];
                                if (textOutput is JsonArray textArray && textArray.Count > 0)
                                {
                                    var caption = textArray[0]?.GetValue<string>();
                                    if (!string.IsNullOrEmpty(caption))
                                    {
                                        Rhino.RhinoApp.WriteLine($"Glimpse AI: Florence2 caption: {caption}");
                                        return caption;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Rhino.RhinoApp.WriteLine($"Glimpse AI: Error checking Florence2 history: {ex.Message}");
                }

                await Task.Delay(200, ct); // Poll every 200ms
            }

            if (ct.IsCancellationRequested)
            {
                await InterruptGenerationAsync(); // Best effort interrupt
                return null;
            }

            Rhino.RhinoApp.WriteLine("Glimpse AI: Florence2 caption generation timed out");
            return null;
        }
        catch (Exception ex)
        {
            Rhino.RhinoApp.WriteLine($"Glimpse AI: Florence2 caption failed: {ex.Message}");
            return null;
        }
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            // Disconnect WebSocket gracefully first
            try
            {
                if (_ws?.State == WebSocketState.Open)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    _ = DisconnectWebSocketAsync(); // Don't await to prevent hanging
                }
            }
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine($"Glimpse AI: Error during WebSocket graceful shutdown: {ex.Message}");
            }
            
            // Force dispose WebSocket
            try 
            { 
                _ws?.Dispose(); 
            } 
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine($"Glimpse AI: Error disposing WebSocket: {ex.Message}");
            }
            
            // Dispose HTTP client
            try 
            { 
                _client?.Dispose(); 
            } 
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine($"Glimpse AI: Error disposing HTTP client: {ex.Message}");
            }
        }
    }
}

/// <summary>
/// Event args for sampling progress updates.
/// </summary>
public class ProgressEventArgs : EventArgs
{
    public int Step { get; set; }
    public int TotalSteps { get; set; }
}

/// <summary>
/// Event args for latent preview images received during generation.
/// </summary>
public class PreviewImageEventArgs : EventArgs
{
    public byte[] ImageData { get; set; }
}
