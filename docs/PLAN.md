# Glimpse AI – Product & Implementation Plan

> **Codename:** GlimpseAI
> **Erstellt:** 2026-02-08
> **Autor:** Adi + Sentinel
> **Ziel:** Real-time AI Preview Rendering direkt in Rhino 8

---

## 🎯 Vision

Ein Rhino 8 Plugin-Panel das den aktuellen Viewport in Echtzeit durch ein AI-Modell (Flux/SDXL) jagt und ein fotorealistisches Preview-Rendering anzeigt. Wie ein "Magic Window" in dem die 3D-Szene lebendig wird.

**Vergleich:**
| Feature | ScreenShare-Workflow | GlimpseAI |
|---------|---------------------|-------------|
| Input | Screen Capture (mit UI-Noise) | Sauberer Viewport Export |
| Depth Map | LeReS Schätzung (ungenau) | Rhino Depth Buffer (pixel-perfekt) |
| Triggering | Fester Intervall (500ms) | Event-basiert (Kamera-Änderung) |
| Integration | Separates Fenster | Rhino Panel (Eto.Forms) |
| Prompt | Statisch | Auto-generiert aus Materialien/Szene |
| Setup | ComfyUI + ScreenShare Node | Ein-Klick im Plugin |

---

## 🏗️ Architektur

```
┌─────────────────────────────────────────────────────┐
│                    RHINO 8                           │
│                                                     │
│  ┌──────────────┐     ┌──────────────────────────┐ │
│  │  Perspective  │────→│   GlimpseAI Panel      │ │
│  │  Viewport     │     │                          │ │
│  │               │     │  ┌────────────────────┐  │ │
│  │  [3D Scene]   │     │  │  AI Preview Image  │  │ │
│  │               │     │  │                    │  │ │
│  └──────┬───────┘     │  └────────────────────┘  │ │
│         │              │  [Prompt] [Settings ⚙️]  │ │
│         │              └─────────┬────────────────┘ │
│         │                        │                   │
│  ┌──────▼────────────────────────▼───────────────┐  │
│  │           ViewportWatcher Service              │  │
│  │  • Camera Change Detection (Debounce 300ms)    │  │
│  │  • ViewCapture → PNG (512x384)                 │  │
│  │  • Depth Buffer → PNG (512x384)                │  │
│  │  • Material Info → Prompt Enhancement          │  │
│  └──────────────────┬────────────────────────────┘  │
│                     │ TCP/HTTP                       │
└─────────────────────┼───────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────┐
│              ComfyUI API (Windows/localhost)          │
│                                                      │
│  ┌─────────────┐   ┌──────────────┐   ┌──────────┐ │
│  │ ControlNet   │   │  Flux/SDXL   │   │ VAE      │ │
│  │ (Depth+Canny)│──→│  KSampler    │──→│ Decode   │ │
│  └─────────────┘   │  (4 Steps)   │   └────┬─────┘ │
│                     └──────────────┘        │       │
│                                              │       │
│                                     ┌────────▼─────┐│
│                                     │ Output Image  ││
│                                     │ → TCP/HTTP    ││
│                                     └──────────────┘│
└─────────────────────────────────────────────────────┘
```

---

## 📦 Projekt-Struktur

```
GlimpseAI/
├── GlimpseAI.sln
├── src/
│   └── GlimpseAI/
│       ├── GlimpseAI.csproj          (.NET 7.0-windows, RhinoCommon 8.0)
│       ├── GlimpseAIPlugin.cs         Plugin-Registrierung
│       ├── Properties/
│       │   └── AssemblyInfo.cs
│       │
│       ├── Commands/
│       │   ├── OpenAIViewCommand.cs     Rhino Command: AIView
│       │   └── SettingsCommand.cs       Rhino Command: AIViewSettings
│       │
│       ├── UI/
│       │   ├── AIViewPanel.cs           Eto.Forms Panel (Hauptfenster)
│       │   ├── AIPreviewImage.cs        Bild-Anzeige Component
│       │   ├── PromptEditor.cs          Prompt-Eingabe + Presets
│       │   └── SettingsDialog.cs        Einstellungen
│       │
│       ├── Services/
│       │   ├── ViewportWatcher.cs       Kamera-Change Detection + Debounce
│       │   ├── ViewportCapture.cs       ViewCapture + Depth Buffer Export
│       │   ├── ComfyUIClient.cs         HTTP Client für ComfyUI API
│       │   ├── PromptBuilder.cs         Auto-Prompt aus Szene/Materialien
│       │   └── WorkflowManager.cs       ComfyUI Workflow Templates
│       │
│       ├── Models/
│       │   ├── AIViewSettings.cs        Persistente Settings
│       │   ├── RenderRequest.cs         Request DTO
│       │   └── RenderResult.cs          Response DTO
│       │
│       └── Workflows/
│           ├── fast_preview.json        Flux Schnell, 4 Steps (Speed)
│           ├── quality_preview.json     Flux Dev, 12 Steps (Qualität)
│           ├── archviz_exterior.json    SDXL + ControlNet (Architektur)
│           └── archviz_interior.json    Interior-spezifisch
│
├── docs/
│   ├── README.md
│   └── setup-guide.md
│
└── scripts/
    └── install.ps1                      Model-Download Helper
```

