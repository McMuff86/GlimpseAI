# GlimpseAI Plugin Architecture
**Version:** 1.0  
**Datum:** 14.02.2026  
**Status:** Nach Threading-Fixes (fix/stability-and-crashes)

---

## 🏗️ Überblick

GlimpseAI ist ein Rhino 3D Plugin das eine AI-gestützte Echtzeitvorschau von Viewport-Inhalten bietet. Die Architektur folgt einem **event-driven, async/await Pattern** mit klarer Trennung zwischen UI, Orchestration und Backend-Services.

### Kernziele
- **Echtzeit-Preview**: Live-Generierung basierend auf Viewport-Änderungen
- **Non-blocking UI**: Alle AI-Operationen laufen asynchron
- **Flexible Workflows**: Modularer Workflow-Builder für verschiedene AI-Presets
- **Rhino-Integration**: Native Viewport-Overlay ohne UI-Störungen

---

## 📊 Komponentendiagramm

```
┌─────────────────────────────────────────────────────────────────┐
│                    RHINO 3D HOST PROCESS                       │
├─────────────────────────────────────────────────────────────────┤
│  UI LAYER (UI Thread)                                          │
│  ┌─────────────────┐  ┌─────────────────────────────────────┐   │
│  │   GlimpsePanel  │  │    GlimpseSettingsDialog            │   │
│  │   - Preview UI  │  │    - Configuration                  │   │
│  │   - Controls    │  │    - Preset Management             │   │
│  └─────────────────┘  └─────────────────────────────────────┘   │
│           │                           │                         │
│           ▼                           ▼                         │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │              ORCHESTRATION LAYER                           │   │
│  │  ┌─────────────────────────────────────────────────────────┐│   │
│  │  │           GlimpseOrchestrator (Singleton)              ││   │
│  │  │  - Event handling & coordination                       ││   │
│  │  │  - Async workflow execution                            ││   │
│  │  │  - Thread marshalling                                  ││   │
│  │  │  - State management                                    ││   │
│  │  └─────────────────────────────────────────────────────────┘│   │
│  └─────────────────┬─────────────┬─────────────┬───────────────┘   │
│                    │             │             │                   │
│  SERVICE LAYER     ▼             ▼             ▼                   │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐   │
│  │ ViewportWatcher │  │ ViewportCapture │  │GlimpseOverlayConduit│   │
│  │ - Change detect │  │ - Screenshot    │  │ - Result display    │   │
│  │ - Events        │  │ - Depth capture │  │ - Viewport overlay  │   │
│  └─────────────────┘  └─────────────────┘  └─────────────────────┘   │
│           │                     │                      │           │
├───────────┼─────────────────────┼──────────────────────┼───────────┤
│  WORKFLOW LAYER                 │                      │           │
│  ┌─────────────────┐            │                      │           │
│  │ WorkflowBuilder │            │                      │           │
│  │ - JSON templates│            │                      │           │
│  │ - Preset configs│            │                      │           │
│  └─────────────────┘            │                      │           │
│           │                     │                      │           │
├───────────┼─────────────────────┼──────────────────────┼───────────┤
│  BACKEND LAYER                  │                      │           │
│  ┌─────────────────┐            │                      │           │
│  │  ComfyUIClient  │◄───────────┴──────────────────────┘           │
│  │ - WebSocket API │                                                │
│  │ - HTTP requests │                                                │
│  │ - Progress track│                                                │
│  └─────────────────┘                                                │
│           │                                                        │
└───────────┼────────────────────────────────────────────────────────┘
            │
        ┌───▼─────┐
        │ ComfyUI │  (External Process)
        │ Server  │
        └─────────┘
```

---

## 🔄 Datenfluss

### 1. **Viewport-Änderung Detection**
```
ViewportWatcher → OnViewportChanged Event → GlimpseOrchestrator
```

### 2. **Capture & Processing Pipeline**
```
GlimpseOrchestrator
  ├─ Invoke UI Thread → ViewportCapture
  │   ├─ Capture RGB Screenshot
  │   └─ Capture Depth (Arctic Mode)
  ├─ Background Thread → WorkflowBuilder 
  │   └─ Build ComfyUI JSON Workflow
  └─ Background Thread → ComfyUIClient
      ├─ WebSocket connection
      ├─ Upload images
      ├─ Queue workflow
      └─ Monitor progress
```

### 3. **Result Display**
```
ComfyUIClient → OnPreviewImage Event → GlimpseOrchestrator
  └─ UI Thread Invoke → GlimpseOverlayConduit
      └─ Update DisplayBitmap → Viewport Redraw
```

---

## 🧵 Thread-Modell

### **UI Thread** (Rhino Main Thread)
- **Zuständig für:**
  - Viewport-Operationen (`ViewportCapture`)
  - DisplayBitmap Creation/Disposal (`GlimpseOverlayConduit`)
  - UI-Updates (`GlimpsePanel`)
  - Rhino API calls

- **Threading-Regel:** 
  - Alle Rhino API calls NUR auf UI Thread
  - `RhinoApp.InvokeOnUiThread()` für Thread-Marshalling

### **Background Threads** (TaskPool)
- **Zuständig für:**
  - ComfyUI HTTP/WebSocket Kommunikation
  - Workflow-Building (JSON Generation)
  - Image Processing (non-Rhino operations)
  - File I/O operations

- **Threading-Regel:**
  - Keine Rhino API calls
  - Keine DisplayBitmap manipulation
  - Results über Events zurück an UI Thread

