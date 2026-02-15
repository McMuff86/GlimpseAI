# 👁️ Glimpse AI

**AI-Powered Rendering & 3D Generation for Rhino 8.**

Transform your viewport into photorealistic renderings, extract clean architectural models, and generate 3D meshes from images — all locally powered by ComfyUI.

![Rhino 8](https://img.shields.io/badge/Rhino-8-blue)
![.NET 7](https://img.shields.io/badge/.NET-7.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

---

## ✨ Features

### 🎨 AI Rendering
- **One-Click Rendering** – Photorealistic AI renderings from any Rhino viewport
- **Multiple Pipelines** – SDXL and Flux support with auto-detection
- **Model Selector** – Choose from all your installed checkpoints/UNets
- **ControlNet Depth** – Preserves your 3D structure in AI output (InstantX Union for Flux, SDXL depth models)
- **Multiple Presets** – Fast (~3s), Balanced (~15s), High Quality (~30s), 4K Export
- **Auto-Prompt** – Material-based detection or Florence2 vision analysis
- **8 Style Presets** – Textured, Sketch, Watercolor, etc.
- **Real-time Overlay** – AI results displayed directly in viewport
- **Auto Mode** – Continuously generates as you navigate

### 🏗️ Monochrome Model Extraction
- **Flux Kontext Pipeline** – Converts any image into a clean, untextured architectural model
- **Multiple Input Sources** – From preview image, loaded file, or viewport capture
- Removes clutter (people, vegetation, background) while preserving geometry

### 🧊 Image to 3D Mesh (Hunyuan3D)
- **Full Pipeline** – Image → Background Removal → Multi-View Generation → Mesh Reconstruction
- **Auto-Import** – Generated GLB mesh is automatically imported into Rhino
- **50k Face Meshes** – High-detail output with post-processing (floater removal, face reduction)
- **Multiple Input Sources** – From preview, loaded image, or viewport

### ⚙️ Pipeline & UI
- **Pipeline Selector** – Auto / Flux / SDXL with instant switching
- **Dynamic Model List** – Populated from ComfyUI API
- **Editable CFG & Denoise** – Sliders + text input with recommended values per pipeline
- **Dark Theme Settings** – Matches Rhino's dark UI
- **Yak Package** – Installable via Rhino Package Manager

---

## 📋 Requirements

| Component | Minimum |
|-----------|---------|
| **Rhino** | 8 (Windows) |
| **ComfyUI** | Latest (running with `--listen 0.0.0.0`) |
| **GPU** | 12 GB+ VRAM (24GB recommended for Flux + Hunyuan3D) |
| **.NET** | 7.0 (ships with Rhino 8) |

### Model Dependencies

| Feature | Required Models | VRAM |
|---------|----------------|------|
| **SDXL Rendering** | Any SDXL checkpoint | ~8GB |
| **Flux Rendering** | flux1-dev-fp8 + clip_l + t5xxl + ae.safetensors | ~20GB |
| **Flux ControlNet** | InstantX_FLUX.1-dev-Controlnet-Union | +6GB |
| **Monochrome** | flux1-dev-kontext_fp8_scaled | ~20GB |
| **3D Mesh** | hunyuan3d-dit-v2 + ComfyUI-Hunyuan3D nodes | ~20GB |

> See [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md) for complete model download links and setup.

---

## 🚀 Installation

### From Yak Package
```powershell
# Build the package
.\build-yak.ps1

# Install locally
& "C:\Program Files\Rhino 8\System\yak.exe" install dist\glimpseai-0.2.0-rh8_0-win.yak
```

### From Source
```bash
git clone https://github.com/McMuff86/GlimpseAI.git
cd GlimpseAI
dotnet build src/GlimpseAI/GlimpseAI.csproj -c Release
```
Copy `src/GlimpseAI/bin/Release/net7.0-windows/GlimpseAI.rhp` to Rhino plugins folder or drag onto Rhino.

---

## 🎮 Usage

1. **Start ComfyUI** (default: `http://localhost:8188`)
2. Open Rhino 8, type **`Glimpse`** to open the panel
3. **Generate** – AI rendering from viewport
4. **Monochrome** – Clean architectural model from image
5. **3D Mesh** – Generate 3D mesh from image (Hunyuan3D)

### Commands

| Command | Description |
|---------|-------------|
| `Glimpse` | Toggle the Glimpse AI panel |
| `GlimpseSettings` | Open settings dialog |

---

## 🗺️ Roadmap

- [x] Core rendering (SDXL + Flux)
- [x] ControlNet Depth (SDXL + InstantX Union)
- [x] Auto-Prompt (material detection + Florence2)
- [x] Model selector with dynamic list
- [x] Monochrome model extraction (Flux Kontext)
- [x] Image to 3D mesh (Hunyuan3D)
- [x] Yak packaging
- [ ] Textured mesh generation (needs custom_rasterizer)
- [ ] 3D mesh preview in panel
- [ ] Generation history browser
- [ ] Keyboard shortcuts
- [ ] A/B comparison view
- [ ] Food4Rhino listing

---

## 📄 License

MIT License – see [LICENSE](LICENSE) for details.

---

*Built with ❤️ for architects and designers who want AI-powered visualization without leaving Rhino.*