---

## 🔧 Komponenten im Detail

### 1. ViewportWatcher (Kern-Service)

```csharp
public class ViewportWatcher : IDisposable
{
    private Timer _debounceTimer;
    private CameraState _lastCameraState;
    private const int DEBOUNCE_MS = 300;
    
    // Events
    public event EventHandler<ViewportChangedEventArgs> ViewportChanged;
    
    public void Start(RhinoDoc doc)
    {
        // RhinoView.Modified Event abonnieren
        RhinoView.Modified += OnViewModified;
    }
    
    private void OnViewModified(object sender, ViewEventArgs e)
    {
        var camera = e.View.ActiveViewport.GetCameraFrame();
        if (CameraHasChanged(camera))
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => 
            {
                ViewportChanged?.Invoke(this, new ViewportChangedEventArgs
                {
                    ViewportName = e.View.ActiveViewport.Name,
                    CameraPosition = camera.Origin,
                    CameraTarget = e.View.ActiveViewport.CameraTarget
                });
            }, null, DEBOUNCE_MS, Timeout.Infinite);
        }
    }
}
```

### 2. ViewportCapture (Bild-Export)

```csharp
public class ViewportCapture
{
    /// <summary>
    /// Capture viewport als RGB Image
    /// </summary>
    public static Bitmap CaptureViewport(RhinoViewport vp, int width = 512, int height = 384)
    {
        var view = vp.ParentView;
        return view.CaptureToBitmap(new Size(width, height));
    }
    
    /// <summary>
    /// Capture Depth Buffer (Z-Buffer) als Grayscale Image
    /// </summary>
    public static Bitmap CaptureDepthBuffer(RhinoViewport vp, int width = 512, int height = 384)
    {
        // Rhino 8 hat DisplayPipeline.CaptureDepthMap()
        // Oder: SetDisplayMode("Rendered") → Arctic/Technical → Depth-like
        // Oder: Custom Display Conduit mit Z-Buffer Access
        
        var settings = new ViewCaptureSettings(vp, new Size(width, height), 72);
        // Depth pass via Custom Conduit oder Rhino's DepthBuffer
        return DepthBufferHelper.CaptureDepth(vp, settings);
    }
}
```

### 3. ComfyUIClient (API Integration)

```csharp
public class ComfyUIClient
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;  // http://localhost:8188
    
    /// <summary>
    /// Upload Bild zu ComfyUI Input
    /// </summary>
    public async Task<string> UploadImage(byte[] imageData, string filename)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(imageData), "image", filename);
        var response = await _client.PostAsync($"{_baseUrl}/upload/image", content);
        var json = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(json)["name"].GetValue<string>();
    }
    
    /// <summary>
    /// Workflow queuen und auf Ergebnis warten
    /// </summary>
    public async Task<byte[]> GenerateAndWait(object workflow, CancellationToken ct)
    {
        // 1. Queue prompt
        var promptId = await QueuePrompt(workflow);
        
        // 2. Poll /history/{promptId} bis fertig
        while (!ct.IsCancellationRequested)
        {
            var status = await GetHistory(promptId);
            if (status.Completed)
            {
                return await DownloadImage(status.OutputFilename, status.Subfolder);
            }
            await Task.Delay(100, ct);
        }
        throw new OperationCanceledException();
    }
}
```

### 4. PromptBuilder (Automatische Prompts)

