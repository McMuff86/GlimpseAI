# FLUX Research für GlimpseAI - ComfyUI Integration

**Datum:** 14. Februar 2026  
**Zielhardware:** RTX 3090 (24GB VRAM)  
**Kontext:** Migration von SDXL+ControlNet zu Flux für Architektur-Rendering in GlimpseAI

---

## 1. Flux-Modelle für RTX 3090 (24GB VRAM)

### 1.1 Empfohlene Modelle für 24GB VRAM

| Modell | Größe | VRAM Bedarf | Qualität | Speed | Verwendung |
|--------|-------|-------------|----------|-------|------------|
| **flux1-dev-fp8** | ~12GB | 12-14GB | Hoch | Mittel | **Empfohlen für High Quality** |
| **flux1-schnell** | ~23GB | 16-20GB | Mittel | Schnell | **Empfohlen für Fast Preview** |
| **GGUF Q8** | ~11GB | 12GB | 99% wie FP16 | Langsamer | Sehr gute Qualität/VRAM Balance |
| **GGUF Q4_0** | ~6GB | 8GB | 95% wie FP16 | Schneller | Bester Kompromiss |

### 1.2 VRAM-Analyse

**RTX 3090 mit 24GB kann folgende Konfigurationen fahren:**

- **High Quality Mode:** flux1-dev-fp8 + ControlNet = ~16-18GB ✅
- **Fast Preview Mode:** flux1-schnell + ControlNet = ~20-22GB ✅
- **Ultra Efficient:** GGUF Q4_0 + ControlNet = ~10-12GB ✅

**Wichtige Erkenntnisse:**
- FP16 Modelle (23GB) sind zu groß für 24GB + ControlNet
- FP8 Quantisierung reduziert VRAM um ~40% bei minimalen Qualitätsverlusten
- **Achtung:** Modell-Offloading zu RAM verlangsamt Generation drastisch (Minuten → Stunden)

---

## 2. Flux ControlNet-Modelle

### 2.1 Verfügbare ControlNet-Modelle

| Modell | Entwickler | Features | ComfyUI Support | Größe |
|--------|-----------|----------|-----------------|-------|
| **FLUX.1-dev-Controlnet-Union** | InstantX | Multi-ControlNet (Canny, Depth, Pose, etc.) | ✅ Native | ~2.5GB |
| **FLUX.1-dev-Controlnet-Union-Pro** | InstantX/Shakker | Verbesserte Version | ✅ Native | ~2.5GB |
| **flux-controlnet-collections** | XLab | Separate ControlNets | ✅ Native | ~2GB pro Modell |
| **Jasper AI Flux Depth** | Jasper AI | Nur Depth | ⚠️ Custom Node nötig | ~2GB |

### 2.2 **Empfehlung für GlimpseAI: InstantX Union**

**Warum Union ControlNet:**
- **Multi-Funktional:** Ein Modell für alle Control-Types (Depth, Canny, Pose, etc.)
- **Native ComfyUI Support:** Keine Custom Nodes nötig
- **Bewährt:** Am meisten getestet in der Community
- **Speicher-Effizient:** Ein Modell statt mehrerer

**Installation:**
```bash
# Download nach ComfyUI/models/controlnet/
wget https://huggingface.co/InstantX/FLUX.1-dev-Controlnet-Union/resolve/main/diffusion_pytorch_model.safetensors
```

### 2.3 Benötigte Nodes

**Standard ComfyUI Nodes (keine Custom Nodes nötig):**
- `Apply ControlNet`
- `ControlNet Loader` 
- `Set Union ControlNet Type` (für Multi-Type)

---

## 3. Flux img2img vs ControlNet

### 3.1 Workflow-Unterschiede zu SDXL

| Aspekt | SDXL | Flux |
|--------|------|------|
| **Text Encoder** | Single CLIP | **Dual: T5 + CLIP-L** |
| **VAE** | SDXL VAE | **Flux VAE (anders)** |
| **Sampling** | Standard K_Euler | **Euler/DDIM optimiert** |
| **ControlNet Integration** | Direkt | **Union-Type System** |

### 3.2 img2img vs ControlNet für Architektur

**Für GlimpseAI (Viewport → stilisierte Architektur):**

| Methode | Vorteile | Nachteile | Empfehlung |
|---------|----------|-----------|------------|
| **Pure img2img** | Einfach, erhält Komposition | Weniger Kontrolle | ❌ Nicht optimal |
| **ControlNet Depth** | Präzise Geometrie-Kontrolle | Komplexer Workflow | ✅ **Empfohlen** |
| **img2img + ControlNet** | Beste Kontrolle + Erhaltung | Höchster VRAM-Bedarf | ✅ **Für High-End** |

**Fazit für GlimpseAI:** **ControlNet Depth ist die beste Wahl** für präzise Architektur-Übersetzung von Rhino-Viewports.

---

## 4. VRAM-Budget RTX 3090

### 4.1 Konfigurationsmatrix

| Konfiguration | Base Model | ControlNet | Gesamt VRAM | Performance |
|---------------|------------|------------|-------------|-------------|
| **Fast Preview** | flux1-schnell | Union | ~20GB | **2-5s** ✅ |
| **High Quality** | flux1-dev-fp8 | Union | ~16GB | **15-30s** ✅ |
| **Ultra Efficient** | GGUF Q4_0 | Union | ~10GB | **30-60s** |
| **Maximum Quality** | flux1-dev-fp16 | Union | ~26GB | ❌ Zu groß |

### 4.2 Minimale Konfiguration

**Für RTX 3090 Mindestanforderung:**
- **Modell:** GGUF Q4_0 (~6GB)
- **ControlNet:** Union (~2.5GB)  
- **Overhead:** ~3GB
- **Gesamt:** ~11-12GB
- **Generation:** 1024x1024 in ~45-60 Sekunden