### **Thread-Safety Mechanisms**
```csharp
// Dual-locking pattern in GlimpseOverlayConduit
private readonly object _lock = new object();
private volatile bool _disposed = false;

// UI Thread marshalling
RhinoApp.InvokeOnUiThread(() => {
    // UI operations only
});

// Background processing
Task.Run(async () => {
    // Background work
    // → Event → UI Thread for results
});
```

---

## ⚡ Event-Wiring

### **Core Events**
```csharp
// Viewport Changes
ViewportWatcher.ViewportChanged 
  → GlimpseOrchestrator.OnViewportChanged()

// AI Processing Results  
ComfyUIClient.PreviewImageReceived
  → GlimpseOrchestrator.OnPreviewImageFromComfy()

ComfyUIClient.ProgressUpdated
  → GlimpseOrchestrator.OnProgressUpdate()
  → GlimpsePanel.UpdateProgress()

// User Interactions
GlimpsePanel.GenerateRequested
  → GlimpseOrchestrator.GenerateFromCaptureAsync()

GlimpsePanel.PresetChanged
  → GlimpseOrchestrator.UpdateActivePreset()
```

### **Event Threading Model**
- **Events ausgelöst:** Background Threads
- **Event Handler:** UI Thread (via `InvokeOnUiThread`)
- **Async Events:** Non-blocking mit CancellationToken

---

## 🏛️ Architektur-Pattern

### **1. Singleton Orchestrator**
```csharp
public class GlimpseOrchestrator 
{
    private static readonly Lazy<GlimpseOrchestrator> _instance;
    
    // Central coordination point
    // State management
    // Event routing
}
```

### **2. Observer Pattern**
- ViewportWatcher observiert Rhino Events
- UI Components observieren Orchestrator Events  
- Loose coupling zwischen Komponenten

### **3. Strategy Pattern**
- WorkflowBuilder: Verschiedene Preset-Strategien
- ComfyUIClient: WebSocket vs HTTP fallback
- ViewportCapture: Normal vs Depth capture

### **4. Command Pattern**
- GlimpseCommand, GlimpseSettingsCommand
- Rhino-konforme Command-Implementierung

---

## 💾 Datenmodelle

### **Core Models**
```csharp
// Configuration
public class GlimpseSettings
{
    public string ComfyUIUrl { get; set; }
    public PresetType ActivePreset { get; set; }
    public bool AutoGenerateEnabled { get; set; }
    // ...
}

// Processing Pipeline
public class RenderRequest
{
    public byte[] RgbImage { get; set; }
    public byte[] DepthImage { get; set; }
    public PresetType Preset { get; set; }
    public string PositivePrompt { get; set; }
    public string NegativePrompt { get; set; }
}

public class RenderResult  
{
    public byte[] GeneratedImage { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorMessage { get; set; }
}
```

---

## 🔌 Rhino Integration Points

### **Plugin Lifecycle**
```csharp
GlimpseAIPlugin : Rhino.PlugIns.PlugIn
├─ OnLoad() → Initialize Services
├─ OnShutdown() → Cleanup Resources  
└─ Commands Registration
```

### **Display Pipeline Integration**
```csharp  
GlimpseOverlayConduit : DisplayConduit
├─ DrawForeground() → Render AI overlay
├─ CalculateBoundingBox() → Set draw region
└─ Enabled property → Control visibility
```

### **Viewport Events**
- `RhinoView.ViewportChanged` 
- `RhinoDoc.ViewTableEvent`
- Custom debouncing (500ms) für Performance

---

## ⚠️ Bekannte Limitierungen

### **1. Threading Constraints**
- **Rhino API Thread-Safety:** Alle Rhino calls nur auf UI Thread
- **DisplayBitmap Lifecycle:** Creation/Disposal nur auf UI Thread
- **Viewport Manipulation:** UI Thread only

### **2. Performance Bottlenecks** 
- **Arctic Mode Switching:** Viewport flicker während depth capture
- **Large Image Processing:** Memory pressure bei hohen Auflösungen
- **Network Latency:** ComfyUI response times

### **3. Fehlerbehandlung**
- **ComfyUI Offline:** Graceful degradation implementiert
- **WebSocket Drops:** Automatic HTTP fallback  
- **Memory Limits:** DisplayBitmap disposal critical

### **4. UI Integration**
- **Panel Lifecycle:** Rhino panel management quirks
- **Settings Persistence:** Limited to Rhino settings storage
- **Cross-Platform:** Windows-focused DisplayBitmap implementation

---

## 🔄 Verbesserungsvorschläge

### **Kurzfristig**
1. **Separate Depth Capture Viewport** → Kein mode switching
2. **Async UI Updates** → Non-blocking progress display  
3. **Memory Pooling** → Reduce GC pressure
4. **Better Error Recovery** → Automatic retry mechanisms

### **Mittelfristig**
5. **Background Processing Queue** → Multiple concurrent requests
6. **Result Caching** → Avoid redundant generations
7. **Custom Display Mode** → No arctic mode dependency
8. **Cross-Platform Display** → OpenTK/SkiaSharp backends

### **Langfristig** 
9. **Plugin-SDK Integration** → Rhino 8+ features
10. **Real-time Streaming** → Live viewport AI processing
11. **Multi-AI Backend Support** → Not just ComfyUI
12. **Cloud Processing Options** → Scalable AI compute

---

## 📚 Related Documentation

- **[TROUBLESHOOTING.md](TROUBLESHOOTING.md)** → Debug guides & common issues
- **[SETUP.md](SETUP.md)** → Installation & configuration  
- **[PLAN.md](PLAN.md)** → Development roadmap
- **[THINK_TANK_ANALYSIS.md](THINK_TANK_ANALYSIS.md)** → Technical deep-dive

---

*Architektur-Dokumentation gepflegt vom GlimpseAI Development Team*