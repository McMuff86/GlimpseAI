# GlimpseAI Model Dependencies

## Base (SDXL/SD1.5)

| Model | Location | Required |
|-------|----------|----------|
| Any SDXL checkpoint (e.g., `juggernautXL`, `dreamshaperXL`) | `models/checkpoints/` | Yes (for SDXL pipeline) |

## Flux Pipeline

| Model | Location | Required |
|-------|----------|----------|
| Flux UNet (e.g., `flux1-dev-fp8_e4m3fn.safetensors`) | `models/unet/` | Yes |
| `clip_l.safetensors` | `models/clip/` | Yes |
| `t5xxl_fp8_e4m3fn.safetensors` | `models/clip/` | Yes |
| `ae.safetensors` | `models/vae/` | Yes |

## Flux ControlNet (Depth-guided generation)

| Model | Location | Required |
|-------|----------|----------|
| `InstantX_FLUX.1-dev-Controlnet-Union.safetensors` | `models/controlnet/` | Optional (improves structure) |

## Monochrome Model (Flux Kontext)

| Model | Location | Required | VRAM |
|-------|----------|----------|------|
| `flux1-dev-kontext_fp8_scaled.safetensors` | `models/unet/` | Yes | ~20 GB |
| `clip_l.safetensors` | `models/clip/` | Yes | Shared with Flux |
| `t5xxl_fp8_e4m3fn.safetensors` | `models/clip/` | Yes | Shared with Flux |
| `ae.safetensors` | `models/vae/` | Yes | Shared with Flux |

**Custom nodes required:** `ImageStitch`, `FluxKontextImageScale`, `ReferenceLatent`, `FluxGuidance`

## Auto-Prompt Vision (Florence2)

| Model | Location | Required |
|-------|----------|----------|
| `microsoft/Florence-2-base` | Auto-downloaded by ComfyUI | Optional |

**Custom node required:** `Florence2Run` (ComfyUI Florence2 node pack)

## SDXL ControlNet (Depth-guided generation)

| Model | Location | Required |
|-------|----------|----------|
| Any depth ControlNet (e.g., `diffusers_xl_depth_full`) | `models/controlnet/` | Optional |

## 4K Export Upscaling

| Model | Location | Required |
|-------|----------|----------|
| `4x-UltraSharp.pth` | `models/upscale_models/` | Optional (for 4K Export preset) |