```csharp
public class PromptBuilder
{
    /// <summary>
    /// Generiert einen Prompt basierend auf der Rhino-Szene
    /// </summary>
    public static string BuildFromScene(RhinoDoc doc, string userPrompt = "")
    {
        var parts = new List<string>();
        
        // Base quality
        parts.Add("masterpiece, best quality, photorealistic, 8k uhd");
        
        // User prompt (höchste Priorität)
        if (!string.IsNullOrEmpty(userPrompt))
            parts.Add(userPrompt);
        
        // Material-basierte Keywords
        var materials = GetSceneMaterials(doc);
        foreach (var mat in materials)
        {
            if (mat.Name.Contains("wood", StringComparison.OrdinalIgnoreCase))
                parts.Add("warm wood textures, natural grain");
            if (mat.Name.Contains("glass", StringComparison.OrdinalIgnoreCase))
                parts.Add("crystal clear glass reflections");
            if (mat.Name.Contains("concrete", StringComparison.OrdinalIgnoreCase))
                parts.Add("exposed concrete, brutalist textures");
            if (mat.Name.Contains("metal", StringComparison.OrdinalIgnoreCase))
                parts.Add("brushed metal surfaces");
        }
        
        // Environment-basierte Keywords
        if (HasGroundPlane(doc))
            parts.Add("landscaped surroundings, professional photography");
        
        // Lighting
        parts.Add("dramatic natural lighting, ambient occlusion, soft shadows");
        
        return string.Join(", ", parts);
    }
}
```

### 5. AIViewPanel (Eto.Forms UI)

```
┌──────────────────────────────────────┐
│ 🤖 AI View               ⚙️ [≡]   │
├──────────────────────────────────────┤
│                                      │
│  ┌──────────────────────────────┐   │
│  │                              │   │
│  │     AI Rendered Preview      │   │
│  │         (Live Image)         │   │
│  │                              │   │
│  └──────────────────────────────┘   │
│                                      │
│  Prompt:                             │
│  ┌──────────────────────────────┐   │
│  │ modern architecture, warm... │   │
│  └──────────────────────────────┘   │
│                                      │
│  Preset: [Exterior ▼]               │
│  Quality: ○ Fast  ● Balanced  ○ HQ  │
│  Strength: [====|========] 0.65     │
│                                      │
│  [▶ Generate]  [⏸ Auto]  [💾 Save] │
│                                      │
│  ℹ️ 1.3s | 512x384 | Flux Schnell  │
└──────────────────────────────────────┘
```

---

## ⚡ Workflow-Presets

### Fast Preview (Flux Schnell, ~1-2s)
- Model: `flux1-dev-fp8` mit LCM
- Steps: 4
- Resolution: 512x384
- ControlNet: Depth (Strength 0.7)
- Denoise: 0.65
- **Use Case:** Live-Preview beim Navigieren

### Balanced (Flux Dev, ~5-8s)
- Model: `flux1-dev-fp8`
- Steps: 12
- Resolution: 768x576
- ControlNet: Depth + Canny
- Denoise: 0.55
- **Use Case:** Schneller Check einer Perspektive

### High Quality (SDXL Architecture, ~20-30s)
- Model: `dvarchMultiPrompt_dvarchExterior`
- Steps: 30
- Resolution: 1024x768
- ControlNet: Depth + Canny + Normal
- Denoise: 0.45
- **Use Case:** Finale Preview-Renderings

### 4K Export (HQ + UltraSharp, ~45-60s)
- Wie High Quality
- + 4x UltraSharp Upscale
- Output: 4096x3072
- **Use Case:** Präsentations-Rendering

---

## 🔑 Technische Entscheidungen

### Depth Buffer Capture
**Option A: RhinoCommon DepthBuffer (empfohlen)**
- `RhinoViewport.GetDepthBuffer()` existiert in Rhino 8
- Pixel-perfekt, keine Schätzung
- Muss in Grayscale konvertiert werden (Near=Weiss, Far=Schwarz)

**Option B: Display Mode Trick**
- Arctic Mode rendern → gibt natürliches Depth-like Image
- Einfacher, aber weniger präzise

**Option C: Custom Display Conduit**
- Eigener Shader der Z-Buffer ausgibt
- Maximale Kontrolle, mehr Aufwand

→ **Starte mit Option B** (schnell), upgrade zu Option A wenn nötig.

