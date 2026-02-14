# GlimpseAI Troubleshooting Guide
**Version:** 2.0  
**Letzte Aktualisierung:** 14.02.2026  
**Nach:** Threading-Fixes (fix/stability-and-crashes Branch)

---

## 🚨 Häufige Crash-Ursachen

### **1. Thread-Safety Violations (P0 - KRITISCH)**

#### **Problem:** Rhino-UI Objekte auf Background Threads
```
Symptome:
- Sofortiger Rhino-Crash ohne Warning
- "Cross-thread operation" Exceptions
- Inconsistent crashes bei Viewport-Änderungen

Ursache:
- DisplayBitmap creation/disposal auf Background Thread  
- Viewport manipulation von Non-UI Threads
```

#### **Debugging:**
```csharp
// Debug output in GlimpseOverlayConduit.cs
public void UpdateImage(byte[] pngData)
{
    System.Diagnostics.Debug.WriteLine($"UpdateImage Thread: {Thread.CurrentThread.ManagedThreadId}");
    System.Diagnostics.Debug.WriteLine($"IsUIThread: {!RhinoApp.InvokeRequired}");
    
    // If not UI thread → PROBLEM!
    if (RhinoApp.InvokeRequired) {
        RhinoCommon.RhinoApp.WriteLine("ERROR: UpdateImage called from background thread!");
    }
}
```

#### **Fix:** UI-Thread Marshalling
```csharp
public void UpdateImage(byte[] pngData)
{
    if (pngData == null) return;
    
    // Safe: Bitmap creation auf Background Thread
    Bitmap newBitmap;
    try {
        using var ms = new MemoryStream(pngData);
        newBitmap = new System.Drawing.Bitmap(ms);
    } catch { return; }
    
    // CRITICAL: DisplayBitmap nur auf UI Thread!
    RhinoApp.InvokeOnUiThread(() => {
        lock (_lock) {
            _displayBitmap?.Dispose();
            _displayBitmap = new DisplayBitmap(newBitmap);
        }
        newBitmap.Dispose();
        RhinoDoc.ActiveDoc?.Views.Redraw();
    });
}
```

---

### **2. Race Conditions in Resource Management**

#### **Problem:** Dispose während aktiver Render-Loops
```
Symptome:
- "Object has been disposed" Exceptions
- Intermittente crashes bei Plugin-Shutdown
- Overlay verschwindet sporadisch

Ursache:
- GlimpseOverlayConduit.Dispose() vs DrawForeground() race
- DisplayBitmap disposal während GPU access
```

#### **Debugging:**
```csharp
// Enable race condition detection
protected override void DrawForeground(DrawEventArgs e)
{
    if (_disposed) {
        System.Diagnostics.Debug.WriteLine("WARNING: DrawForeground called on disposed conduit!");
        return;
    }
    
    DisplayBitmap bitmap;
    lock (_lock) {
        if (_disposed || _displayBitmap == null) return;
        bitmap = _displayBitmap;  // Snapshot reference
    }
    
    // Use snapshot, not _displayBitmap directly
    try {
        e.Display.DrawSprite(bitmap, ...);
    } catch (ObjectDisposedException ex) {
        System.Diagnostics.Debug.WriteLine($"Race condition detected: {ex.Message}");
    }
}
```

#### **Fix:** Proper Synchronization
```csharp
public void Dispose() 
{
    if (!_disposed) {
        _disposed = true;
        Enabled = false;  
        
        // Wait for potential DrawForeground completion
        Thread.Sleep(100);
        
        lock (_lock) {
            _displayBitmap?.Dispose();
            _displayBitmap = null;
        }
    }
}
```

---

### **3. ViewportCapture Arctic Mode Issues**

#### **Problem:** Display Mode Switching auf Background Thread
```
Symptome:
- Rhino-Crash bei depth capture
- Viewport "flackert" oder bleibt in arctic mode
- "DisplayMode cannot be changed" exceptions

Ursache:
- viewport.DisplayMode assignment auf Background Thread
- Concurrent viewport operations
```

