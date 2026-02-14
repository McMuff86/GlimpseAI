# ComfyUI Model Cleanup – Empfehlungen

## ⚠️ Sofort-Fix: InstantX ControlNet verschieben

Das Flux Union ControlNet liegt im falschen Ordner:
```
AKTUELL:  models/diffusion_models/InstantX_FLUX.1-dev-Controlnet-Union/
RICHTIG:  models/controlnet/InstantX_FLUX.1-dev-Controlnet-Union.safetensors
```

**In PowerShell:**
```powershell
Move-Item "C:\Users\Adi.Muff\repos\ComfyUI_windows_portable\ComfyUI\models\diffusion_models\InstantX_FLUX.1-dev-Controlnet-Union\InstantX_FLUX.1-dev-Controlnet-Union.safetensors" "C:\Users\Adi.Muff\repos\ComfyUI_windows_portable\ComfyUI\models\controlnet\"
```

## 📦 Checkpoints (~270GB) – Aufräumen empfohlen

### Behalten (für GlimpseAI relevant)
| Modell | Typ | Grösse | Nutzen |
|--------|-----|--------|--------|
| dreamshaperXL_turboDPMSDE | SDXL | 6.5G | Fast Preview |
| juggernautXL_v9Rdphoto2Lightning | SDXL | 6.7G | Balanced |
| dvarchMultiPrompt_dvarchExterior | SD1.5 | 2.0G | Architektur HQ |
| architecturerealmix_v11 | SD1.5 | 2.0G | Architektur |
| interiordesignsuperm_v2 | SD1.5 | 2.0G | Interior |
| xsarchitectural_v11 | SD1.5 | 4.0G | Architektur |

### Flux (in unet/ – korrekt!)
| Modell | Grösse | Nutzen |
|--------|--------|--------|
| flux1-dev-fp8 | 17G | ⭐ HQ Rendering |
| flux1-kontext-dev | 23G | Context/Editing |
| flux_dev_big | 23G | Full precision (zu gross für 24GB mit CN) |
| flux_dev_small | 16G | Alternative fp8 |

### Duplikate / Verschieben empfohlen
Vorschlag: Unterordner erstellen für bessere Übersicht

```
checkpoints/
├── SD1.5/           ← 18 Modelle (~40GB)
├── SDXL/            ← 15 Modelle (~95GB)  
├── SD3/             ← 3 Modelle (~21GB)
├── Pony/            ← 4 Modelle (~26GB)
├── AnimateDiff/     ← 2 Modelle (~3.4GB)
├── Special/         ← ace_step, hunyuan_3d etc.
└── (Archiv/)        ← Modelle die du nicht mehr brauchst
```

**ComfyUI findet Modelle in Unterordnern** – du verlierst nichts!

### Potenzielle Löschkandidaten (~80GB frei)
- `Hyper-SDXL-1step-Unet.safetensors` (9.6G) – du hast auch die fp16 Version
- `sd-v1-4.ckpt` (4.0G) – uraltes SD1.4
- `v1-5-pruned.ckpt` (7.2G) – du hast v1-5-pruned-emaonly.safetensors (4G)
- `Realistic_Vision_V6.0_NV_B1.safetensors` (4G) – du hast auch die fp16 (2G)
- `flux_dev_big.safetensors` (23G) – du hast flux1-dev-fp8 (17G, reicht für 3090)
- `sd_xl_refiner_1.0` (5.7G) – Refiner wird kaum noch genutzt

## 🎛️ ControlNet – Aufräumen

```
controlnet/
├── SD1.5/                              ← ControlNet-v1-1 Ordner (19GB!)
├── SDXL/
│   ├── diffusers_xl_depth_full.safetensors     (2.4G)
│   ├── diffusion_pytorch_model.fp16.safetensors (2.4G)  ← DUPLIKAT? Prüfen
│   ├── controlnet-depth-sdxl-1.0-small/         (0.9G)
│   └── controlnet-canny-sdxl-1.0/               (0.9G)
├── Flux/
│   └── InstantX_FLUX.1-dev-Controlnet-Union.safetensors (6.2G) ← HIERHIN VERSCHIEBEN!
├── control_v1p_sd15_qrcode_monster.safetensors  (0.7G)
└── control-lora/
```

**Achtung:** `diffusers_xl_depth_full.safetensors` und `diffusion_pytorch_model.fp16.safetensors` – sind das verschiedene Modelle oder Duplikate? Beide ~2.4GB. Vermutlich das gleiche Modell in verschiedenen Formaten.

## ✅ Alles korrekt platziert
- VAE: ✅ (ae.safetensors für Flux, sdxl_vae.safetensors für SDXL)
- CLIP: ✅ (clip_l + t5xxl für Flux)
- Upscaler: ✅ (4x-UltraSharp etc.)
- LoRAs: ✅ (gut organisiert mit Unterordnern)
