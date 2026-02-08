using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GlimpseAI.Models;

namespace GlimpseAI.Services;

/// <summary>
/// HTTP client for the ComfyUI API.
/// Handles image upload, workflow queueing, polling, and result download.
/// </summary>
public class ComfyUIClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private bool _disposed;

    /// <summary>
    /// Polling interval in milliseconds when waiting for prompt completion.
    /// </summary>
    private const int PollIntervalMs = 150;

    /// <summary>
    /// Maximum time to wait for a single generation before timing out.
    /// </summary>
    private static readonly TimeSpan MaxWaitTime = TimeSpan.FromMinutes(5);

    public ComfyUIClient(string baseUrl = "http://localhost:8188")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _client = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

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
    /// Uploads an image to the ComfyUI input folder.
    /// </summary>
    /// <param name="imageData">PNG image bytes.</param>
    /// <param name="filename">Desired filename (e.g. "viewport_capture.png").</param>
    /// <returns>The server-assigned filename.</returns>
    public async Task<string> UploadImageAsync(byte[] imageData, string filename)
    {
        using var content = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(imageData);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(imageContent, "image", filename);
        // Upload to input subfolder
        content.Add(new StringContent("input"), "subfolder");
        content.Add(new StringContent("true"), "overwrite");

        var response = await _client.PostAsync("/upload/image", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(json);
        return node["name"]?.GetValue<string>()
               ?? throw new InvalidOperationException("ComfyUI upload response missing 'name' field.");
    }

    /// <summary>
    /// Queues a workflow prompt for execution.
    /// </summary>
    /// <param name="workflow">The workflow dictionary (node graph).</param>
    /// <returns>The prompt ID for tracking.</returns>
    public async Task<string> QueuePromptAsync(Dictionary<string, object> workflow)
    {
        var payload = new Dictionary<string, object>
        {
            ["prompt"] = workflow
        };

        var jsonContent = JsonSerializer.Serialize(payload);
        var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/prompt", httpContent);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(responseJson);
        return node["prompt_id"]?.GetValue<string>()
               ?? throw new InvalidOperationException("ComfyUI prompt response missing 'prompt_id' field.");
    }

    /// <summary>
    /// Checks the status of a queued prompt.
    /// </summary>
    /// <param name="promptId">The prompt ID to check.</param>
    /// <returns>Tuple of (completed, error).</returns>
    public async Task<(bool completed, bool error)> GetPromptStatusAsync(string promptId)
    {
        try
        {
            var response = await _client.GetAsync($"/history/{promptId}");
            if (!response.IsSuccessStatusCode)
                return (false, false);

            var json = await response.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(json);

            // History endpoint returns {} if prompt is still running
            if (root is JsonObject obj && obj.ContainsKey(promptId))
            {
                var promptData = obj[promptId];
                var status = promptData?["status"];

                if (status != null)
                {
                    var statusCompleted = status["completed"]?.GetValue<bool>() ?? false;
                    var statusError = status["status_str"]?.GetValue<string>() == "error";
                    return (statusCompleted, statusError);
                }

                // If we have outputs, it's completed
                var outputs = promptData?["outputs"];
                if (outputs is JsonObject outputsObj && outputsObj.Count > 0)
                    return (true, false);
            }

            return (false, false);
        }
        catch
        {
            return (false, true);
        }
    }

    /// <summary>
    /// Downloads an output image from the ComfyUI server.
    /// </summary>
    /// <param name="filename">Output image filename.</param>
    /// <param name="subfolder">Output subfolder (default: empty).</param>
    /// <returns>Image bytes.</returns>
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
    /// </summary>
    private async Task<(string filename, string subfolder)?> GetOutputImageInfoAsync(string promptId)
    {
        var response = await _client.GetAsync($"/history/{promptId}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json);

        if (root is not JsonObject obj || !obj.ContainsKey(promptId))
            return null;

        var outputs = root[promptId]?["outputs"];
        if (outputs is not JsonObject outputNodes)
            return null;

        // Find the first node with images output
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

        return null;
    }

    /// <summary>
    /// Full generation pipeline: upload images → queue workflow → wait → download result.
    /// </summary>
    /// <param name="request">The render request with images, prompt, and settings.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The render result with output image or error.</returns>
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

            // 2. Upload depth image if available
            string depthFilename = null;
            if (request.DepthImage != null && request.DepthImage.Length > 0)
            {
                depthFilename = await UploadImageAsync(
                    request.DepthImage,
                    $"glimpse_depth_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            }

            // 3. Build workflow
            var workflow = WorkflowBuilder.BuildWorkflow(
                request.Preset,
                viewportFilename,
                depthFilename,
                request.Prompt,
                request.NegativePrompt,
                request.DenoiseStrength,
                seed);

            // 4. Queue prompt
            var promptId = await QueuePromptAsync(workflow);

            // 5. Poll for completion
            var deadline = DateTime.UtcNow + MaxWaitTime;
            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                var (completed, error) = await GetPromptStatusAsync(promptId);

                if (error)
                {
                    return RenderResult.Fail("ComfyUI reported an error during generation.", stopwatch.Elapsed);
                }

                if (completed)
                {
                    // 6. Get output image info
                    var imageInfo = await GetOutputImageInfoAsync(promptId);
                    if (imageInfo == null)
                    {
                        return RenderResult.Fail("Generation completed but no output image found.", stopwatch.Elapsed);
                    }

                    // 7. Download output
                    var imageData = await DownloadImageAsync(imageInfo.Value.filename, imageInfo.Value.subfolder);
                    stopwatch.Stop();

                    return RenderResult.Ok(
                        imageData,
                        imageInfo.Value.filename,
                        stopwatch.Elapsed,
                        request.Preset,
                        seed);
                }

                await Task.Delay(PollIntervalMs, ct);
            }

            if (ct.IsCancellationRequested)
                return RenderResult.Fail("Generation was cancelled.", stopwatch.Elapsed);

            return RenderResult.Fail("Generation timed out.", stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return RenderResult.Fail("Generation was cancelled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            return RenderResult.Fail($"Generation failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client?.Dispose();
            _disposed = true;
        }
    }
}
