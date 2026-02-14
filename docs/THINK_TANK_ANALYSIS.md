# GlimpseAI Think Tank Analysis

**Date:** February 14, 2026  
**Analyst:** AI Code Reviewer  
**Scope:** Complete analysis of C# codebase under `src/GlimpseAI/`

## Executive Summary

GlimpseAI is a Rhino Plugin for real-time AI preview rendering using ComfyUI. The architecture consists of:
- **UI Layer:** GlimpsePanel (WinForms), GlimpseSettingsDialog
- **Service Layer:** GlimpseOrchestrator (coordination), ComfyUIClient (API), ViewportWatcher (camera detection)
- **Rendering:** GlimpseOverlayConduit (viewport overlay), ViewportCapture (image capture)
- **Workflow:** WorkflowBuilder (ComfyUI workflow generation)

**Critical Finding:** Multiple P0 thread-safety issues could cause crashes. Several architectural problems (P1) impact stability and performance.

---

## P0 Issues - CRASH-CAUSING (Fix Immediately)

### 1. **Thread-Safety in GlimpseOverlayConduit** ⚠️ CRITICAL
**File:** `Services/GlimpseOverlayConduit.cs`  
**Issue:** `DrawForeground()` accesses `_displayBitmap` from UI thread while `UpdateImage()` modifies it from background threads.

**Code Analysis:**
```csharp
// PROBLEM: Race condition here
protected override void DrawForeground(DrawEventArgs e)
{
    lock (_lock)  // ✅ Lock is present
    {
        if (_displayBitmap == null) return;  // ❌ Could become null between check and usage
        // ... uses _displayBitmap
    }
}

public void UpdateImage(byte[] pngData)
{
    lock (_lock)
    {
        _displayBitmap?.Dispose();     // ❌ Disposes while UI thread might be using it
        _displayBitmap = new DisplayBitmap(bitmap);  // ❌ Creates new while UI renders
    }
}
```

**Risk:** Rhino crashes with "AccessViolationException" or "ObjectDisposedException" during viewport redraw.

**Fix:** Implement proper double-buffering with safe swapping.

---

### 2. **System.Drawing.Bitmap in DisplayConduit** ⚠️ CRITICAL
**File:** `Services/GlimpseOverlayConduit.cs`  
**Issue:** Uses `System.Drawing.Bitmap` in Rhino's rendering pipeline. Rhino uses `Eto.Drawing` internally.

**Code Analysis:**
```csharp
public void UpdateImage(byte[] pngData)
{
    using var ms = new MemoryStream(pngData);
    var bitmap = new System.Drawing.Bitmap(ms);  // ❌ System.Drawing in Rhino pipeline
    _displayBitmap = new DisplayBitmap(bitmap);
}
```

**Risk:** Memory corruption, GDI+ exceptions, viewport rendering failures.

**Fix:** Ensure proper bitmap lifecycle management and consider Eto.Drawing compatibility.

---

### 3. **ViewportCapture DisplayMode Race Condition** ⚠️ CRITICAL
**File:** `Services/ViewportCapture.cs`  
**Issue:** `CaptureDepthApprox()` changes DisplayMode during active rendering without proper synchronization.

**Code Analysis:**
```csharp
public static byte[] CaptureDepthApprox(RhinoViewport viewport, ...)
{
    var originalMode = viewport.DisplayMode;
    try
    {
        viewport.DisplayMode = arcticMode;  // ❌ No render sync
        view.Redraw();                      // ❌ Immediate redraw
        var bitmap = view.CaptureToBitmap(...);  // ❌ Could capture mid-transition
    }
    finally
    {
        viewport.DisplayMode = originalMode;  // ❌ Restore without sync
    }
}
```

**Risk:** Rhino crashes when DisplayMode changes during active rendering operations.

**Fix:** Wait for render completion before mode changes, add proper error handling.

---

### 4. **Timer-based Debounce Thread Safety** ⚠️ HIGH
**File:** `Services/ViewportWatcher.cs`  
**Issue:** Timer callback executes on ThreadPool thread, invokes event handlers that may execute UI code.

**Code Analysis:**
```csharp
private void OnDebounceElapsed(string viewportName, Point3d cameraPos, Point3d cameraTarget)
{
    // ❌ Running on ThreadPool thread
    ViewportChanged?.Invoke(this, new ViewportChangedEventArgs { ... });
}

// In GlimpseOrchestrator.OnViewportChanged():
private void OnViewportChanged(object sender, ViewportChangedEventArgs e)
{
    // ❌ UI marshalling required but not enforced
    Task.Run(async () => { ... });
}
```

**Risk:** Cross-thread operations, UI updates on wrong thread, race conditions.

**Fix:** Marshal Timer callbacks to UI thread before invoking events.

---

### 5. **WebSocket Buffer Overflow** ⚠️ HIGH
**File:** `Services/ComfyUIClient.cs`  
**Issue:** Fixed 4MB buffer for preview images. Large images (1024x768+ JPEG) can overflow.

**Code Analysis:**
```csharp
private async Task<RenderResult> WaitForCompletionWebSocketAsync(...)
{
    var buffer = new byte[4 * 1024 * 1024]; // ❌ Fixed 4MB buffer
    
    wsResult = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
    // ❌ No check if message > buffer size
}
```

**Risk:** Buffer overflow, corrupted preview images, WebSocket connection drops.

**Fix:** Dynamic buffer allocation or streaming receive for large messages.

---

## P1 Issues - ARCHITECTURAL PROBLEMS (Fix After P0)