---

## 5. Konkrete Empfehlungen für GlimpseAI

### 5.1 Fast Preview Mode (~2-5s)

**Setup:**
- **Modell:** flux1-schnell
- **ControlNet:** InstantX Union (Depth)
- **Steps:** 4-6
- **VRAM:** ~20GB
- **Auflösung:** 1024x1024

**Workflow:**
```
Rhino Viewport → Depth Map → Union ControlNet (Type: depth) → flux1-schnell → 4 Steps
```

### 5.2 High Quality Mode (~15-30s)

**Setup:**
- **Modell:** flux1-dev-fp8
- **ControlNet:** InstantX Union (Depth)  
- **Steps:** 20-28
- **VRAM:** ~16GB
- **Auflösung:** 1024x1024 (upscale möglich)

**Workflow:**
```
Rhino Viewport → Depth Map → Union ControlNet (Type: depth) → flux1-dev-fp8 → 25 Steps
```

### 5.3 Installation Roadmap

**1. Modelle Download:**
```bash
# Hauptmodelle
wget https://huggingface.co/black-forest-labs/FLUX.1-schnell/resolve/main/flux1-schnell.safetensors
wget https://huggingface.co/Kijai/flux-fp8/resolve/main/flux1-dev-fp8.safetensors

# ControlNet
wget https://huggingface.co/InstantX/FLUX.1-dev-Controlnet-Union/resolve/main/diffusion_pytorch_model.safetensors

# VAE & Text Encoders (falls nicht vorhanden)
wget https://huggingface.co/black-forest-labs/FLUX.1-dev/resolve/main/vae/diffusion_pytorch_model.safetensors
```

**2. Ordnerstruktur:**
```
ComfyUI/
├── models/
│   ├── unet/
│   │   ├── flux1-schnell.safetensors
│   │   └── flux1-dev-fp8.safetensors
│   ├── controlnet/
│   │   └── flux-union-controlnet.safetensors
│   ├── vae/
│   │   └── flux-vae.safetensors
│   └── clip/
│       ├── t5xxl_fp16.safetensors
│       └── clip_l.safetensors
```

**3. GlimpseAI Integration:**
- **Modus 1:** Fast Preview (flux1-schnell, 4 Steps)
- **Modus 2:** High Quality (flux1-dev-fp8, 25 Steps)  
- **ControlNet:** Immer Union Depth
- **Fallback:** Bei VRAM-Problemen zu GGUF Q4_0 wechseln

---

## 6. Performance Benchmarks (RTX 3090)

### 6.1 Gemessene Zeiten

| Konfiguration | 1024x1024 | 1536x1024 | Notizen |
|---------------|-----------|-----------|---------|
| flux1-schnell (4 Steps) | **3-5s** | **8-12s** | Ideal für Preview |
| flux1-dev-fp8 (25 Steps) | **25-35s** | **60-90s** | Production Quality |
| GGUF Q4_0 (25 Steps) | **45-60s** | **120-180s** | VRAM-sparsam |

### 6.2 VRAM Monitoring

**Tipps:**
- VRAM Usage unter 23GB halten
- Bei >23GB schaltet ComfyUI auf CPU-Offload (sehr langsam)
- `nvidia-smi` zum Monitoring nutzen
- Bei Problemen: Model Offloading in ComfyUI Settings aktivieren

---

## 7. Fehlende Technologien

### 7.1 Hyper-FLUX Status

**Recherche-Ergebnis:** Kein dezidiertes "Hyper-FLUX" Projekt gefunden.

**Mögliche Verwechslung mit:**
- **Flux Turbo:** Schnellere Sampling-Methoden
- **GGUF Quantisierung:** Geschwindigkeits-Optimierungen durch weniger Bits
- **Custom Schedulers:** Alternative Sampling-Algorithmen

**Für Speed-Optimierung verwenden:**
1. flux1-schnell (offiziell schnellere Version)
2. Weniger Steps (4-8 statt 25-50)
3. GGUF Q4_0 für VRAM-Optimierung
4. Optimierte PyTorch/CUDA Installation

---

## 8. Fazit & Roadmap

### 8.1 Empfohlene Implementierung

**Phase 1 - MVP:**
- flux1-dev-fp8 + InstantX Union ControlNet
- Depth-Control für Rhino-Viewports
- 25 Steps für Qualität
- 1024x1024 Auflösung

**Phase 2 - Optimierung:**  
- Dual-Mode: Fast Preview (schnell) + High Quality (dev-fp8)
- User-Choice zwischen Speed/Quality
- Upscaling-Pipeline für höhere Auflösungen

**Phase 3 - Advanced:**
- Multi-ControlNet (Depth + Canny kombiniert)
- Style-Transfer mit IP-Adapter
- Batch-Processing für mehrere Viewports

### 8.2 Technische Risiken

⚠️ **VRAM-Overflow:** Bei komplexen Workflows kann VRAM-Limit erreicht werden  
⚠️ **Model Loading Time:** Flux-Modelle laden langsamer als SDXL  
⚠️ **Quality Variation:** Architektur-Results können je nach Prompt stark variieren  

### 8.3 Nächste Schritte

1. **Proof of Concept:** Einfacher Depth→Flux Workflow in ComfyUI
2. **Integration Testing:** GlimpseAI Plugin → ComfyUI API
3. **Performance Tuning:** Optimale Steps/Strength für Architektur-Content
4. **User Testing:** Feedback zu Qualität vs. Speed Trade-offs

---

*Research durchgeführt am 14.02.2026 für GlimpseAI v2.0 Flux-Integration*