#### **Debugging:**
```csharp
public static byte[] CaptureDepthApprox(RhinoViewport viewport, int width, int height)
{
    // Thread check
    if (RhinoApp.InvokeRequired) {
        throw new InvalidOperationException("CaptureDepthApprox must run on UI thread!");
    }
    
    System.Diagnostics.Debug.WriteLine($"Viewport Display Mode before: {viewport.DisplayMode?.EnglishName}");
    // ... capture logic
    System.Diagnostics.Debug.WriteLine($"Viewport Display Mode after: {viewport.DisplayMode?.EnglishName}");
}
```

#### **Fix:** UI-Thread Only Operations
```csharp
public static byte[] CaptureDepthApprox(RhinoViewport viewport, int width, int height)
{
    if (RhinoApp.InvokeRequired) {
        // Marshal to UI thread  
        byte[] result = null;
        var completed = false;
        RhinoApp.InvokeOnUiThread(() => {
            result = CaptureDepthApproxInternal(viewport, width, height);
            completed = true;
        });
        
        // Wait with timeout
        var stopwatch = Stopwatch.StartNew();
        while (!completed && stopwatch.ElapsedMilliseconds < 5000) {
            Thread.Sleep(10);
        }
        
        return result ?? new byte[0];
    }
    
    return CaptureDepthApproxInternal(viewport, width, height);
}
```

---

## 🔧 ComfyUI Workflow Debugging

### **WebSocket Connection Issues**

#### **Diagnose:** Connection Status
```bash
# Test ComfyUI server availability  
curl -f http://localhost:8188/system_stats

# Test WebSocket endpoint
wscat -c ws://localhost:8188/ws?clientId=test

# Check if port is open
netstat -an | grep 8188
```

#### **Debug Logging:** 
```csharp
// In ComfyUIClient.cs
private async Task ConnectWebSocket()
{
    try {
        System.Diagnostics.Debug.WriteLine($"Connecting to WebSocket: {_webSocketUrl}");
        await _webSocket.ConnectAsync(new Uri(_webSocketUrl), CancellationToken.None);
        System.Diagnostics.Debug.WriteLine($"WebSocket State: {_webSocket.State}");
    } catch (Exception ex) {
        System.Diagnostics.Debug.WriteLine($"WebSocket connection failed: {ex.Message}");
        // Fallback to HTTP
    }
}
```

### **Workflow Validation**

#### **JSON Schema Errors**
```csharp
// WorkflowBuilder.cs debugging
public string BuildWorkflow(RenderRequest request)
{
    var workflow = /* build logic */;
    
    // Validate JSON structure
    try {
        JObject.Parse(workflow);
        System.Diagnostics.Debug.WriteLine("Workflow JSON valid");
    } catch (JsonReaderException ex) {
        System.Diagnostics.Debug.WriteLine($"Invalid workflow JSON: {ex.Message}");
        RhinoApp.WriteLine($"Workflow build error: {ex.Message}");
    }
    
    return workflow;
}
```

#### **Common Workflow Issues**
```json
// Missing required nodes
{
  "error": "Unknown node type: 'LoadImage'",
  "fix": "Install missing ComfyUI nodes/extensions"
}

// Invalid input connections  
{
  "error": "Input 'image' not found on node 'SaveImage'", 
  "fix": "Check node input/output connections in workflow"
}

// Wrong data types
{
  "error": "Expected INT but got STRING",
  "fix": "Verify parameter types match node requirements"
}
```

---

## 📊 Rhino Log-Analyse

### **Debug Output aktivieren**
```csharp
// In GlimpseAIPlugin.cs OnLoad()
#if DEBUG
    System.Diagnostics.Debug.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());
    RhinoApp.WriteLine("GlimpseAI Debug Mode enabled");
#endif
```

### **Wichtige Log-Pattern**

#### **Thread Violations:**
```
FEHLER: UpdateImage Thread: 15 (should be UI thread)
FEHLER: DisplayMode assignment on background thread
FEHLER: Cross-thread operation not valid
```

#### **Resource Leaks:**
```
WARNING: DisplayBitmap not disposed after 5 seconds
WARNING: WebSocket connection not closed properly  
WARNING: Background task still running during shutdown
```