### Communication: Rhino → ComfyUI
**Direkt HTTP** (empfohlen)
- ComfyUI läuft auf localhost:8188
- Rhino Plugin macht direkte HTTP Calls
- Kein Middleware nötig
- `HttpClient` in C# ist perfekt dafür

### Image Transfer
- Viewport Capture → `MemoryStream` → Upload via MultipartFormData
- Kein Filesystem-Roundtrip nötig (schneller!)
- Result: Download via HTTP → `Bitmap` → Panel anzeigen

---

## 📋 Implementation Roadmap

### Phase 1: MVP – Static Generate (1-2 Abende)
- [ ] Neues Plugin Projekt (wie RhinoAssemblyOutliner)
- [ ] Basic Panel mit Image Display
- [ ] "Generate" Button: Viewport Capture → ComfyUI → Result anzeigen
- [ ] Ein fester Workflow (Fast Preview)
- [ ] Prompt-Eingabe

### Phase 2: Live Preview (1-2 Abende)
- [ ] ViewportWatcher mit Debounce
- [ ] Auto-Generate bei Kamera-Änderung
- [ ] Toggle: Manual / Auto Mode
- [ ] Depth Buffer Export (Option B zuerst)
- [ ] ControlNet Integration (Depth)

### Phase 3: Smart Features (1-2 Abende)
- [ ] PromptBuilder: Auto-Prompt aus Materialien
- [ ] Preset-System (Fast/Balanced/HQ/4K)
- [ ] Denoise Strength Slider
- [ ] Save-Button (4K Export)
- [ ] Settings Dialog (ComfyUI URL, Model-Auswahl)

### Phase 4: Polish & Product (1-2 Abende)
- [ ] ControlNet: Depth + Canny + Normal kombinieren
- [ ] Proper Depth Buffer (Option A)
- [ ] Cancel laufende Generation bei neuer Kamera-Änderung
- [ ] Progress Indicator
- [ ] Keyboard Shortcut
- [ ] README + Food4Rhino Page vorbereiten
- [ ] Demo-Video aufnehmen

---

## 🎯 Minimum Viable Product (Phase 1)

Das absolute Minimum für einen ersten Wow-Moment:
1. Panel öffnen
2. Prompt eingeben (oder Default)
3. "Generate" klicken
4. Viewport wird captured → an ComfyUI geschickt → Rendering erscheint im Panel

**Das allein ist schon beeindruckend** und zeigt das Konzept.

---

## 💰 Produkt-Potenzial

### Food4Rhino Listing
- **Name:** GlimpseAI – AI Preview Rendering
- **Kategorie:** Rendering / Visualization
- **Preis:** Free (Basic) / $29-49 Pro (HQ Presets, 4K Export, Prompt-Bibliothek)
- **Zielgruppe:** Architekten, Interior Designer, Industrie-Designer

### Alleinstellungsmerkmale
1. **Einziges** Rhino-Plugin mit direkter AI-Rendering Integration
2. Nutzt Rhino's Depth Buffer (nicht LeReS Schätzung)
3. Material-aware Prompt Generation
4. Ein-Klick von 3D-Modell zu fotorealistischem Rendering
5. Lokal auf eigener GPU – keine Cloud-Kosten

### Vergleich mit Konkurrenz
| | GlimpseAI | Veras (AI Render) | Stable Diffusion Manual |
|---|---|---|---|
| Integration | Rhino Panel | Revit Plugin | Extern |
| Preis | $29-49 einmalig | $30/Monat | Kostenlos |
| GPU | Lokal (keine Kosten) | Cloud ($$$) | Lokal |
| Depth Map | Pixel-perfekt | Unbekannt | LeReS Schätzung |
| Speed | 1-2s Fast Preview | ~10s | Manuell |
| Auto-Prompt | ✅ aus Materialien | ❌ | ❌ |

---

## ⚠️ Risiken & Mitigations

1. **VRAM:** Flux + ControlNet braucht ~16GB → RTX 3090 hat 24GB, passt
2. **Latenz:** Erste Generation braucht Model-Load (~5s), danach cached
3. **Rhino Depth Buffer:** API könnte limitiert sein → Fallback auf Arctic Mode
4. **ComfyUI Dependency:** User muss ComfyUI installiert haben → Setup-Guide + Installer-Script
5. **Rhino 8 only:** Eto.Forms Panels + .NET 7 = Rhino 8 Minimum
