# 👁️ Glimpse AI

**Real-time AI Preview Rendering for Rhino 8.**

Transform your 3D viewport into photorealistic renderings instantly.  
Powered by Stable Diffusion and Flux via ComfyUI.

![Rhino 8](https://img.shields.io/badge/Rhino-8-blue)
![.NET 7](https://img.shields.io/badge/.NET-7.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

---

## ✨ Features

### ✅ **Working (Stable)**
- **One-Click Rendering** – Generate AI renderings from any Rhino viewport
- **Multiple Presets** – Fast (1-2s), Balanced (5-8s), High Quality (20-30s), 4K Export (45-60s)
- **Local Processing** – Runs on your GPU, no cloud costs, no subscriptions
- **Rhino Integration** – Native dockable panel via Eto.Forms
- **WebSocket + HTTP Fallback** – Robust ComfyUI communication
- **Real-time Overlay** – AI results displayed directly in viewport
- **Thread-Safe Architecture** – No more crashes from UI thread violations

### 🚧 **In Development** 
- **Live Preview** – Auto-generates when you move the camera *(viewport watcher implemented, needs stability testing)*
- **Smart Prompts** – Auto-generates prompts from scene materials *(planned)*
- **Advanced Depth Processing** – Enhanced depth capture without display mode switching *(in progress)*

### 🔬 **Experimental**
- **Depth-Aware Generation** – Uses viewport depth via Arctic mode *(functional but can cause viewport flickering)*

---

## ⚠️ Known Issues

### **Critical (Fixed in fix/stability-and-crashes branch)**
- ~~Thread-safety violations causing Rhino crashes~~ ✅ **FIXED**
- ~~DisplayBitmap creation on background threads~~ ✅ **FIXED** 
- ~~Viewport manipulation from non-UI threads~~ ✅ **FIXED**

### **Minor Issues**
- **Depth Capture Flickering** – Arctic mode switching can cause brief viewport flicker
- **Large Image Memory** – High-resolution captures (>2K) may cause memory pressure
- **ComfyUI Offline** – Plugin doesn't gracefully handle server disconnection yet

### **Performance Notes**
- **VRAM Requirements** – 4K presets need 8GB+ VRAM to avoid OOM errors
- **First Generation Slow** – Model loading causes ~10s delay on first run
- **Background Processing** – Multiple rapid viewport changes may queue requests

> See [TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) for detailed fixes and workarounds.

---

## 📋 Requirements

| Component | Minimum |
|-----------|---------|
| **Rhino** | 8 (Windows) |
| **ComfyUI** | Latest (running with `--listen 0.0.0.0`) |
| **GPU** | 12 GB+ VRAM (RTX 3060 or better) |
| **.NET** | 7.0 (ships with Rhino 8) |

### Recommended Models

Install these via ComfyUI Manager or download manually into `ComfyUI/models/checkpoints/`:

| Preset | Model | Use Case |
|--------|-------|----------|
| **Fast** | DreamShaper XL Turbo | Live preview while navigating (~1-2s) |
| **Balanced** | Juggernaut XL Lightning | Quick perspective checks (~5-8s) |
| **High Quality** | dvArch Exterior | Final preview renderings (~20-30s) |
| **4K Export** | dvArch Exterior + 4x-UltraSharp | Presentation renderings (~45-60s) |

The 4x-UltraSharp upscaler model goes into `ComfyUI/models/upscale_models/`.

---

## 🚀 Installation

### Build from Source

1. **Clone the repository:**

   ```bash
   git clone https://github.com/McMuff86/GlimpseAI.git
   cd GlimpseAI
   ```

2. **Build the plugin:**

   ```bash
   dotnet build src/GlimpseAI/GlimpseAI.csproj -c Release
   ```

3. **Install the plugin:**

   Copy the built `.rhp` file to your Rhino plugins folder:

   ```
   %APPDATA%\McNeel\Rhinoceros\8.0\Plug-ins\
   ```

   Or drag-and-drop the `.rhp` file onto an open Rhino window.

4. **Verify installation:**

   In Rhino, type `Glimpse` in the command line. The Glimpse AI panel should appear.

### Prerequisites

Make sure ComfyUI is running before using the plugin. See [`docs/SETUP.md`](docs/SETUP.md) for a detailed setup guide.

---

## 🎮 Usage

### Quick Start

1. **Start ComfyUI** on your machine (default: `http://localhost:8188`)
2. **Open Rhino 8** and load or create a 3D scene
3. Type **`Glimpse`** in the command line to open the panel
4. Enter a **prompt** (or use the auto-generated default)
5. Click **Generate** – your viewport becomes a photorealistic rendering!

### Commands

| Command | Description |
|---------|-------------|
| `Glimpse` | Toggle the Glimpse AI panel (open/close) |
| `GlimpseSettings` | Open the settings dialog |

### Live Preview Mode

Enable **Auto-generate** in settings to automatically render whenever you move the camera. The watcher uses smart debouncing (default: 300ms) – it waits until you stop navigating before sending a new request.

### Saving Results

Use the **4K Export** preset to generate high-resolution (4096×3072) images suitable for presentations and client deliverables.

---

## ⚙️ Configuration

Open settings via the `GlimpseSettings` command or the gear icon in the panel.

| Setting | Default | Description |
|---------|---------|-------------|
| **ComfyUI URL** | `http://localhost:8188` | ComfyUI server address |
| **Default Preset** | Fast | Quality/speed tradeoff |
| **Default Prompt** | `modern architecture...` | Starting prompt for generation |
| **Denoise Strength** | 0.65 | How much the AI changes the image (0.0–1.0) |
| **Auto-Generate** | Off | Auto-render on camera movement |
| **Debounce** | 300ms | Delay before auto-generating |
| **Capture Resolution** | 512×384 | Viewport capture size |

### Denoise Strength Guide

- **0.3–0.4** – Subtle enhancement, keeps scene very close to original
- **0.5–0.6** – Balanced – adds realism while maintaining structure
- **0.65–0.75** – Creative – more AI interpretation (default)
- **0.8–1.0** – Heavy transformation, may lose original composition

---

## 📐 Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                       RHINO 8 HOST                             │
├─────────────────────────────────────────────────────────────────┤
│  UI LAYER (UI Thread)                                          │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────────────────────┐ │
│  │ GlimpsePanel│  │ViewportCapture│  │GlimpseOverlayConduit   │ │
│  │ - Controls  │  │ - Screenshots │  │ - AI Result Display    │ │
│  │ - Preview   │  │ - Depth Mode  │  │ - Viewport Overlay     │ │
│  └─────┬───────┘  └──────┬───────┘  └──────┬──────────────────┘ │
│        │                 │                 │                    │
│        ▼                 ▼                 ▼                    │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │              GlimpseOrchestrator                            │ │
│  │         (Event-Driven Coordinator)                         │ │
│  │  ┌─────────────┐ ┌──────────────┐ ┌────────────────────┐   │ │
│  │  │ViewportWatch│ │WorkflowBuild │ │ Thread Marshalling │   │ │
│  │  │- Events     │ │- JSON Presets│ │ - UI ↔ Background  │   │ │
│  │  └─────────────┘ └──────────────┘ └────────────────────┘   │ │
│  └─────────────────────┬───────────────────────────────────────┘ │
│                        │ WebSocket/HTTP                          │
└────────────────────────┼─────────────────────────────────────────┘
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                   ComfyUIClient                                 │
│  ┌─────────────┐    ┌─────────────┐    ┌──────────────────────┐ │
│  │  WebSocket  │    │    HTTP     │    │   Progress Monitor   │ │
│  │- Real-time  │    │- Fallback   │    │ - Queue Status      │ │
│  │- Progress   │    │- Upload     │    │ - Error Recovery    │ │
│  └─────────────┘    └─────────────┘    └──────────────────────┘ │
└─────────────────────┬───────────────────────────────────────────┘
                      ▼
┌─────────────────────────────────────────────────────────────────┐
│              ComfyUI Server (External)                         │
│  Workflow: LoadImage → ControlNet → KSampler → SaveImage       │
└─────────────────────────────────────────────────────────────────┘
```

> **Thread-Safety:** All Rhino API calls happen on UI Thread. Background threads handle only ComfyUI communication and workflow building.

> **Full Architecture:** See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed component breakdown and data flow.

### Key Components

| Component | File | Role |
|-----------|------|------|
| **GlimpseAIPlugin** | `GlimpseAIPlugin.cs` | Plugin entry point, settings management |
| **GlimpsePanel** | `UI/GlimpsePanel.cs` | Dockable Eto.Forms panel with preview |
| **GlimpseCommand** | `Commands/GlimpseCommand.cs` | `Glimpse` command + panel registration |
| **ComfyUIClient** | `Services/ComfyUIClient.cs` | HTTP client for ComfyUI API |
| **ViewportCapture** | `Services/ViewportCapture.cs` | Viewport + depth image capture |
| **ViewportWatcher** | `Services/ViewportWatcher.cs` | Camera change detection with debounce |
| **WorkflowBuilder** | `Services/WorkflowBuilder.cs` | Builds ComfyUI workflow graphs per preset |

### Project Structure

```
GlimpseAI/
├── GlimpseAI.sln
├── src/GlimpseAI/
│   ├── GlimpseAI.csproj
│   ├── GlimpseAIPlugin.cs
│   ├── Properties/AssemblyInfo.cs
│   ├── Commands/
│   │   ├── GlimpseCommand.cs
│   │   └── GlimpseSettingsCommand.cs
│   ├── UI/
│   │   ├── GlimpsePanel.cs
│   │   └── GlimpseSettingsDialog.cs
│   ├── Services/
│   │   ├── ComfyUIClient.cs
│   │   ├── ViewportCapture.cs
│   │   ├── ViewportWatcher.cs
│   │   └── WorkflowBuilder.cs
│   └── Models/
│       ├── GlimpseSettings.cs
│       ├── PresetType.cs
│       ├── RenderRequest.cs
│       └── RenderResult.cs
└── docs/
    ├── PLAN.md
    └── SETUP.md
```

---

## 🗺️ Roadmap

### Phase 1: Core Stability ✅ **COMPLETED**
- [x] Plugin project scaffold (Rhino 8, .NET 7, Eto.Forms)
- [x] Basic panel with image display and viewport overlay
- [x] Generate button: Viewport Capture → ComfyUI → Result
- [x] Workflow presets (Fast/Balanced/HQ/4K)
- [x] Settings dialog with connection test
- [x] **Thread-safety fixes** – No more crashes from UI violations
- [x] **WebSocket + HTTP fallback** – Robust ComfyUI communication
- [x] **Real-time overlay display** – AI results in viewport

### Phase 2: Live Preview & Stability 🚧 **IN PROGRESS**
- [x] ViewportWatcher implementation *(needs testing)*
- [ ] **Stable auto-generate** on camera change
- [ ] **Improved depth capture** without arctic mode flicker
- [ ] **Graceful error handling** for ComfyUI offline scenarios
- [ ] **Memory optimization** for large images
- [ ] **Progress cancellation** on new viewport changes

### Phase 3: Enhanced Features 📋 **PLANNED**
- [ ] **Smart material detection** → Auto-prompt generation
- [ ] **Denoise strength slider** in panel
- [ ] **Save/export** rendered images with metadata
- [ ] **Keyboard shortcuts** for quick generation
- [ ] **VRAM-aware resolution limits** 
- [ ] **Background processing queue** for multiple concurrent requests

### Phase 4: Advanced AI Integration 🔮 **FUTURE**
- [ ] **Multiple ControlNet support** (Depth + Canny + Normal)
- [ ] **Native depth buffer** via RhinoCommon (no arctic mode)
- [ ] **Real-time streaming** for ultra-fast preview
- [ ] **Cloud processing options** for resource-limited machines
- [ ] **Multi-AI backend support** (not just ComfyUI)

### Phase 5: Polish & Distribution 🚀 **RELEASE**
- [ ] **Comprehensive documentation** 
- [ ] **Video tutorials** and usage examples
- [ ] **Food4Rhino listing** 
- [ ] **Professional installer**
- [ ] **Performance benchmarking** across different hardware

---

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'feat: add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the MIT License – see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- [ComfyUI](https://github.com/comfyanonymous/ComfyUI) – The powerful and modular Stable Diffusion backend
- [RhinoCommon](https://developer.rhino3d.com/) – Rhino 8 SDK
- [Eto.Forms](https://github.com/picoe/Eto) – Cross-platform UI framework

---

*Built with ❤️ for architects and designers who want AI-powered visualization without leaving Rhino.*