#### **ComfyUI Communication:**
```
INFO: WebSocket connected successfully
INFO: Workflow queued with ID: abc123
ERROR: ComfyUI server not responding after 30s
ERROR: Image upload failed: 413 Request Entity Too Large
```

### **Rhino Command für Debugging**
```csharp
[System.Runtime.InteropServices.Guid("YOUR-GUID")]
public class GlimpseDebugCommand : Command
{
    public override string EnglishName => "GlimpseDebug";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var orchestrator = GlimpseOrchestrator.Instance;
        
        RhinoApp.WriteLine("=== GlimpseAI Debug Info ===");
        RhinoApp.WriteLine($"UI Thread: {!RhinoApp.InvokeRequired}");
        RhinoApp.WriteLine($"Current Thread: {Thread.CurrentThread.ManagedThreadId}");
        RhinoApp.WriteLine($"Conduit Enabled: {orchestrator.OverlayConduit?.Enabled}");
        RhinoApp.WriteLine($"Active Tasks: {Task.CurrentId}");
        
        return Result.Success;
    }
}
```

---

## 💾 VRAM-Probleme

### **Symptome:**
- ComfyUI "CUDA out of memory" errors
- Extremely slow generation (>5 minutes)
- Rhino becomes unresponsive during generation
- Generated images are corrupted or black

### **VRAM Monitoring:**
```bash
# NVIDIA
nvidia-smi --query-gpu=memory.used,memory.total --format=csv

# AMD  
rocm-smi --showmeminfo vram

# Windows Task Manager → Performance → GPU
```

### **Resolution Limits:**
```csharp
// Add VRAM-based resolution limits
public class ViewportCapture  
{
    private static readonly Dictionary<long, Size> VramLimits = new() {
        { 4L * 1024 * 1024 * 1024, new Size(768, 768) },   // 4GB VRAM
        { 6L * 1024 * 1024 * 1024, new Size(1024, 1024) }, // 6GB VRAM  
        { 8L * 1024 * 1024 * 1024, new Size(1280, 1280) }, // 8GB VRAM
        { 12L * 1024 * 1024 * 1024, new Size(1536, 1536) } // 12GB+ VRAM
    };

    public static Size GetMaxResolution()
    {
        // Query available VRAM and return appropriate limit
        // Fallback to conservative 512x512 for safety
        return new Size(512, 512);
    }
}
```

### **Memory Management:**
```csharp
// Force garbage collection after large operations
public void UpdateImage(byte[] pngData)
{
    // ... image processing
    
    // Clean up immediately for large images
    if (pngData.Length > 1024 * 1024) { // > 1MB
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
```

---

## 🐛 Debug-Tools & Utilities

### **1. Memory Leak Detection**
```csharp
// Add to GlimpseOrchestrator
private readonly WeakReference<DisplayBitmap> _lastBitmapRef = new(null);

public void CheckMemoryLeaks()
{
    if (_lastBitmapRef.TryGetTarget(out var bitmap)) {
        RhinoApp.WriteLine("WARNING: Previous DisplayBitmap not garbage collected!");
    }
    
    GC.Collect();
    var memoryBefore = GC.GetTotalMemory(false);
    GC.WaitForPendingFinalizers(); 
    var memoryAfter = GC.GetTotalMemory(true);
    
    RhinoApp.WriteLine($"Memory cleaned: {(memoryBefore - memoryAfter) / 1024}KB");
}
```

### **2. Thread Safety Validator**
```csharp
public static class ThreadValidator 
{
    public static void AssertUIThread([CallerMemberName] string callerName = "") 
    {
        if (RhinoApp.InvokeRequired) {
            var error = $"THREAD VIOLATION: {callerName} called from background thread {Thread.CurrentThread.ManagedThreadId}";
            System.Diagnostics.Debug.WriteLine(error);
            RhinoApp.WriteLine(error);
            throw new InvalidOperationException(error);
        }
    }
    
    public static void AssertBackgroundThread([CallerMemberName] string callerName = "")
    {
        if (!RhinoApp.InvokeRequired) {
            RhinoApp.WriteLine($"WARNING: {callerName} should run on background thread");
        }
    }
}

// Usage:
public void UpdateImage(byte[] pngData) 
{
    ThreadValidator.AssertUIThread(); // Will throw if violated
    // ...
}
```

