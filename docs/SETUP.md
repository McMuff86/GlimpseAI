# 🛠️ Glimpse AI – Setup Guide

Step-by-step guide to get Glimpse AI running with ComfyUI on your machine.

---

## Table of Contents

1. [Install ComfyUI](#1-install-comfyui)
2. [Download Models](#2-download-models)
3. [Configure ComfyUI](#3-configure-comfyui)
4. [Build the Plugin](#4-build-the-plugin)
5. [Load in Rhino](#5-load-in-rhino)
6. [Verify Everything Works](#6-verify-everything-works)
7. [Troubleshooting](#7-troubleshooting)

---

## 1. Install ComfyUI

### Option A: Standalone (Recommended)

Download the latest portable release from [ComfyUI GitHub](https://github.com/comfyanonymous/ComfyUI):

```
https://github.com/comfyanonymous/ComfyUI/releases
```

Extract the archive and run:

```powershell
# Navigate to the ComfyUI directory
cd C:\ComfyUI

# Run with network listening enabled (required for Glimpse AI)
python main.py --listen 0.0.0.0
```

### Option B: Via ComfyUI Manager

If you already have ComfyUI installed, make sure to start it with the `--listen` flag:

```powershell
python main.py --listen 0.0.0.0
```

> **⚠️ Important:** The `--listen 0.0.0.0` flag is **required** so that the Rhino plugin can connect to ComfyUI via HTTP.

### Verify ComfyUI is Running

Open your browser and navigate to `http://localhost:8188`. You should see the ComfyUI web interface.

---

## 2. Download Models

Glimpse AI uses different models for each quality preset. Download and place them in the correct ComfyUI folders.

### Checkpoint Models

Place these in `ComfyUI/models/checkpoints/`:

| Model | Preset | Download |
|-------|--------|----------|
| **DreamShaper XL Turbo** | Fast | [CivitAI](https://civitai.com/models/112902/dreamshaper-xl) – Download the "Turbo DPM SDE" variant |
| **Juggernaut XL Lightning** | Balanced | [CivitAI](https://civitai.com/models/133005/juggernaut-xl) – Download the "v9 + RunDiffusion Photo2 Lightning" variant |
| **dvArch Multi-Prompt Exterior** | HQ / 4K | [CivitAI](https://civitai.com/models/149686/dvarch) – Download "dvarchMultiPrompt_dvarchExterior" |

#### Expected Filenames

The workflows expect these exact filenames (rename after download if needed):

```
ComfyUI/models/checkpoints/
├── dreamshaperXL_turboDPMSDE.safetensors
├── juggernautXL_v9Rdphoto2Lightning.safetensors
└── dvarchMultiPrompt_dvarchExterior.safetensors
```

### Upscale Models

Place these in `ComfyUI/models/upscale_models/`:

| Model | Preset | Download |
|-------|--------|----------|
| **4x-UltraSharp** | 4K Export | [OpenModelDB](https://openmodeldb.info/models/4x-UltraSharp) |

```
ComfyUI/models/upscale_models/
└── 4x-UltraSharp.pth
```

> **💡 Tip:** You can start with just the DreamShaper XL Turbo model for the Fast preset. Add the others as needed.

---

## 3. Configure ComfyUI

### Recommended Startup Command

Create a batch file `start_comfyui.bat` for convenience:

```batch
@echo off
cd /d C:\ComfyUI
python main.py --listen 0.0.0.0 --port 8188 --preview-method auto
pause
```

### GPU Memory Settings

If you have limited VRAM (12 GB), add memory optimization flags:

```batch
python main.py --listen 0.0.0.0 --lowvram
```

For 16 GB+ VRAM (recommended):

```batch
python main.py --listen 0.0.0.0
```

For 24 GB+ VRAM (optimal):

```batch
python main.py --listen 0.0.0.0 --highvram
```

---

## 4. Build the Plugin

### Prerequisites

- [.NET 7.0 SDK](https://dotnet.microsoft.com/download/dotnet/7.0) (or let Visual Studio handle it)
- Visual Studio 2022 with ".NET desktop development" workload, **or** the `dotnet` CLI

### Build via Command Line

```powershell
git clone https://github.com/McMuff86/GlimpseAI.git
cd GlimpseAI

# Restore NuGet packages and build
dotnet build src/GlimpseAI/GlimpseAI.csproj -c Release
```

The output `.rhp` file will be in:

```
src/GlimpseAI/bin/Release/net7.0-windows/GlimpseAI.rhp
```

### Build via Visual Studio

1. Open `GlimpseAI.sln` in Visual Studio 2022
2. Set the configuration to **Release**
3. Build → Build Solution (Ctrl+Shift+B)

---

## 5. Load in Rhino

### Method A: Drag and Drop

1. Open Rhino 8
2. Drag the `GlimpseAI.rhp` file from Explorer onto the Rhino window
3. Rhino will ask to install the plugin – confirm

### Method B: Plugin Manager

1. In Rhino, go to **Tools → Options → Plug-ins**
2. Click **Install…**
3. Navigate to the `.rhp` file and select it

### Method C: Manual Copy

Copy the entire build output folder to:

```
%APPDATA%\McNeel\Rhinoceros\8.0\Plug-ins\GlimpseAI (A3F7B2E1-9C84-4D6F-B5E3-1A2D8F4C6E90)\
```

Copy these files:
- `GlimpseAI.rhp`
- `System.Text.Json.dll` (and any other DLLs in the output folder)

---

## 6. Verify Everything Works

### Step 1: Check ComfyUI

1. Open `http://localhost:8188` in your browser
2. Confirm the web UI loads without errors
3. Check that at least one checkpoint model appears in the model dropdown

### Step 2: Check the Plugin

1. In Rhino, type `Glimpse` in the command line
2. The Glimpse AI panel should appear
3. Type `GlimpseSettings` to open settings
4. Click **Test Connection** – it should show "🟢 Connected"

### Step 3: First Generation

1. Create or open a 3D model in Rhino
2. Navigate to a perspective viewport
3. Open the Glimpse AI panel (`Glimpse` command)
4. Enter a prompt (e.g. `modern architecture, photorealistic, natural lighting`)
5. Click **Generate**
6. Wait for the AI rendering to appear in the panel

---

## 7. Troubleshooting

### "🔴 Unreachable" in Connection Test

- **Is ComfyUI running?** Check that `python main.py --listen 0.0.0.0` is active
- **Correct URL?** Default is `http://localhost:8188`
- **Firewall?** Windows Firewall may block the connection. Allow Python through the firewall.
- **Different port?** If ComfyUI runs on a different port, update the URL in GlimpseSettings

### "Generation failed" or Error During Rendering

- **Missing model?** Check the Rhino command line for error details. ComfyUI may not find the checkpoint file.
- **Filename mismatch?** Verify your model filenames match exactly (see [Expected Filenames](#expected-filenames) above)
- **Out of VRAM?** Try the `--lowvram` flag or use a smaller preset (Fast instead of HQ)

### Plugin Doesn't Load

- **Rhino version?** Glimpse AI requires Rhino 8. It will not work with Rhino 7 or earlier.
- **Missing dependencies?** Make sure all DLLs from the build output are in the same folder as the `.rhp` file
- **Check Rhino log:** `Tools → Options → Advanced → EnableLogging` then check `%TEMP%\RhinoLogs\`

### Slow Generation Times

| Issue | Solution |
|-------|----------|
| First generation is slow (~10-30s) | Normal – ComfyUI loads the model into GPU memory on first use. Subsequent runs are faster. |
| All generations are slow | Check GPU utilization in Task Manager. If GPU is not at 100%, there may be a CPU bottleneck. |
| Out of VRAM errors | Use `--lowvram` flag. Reduce capture resolution in settings. Use the Fast preset. |

### ComfyUI Log Shows Errors

Check the ComfyUI terminal window for error messages. Common issues:

- `FileNotFoundError` → Model file not found, check filenames and paths
- `CUDA out of memory` → Reduce resolution or use `--lowvram`
- `Connection refused` → ComfyUI not started with `--listen` flag

---

## 📁 Folder Structure Reference

```
C:\ComfyUI\
├── models\
│   ├── checkpoints\
│   │   ├── dreamshaperXL_turboDPMSDE.safetensors       ← Fast preset
│   │   ├── juggernautXL_v9Rdphoto2Lightning.safetensors ← Balanced preset
│   │   └── dvarchMultiPrompt_dvarchExterior.safetensors  ← HQ / 4K preset
│   └── upscale_models\
│       └── 4x-UltraSharp.pth                            ← 4K Export upscaler
├── input\                                                ← Viewport images uploaded here
├── output\
│   └── GlimpseAI\                                        ← Rendered images saved here
└── main.py
```

---

*Need help? Open an issue on [GitHub](https://github.com/McMuff86/GlimpseAI/issues).*