### 1. **DisplayBitmap Memory Waste** 
**File:** `Services/GlimpseOverlayConduit.cs`  
**Issue:** Creates new `DisplayBitmap` on every `UpdateImage()` instead of reusing.

**Impact:** Memory pressure, GC overhead, frame drops during rapid updates.
**Fix:** Implement bitmap pooling/reuse strategy.

---

### 2. **DrawSprite Width Parameter Error** 
**File:** `Services/GlimpseOverlayConduit.cs`  
**Issue:** `DrawSprite()` width parameter likely incorrect.

**Code Analysis:**
```csharp
e.Display.DrawSprite(
    _displayBitmap,
    new Point2d(vpWidth / 2.0, vpHeight / 2.0),  // Center point
    drawWidth,  // ❌ Should this be drawWidth/2 for radius?
    System.Drawing.Color.FromArgb(alpha, 255, 255, 255));
```

**Fix:** Verify Rhino API documentation for correct `DrawSprite` parameters.

---

### 3. **No Cancel Mechanism for ComfyUI Generation** 
**File:** `Services/ComfyUIClient.cs`  
**Issue:** Can cancel client-side waiting, but ComfyUI server continues processing.

**Impact:** Resource waste, server congestion, slower subsequent requests.
**Fix:** Implement ComfyUI `/interrupt` API call on cancellation.

---

### 4. **Task.Run with Async Void Pattern** 
**File:** `Services/GlimpseOrchestrator.cs`  
**Issue:** `OnViewportChanged()` uses `Task.Run(async () => ...)` without proper error handling.

**Code Analysis:**
```csharp
Task.Run(async () =>  // ❌ Fire-and-forget with async lambda
{
    // Exception here won't be caught
    await GenerateFromCaptureAsync(...);
});
```

**Fix:** Use explicit task scheduling with error handling.

---

### 5. **No WebSocket Reconnect Logic** 
**File:** `Services/ComfyUIClient.cs`  
**Issue:** WebSocket disconnect requires manual reconnection.

**Impact:** Falls back to HTTP polling permanently until plugin reload.
**Fix:** Implement automatic reconnection with exponential backoff.

---

## P2 Issues - MISSING FEATURES (Future Enhancement)

### 1. **No Error Recovery After Crash**
**Impact:** Plugin becomes unusable after any unhandled exception.
**Fix:** Implement circuit breaker pattern and graceful degradation.

### 2. **Insufficient Logging**
**Impact:** Debugging issues is difficult; only `RhinoApp.WriteLine()` available.
**Fix:** Implement structured logging with file output and log levels.

### 3. **Settings Not Updated on Preset Change**
**File:** `Services/GlimpseOrchestrator.cs`  
**Issue:** `UpdateAutoSettings()` exists but auto-mode doesn't pick up new preset settings.
**Fix:** Wire up preset changes to update auto-mode parameters.

---

## Security Analysis

### Data Exposure
- ✅ No sensitive data in viewport captures
- ✅ Local ComfyUI communication (localhost)
- ⚠️ No HTTPS validation if ComfyUI URL is external

### Input Validation
- ⚠️ No validation of ComfyUI responses (JSON parsing)
- ⚠️ No size limits on image uploads
- ✅ Proper HTTP timeout handling

---

## Performance Analysis

### Memory Usage
- ❌ **High:** DisplayBitmap recreation on every update
- ❌ **High:** No image size limits (could allocate GB)
- ✅ **Good:** Proper disposal in most paths

### Rendering Performance
- ✅ **Good:** Uses `DrawSprite()` for hardware acceleration
- ❌ **Poor:** Multiple bitmap allocations per frame
- ⚠️ **Risk:** No frame rate limiting

### Network Performance  
- ✅ **Good:** WebSocket for real-time updates
- ✅ **Good:** HTTP fallback mechanism
- ⚠️ **Risk:** No request queuing (rapid-fire requests)

---

## Recommended Fix Priority

### Phase 1 - Critical Stability (P0)
1. Fix thread-safety in `GlimpseOverlayConduit` 
2. Add proper disposal and error handling in `ViewportCapture`
3. Marshal Timer callbacks to UI thread in `ViewportWatcher`
4. Implement dynamic WebSocket buffering
5. Add comprehensive error handling throughout

### Phase 2 - Architecture Improvements (P1)  
1. Implement bitmap pooling/reuse
2. Fix `DrawSprite` parameter usage
3. Add ComfyUI generation cancellation
4. Improve async error handling
5. Add WebSocket reconnect logic

### Phase 3 - Enhanced Features (P2)
1. Add structured logging system
2. Implement error recovery mechanisms  
3. Fix auto-mode settings updates
4. Add performance monitoring
5. Implement security hardening

---

## Testing Strategy

### Unit Tests Needed
- `ViewportWatcher` debounce logic
- `WorkflowBuilder` workflow generation
- `GlimpseSettings` serialization

### Integration Tests Needed  
- ComfyUI API communication
- WebSocket message handling
- Viewport capture under different display modes

### Stress Tests Needed
- Rapid viewport changes (auto-mode)
- Large image handling (4K export)
- Extended operation (memory leaks)

---

## Conclusion

GlimpseAI has solid core architecture but **requires immediate P0 fixes** to prevent crashes in production. The thread-safety issues in `GlimpseOverlayConduit` and race conditions in `ViewportCapture` pose the highest risk.

**Estimated Fix Effort:**
- **P0 Fixes:** 2-3 days  
- **P1 Fixes:** 3-4 days
- **P2 Features:** 1-2 weeks

**Recommendation:** Implement P0 fixes immediately before any production deployment.