### **3. Performance Profiling**
```csharp
public class PerformanceProfiler
{
    private static readonly ConcurrentDictionary<string, List<long>> Timings = new();
    
    public static IDisposable Profile(string operation)
    {
        return new ProfileScope(operation);
    }
    
    private class ProfileScope : IDisposable
    {
        private readonly string _operation;
        private readonly Stopwatch _stopwatch;
        
        public ProfileScope(string operation) {
            _operation = operation;
            _stopwatch = Stopwatch.StartNew();
        }
        
        public void Dispose() {
            _stopwatch.Stop();
            Timings.AddOrUpdate(_operation, 
                new List<long> { _stopwatch.ElapsedMilliseconds },
                (key, list) => { list.Add(_stopwatch.ElapsedMilliseconds); return list; });
        }
    }
    
    public static void PrintStats() {
        foreach (var kvp in Timings) {
            var avg = kvp.Value.Average();
            var max = kvp.Value.Max();
            RhinoApp.WriteLine($"{kvp.Key}: avg={avg:F1}ms, max={max}ms, samples={kvp.Value.Count}");
        }
    }
}

// Usage:
using (PerformanceProfiler.Profile("ViewportCapture")) {
    var image = ViewportCapture.CaptureViewport(viewport, 1024, 1024);
}
```

---

## 🔄 Automatische Recovery

### **ComfyUI Connection Recovery**
```csharp
public class ComfyUIClient 
{
    private int _reconnectAttempts = 0;
    private readonly int _maxReconnectAttempts = 3;
    
    private async Task<bool> EnsureConnection()
    {
        if (_webSocket.State == WebSocketState.Open) return true;
        
        while (_reconnectAttempts < _maxReconnectAttempts) {
            try {
                await ConnectWebSocket();
                _reconnectAttempts = 0; // Reset on success
                return true;
            } catch {
                _reconnectAttempts++;
                await Task.Delay(2000 * _reconnectAttempts); // Exponential backoff
            }
        }
        
        RhinoApp.WriteLine("ComfyUI connection failed, falling back to HTTP mode");
        return false;
    }
}
```

### **Graceful Degradation**
```csharp
public async Task<RenderResult> GenerateImageAsync(RenderRequest request)
{
    try {
        // Primary: WebSocket approach
        return await GenerateViaWebSocket(request);
    } catch (Exception ex1) {
        RhinoApp.WriteLine($"WebSocket generation failed: {ex1.Message}");
        
        try {
            // Fallback: HTTP approach  
            return await GenerateViaHttp(request);
        } catch (Exception ex2) {
            RhinoApp.WriteLine($"HTTP generation failed: {ex2.Message}");
            
            // Final fallback: Return error result
            return new RenderResult {
                IsSuccess = false,
                ErrorMessage = $"Both WebSocket and HTTP failed. WebSocket: {ex1.Message}, HTTP: {ex2.Message}"
            };
        }
    }
}
```

---

## 🚨 Emergency Recovery

### **Plugin Stuck/Frozen**
```
1. Run Rhino Command: GlimpseDebug
2. Check thread info in output
3. If UI thread blocked:
   - Task Manager → End ComfyUI process
   - Restart ComfyUI server
   - Reload GlimpseAI plugin
```

### **Rhino Instability after Crashes**
```
1. Close all Rhino instances completely
2. Clear temp files: %TEMP%/GlimpseAI* 
3. Check ComfyUI logs for errors
4. Restart in this order:
   a) ComfyUI server
   b) Rhino (load minimal scene)
   c) Test GlimpseAI basic function
```

### **Total Reset**
```bash
# Stop all processes
taskkill /f /im Rhino.exe
taskkill /f /im python.exe  # ComfyUI

# Clear state
del %APPDATA%\GlimpseAI\*.json
del %TEMP%\GlimpseAI*

# Restart fresh
# 1. Start ComfyUI server
# 2. Wait for "server started" message  
# 3. Open Rhino
# 4. Run GlimpseSettings → Test Connection
```

---

*Troubleshooting Guide - Maintained by GlimpseAI Development Team*  
*Bei neuen Problemen: Erstelle Issue mit Debug-Output und System-Specs*