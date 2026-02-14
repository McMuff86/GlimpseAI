# ComfyUI Modellübersicht - Adi's System

**Scan-Datum:** 2025-02-14  
**ComfyUI-Pfad:** `/mnt/c/Users/Adi.Muff/repos/ComfyUI_windows_portable/ComfyUI/`  
**Gesamte Modellgröße:** ~330GB

---

## 1. Installierte Checkpoints (~270GB)

### **Flux Modelle (moderne, hochqualitative Architektur)**
- **ace_step_v1_3.5b.safetensors** (7.2GB) - Flux Typ
- **hunyuan_3d_v2.1.safetensors** (6.9GB) - 3D-spezialisiert

### **SDXL Modelle (1024x1024 optimiert)**
- **sd_xl_base_1.0.safetensors** (6.5GB) - Standard SDXL Base
- **sd_xl_refiner_1.0.safetensors** (5.7GB) - SDXL Refiner
- **Hyper-SDXL-1step-Unet-Comfyui.fp16.safetensors** (6.5GB) - 1-Step Lightning
- **dreamshaperXL10_alpha2Xl10.safetensors** (6.5GB) - Artistisch
- **juggernautXL_v9Rdphoto2Lightning.safetensors** (6.7GB) - Fotorealistisch
- **ponyDiffusionV6XL_v6StartWithThisOne.safetensors** (6.5GB) - Anime/Manga
- **cyberrealisticXL_v20.safetensors** (6.5GB) - Cyberpunk-Stil
- **copaxTimelessxlSDXL1_v12.safetensors** (6.5GB)
- **zavychromaxl_v50.safetensors** (6.5GB)
- **realcartoonXL_v6.safetensors** (6.5GB) - Cartoon/Animation

### **SD 1.5 Modelle (512x512 klassisch)**
- **v1-5-pruned-emaonly.safetensors** (4.0GB) - Standard SD 1.5
- **realisticVisionV51_v51VAE.safetensors** (2.0GB) - Fotorealistisch
- **cyberrealistic_v60_fp16.safetensors** (4.0GB) - Cyberpunk
- **dreamshaper_8.safetensors** (2.0GB) - Allround
- **absolutereality_v181.safetensors** (2.0GB) - Realistisch
- **epicrealism_pureEvolutionV5.safetensors** (2.0GB) - Fotorealistisch
- **deliberate_v2.safetensors** (2.0GB) - Kunst-orientiert

### **SD 3 Modelle (neueste Generation)**
- **stableDiffusion3SD3_sd3MediumInclT5XXL.safetensors** (11GB) - Vollversion mit Text-Encoder
- **stableDiffusion3SD3_sd3MediumInclClips.safetensors** (5.6GB) - Mit CLIP
- **stableDiffusion3SD3_sd3Medium.safetensors** (4.1GB) - Base

### **Spezialisierte Modelle**
- **AnimateLCM_sd15_t2v.ckpt** (1.7GB) - Video-Generation
- **animatediffLCMMotion_v10.ckpt** (1.7GB) - Animation
- **architecturerealmix_v11.safetensors** (2.0GB) - Architektur
- **interiordesignsuperm_v2.safetensors** (2.0GB) - Innenarchitektur

---

## 2. UNet-Modelle (~77GB) - **FLUX POWER!**

**Flux ist Adis Hauptmodell-Familie:**
- **flux1-dev-fp8.safetensors** (17GB) - Optimiert, kompakt
- **flux1-kontext-dev.safetensors** (23GB) - Kontext-verstärkt
- **flux_dev_big.safetensors** (23GB) - Vollversion
- **flux_dev_small.safetensors** (16GB) - Kompaktversion

---

## 3. ControlNet-Modelle (~mehrere GB)

### **Standard ControlNets**
- **controlnet-canny-sdxl-1.0** - Kantenerkennung (917MB)
- **controlnet-depth-sdxl-1.0-small** - Tiefenerkennung (917MB)
- **control-lora** Sammlung mit verschiedenen Rank-Größen:
  - Canny, Depth, Recolor, Sketch Control-LoRAs (je 378MB-739MB)
  - **clip_vision_g.safetensors** (3.5GB) - Vision-Modell

### **Pose/Animation ControlNets**
- **characterWalkingAndRunning_betterCrops** - Umfangreiches Pose-Dataset
  - Tausende von Pose-Referenzbildern für Charakter-Animation
  - Verschiedene Blickwinkel (B, BL, BR, L, R, T, TL, TR)
  - Walking/Running/Woman-spezifische Posen

**Kompatibilität:** Canny/Depth mit SDXL, Pose-Daten universell einsetzbar

---

## 4. CLIP/Text-Encoder (~9.6GB)

- **clip_l.safetensors** (235MB) - Standard CLIP
- **t5xxl_fp8_e4m3fn.safetensors** (4.6GB) - T5 Text-Encoder (Flux)
- **t5xxl_fp8_e4m3fn_scaled.safetensors** (4.9GB) - T5 skaliert

---

## 5. VAE-Modelle (~1.5GB)

- **ae.safetensors** (320MB) - Standard Auto-Encoder
- **flux2-vae.safetensors** (321MB) - **Flux VAE**
- **qwen_image_vae.safetensors** (243MB) - Qwen-spezifisch
- **sdxl_vae.safetensors** (320MB) - SDXL VAE
- **vae-ft-mse-840000-ema-pruned.safetensors** (320MB) - Fine-tuned VAE
- **flux1.schnell/diffusion_pytorch_model.safetensors** (160MB) - Flux Schnell

---

## 6. LoRAs (~16GB)

### **Flux LoRAs (606MB)**
- **aidmaFLUXPro1.1-FLUX-v0.3.safetensors** (74MB)
- **aidmaMJ6.1-FLUX-v0.5.safetensors** (74MB)
- **kontext-turnaround-sheet-v1.safetensors** (328MB) - Charakter-Sheets
- **impossible_geometry_V1.0.safetensors** (76MB)
- **Cyberpunk Realistic V1.safetensors** (19MB)
- **The_Vanguard.safetensors** (19MB)
- **FLUX NSFW Collection** (3.3GB) - Adult Content LoRAs

### **SDXL LoRAs**
- **Hyper-SDXL-1step-lora.safetensors** (751MB) - 1-Step Generation
- **SDXLFaeTastic2400.safetensors** (436MB)
- **perfection style.safetensors** (871MB)
- **xl_more_art-full_v1.safetensors** (686MB)
- **Face Detailer SDXL** (218MB) - Gesichtsverbesserung

### **SD 1.5 LoRAs**
- **add_detail.safetensors** (37MB) - Detail-Verbesserung
- **more_details.safetensors** (9.2MB)
- Verschiedene Portrait-LoRAs

### **Spezialisierte LoRAs**
- **xsarchitecturalv3com_v31InSafetensor.safetensors** (4.0GB) - Architektur
- **clarity_3.safetensors** (2.0GB) - Klarheits-Enhancement
- **Walking_Sprite.safetensors** (218MB) - Sprite-Animation
- **IP-Adapter Face-ID LoRAs** - Gesichtserkennung/Konsistenz

---

## 7. Upscale-Modelle (~468MB)

### **ESRGAN Upscaler**
- **4x-UltraSharp.pth** (64MB) - Schärfungsbasiert
- **4xReal_SSDIR_DAT_GAN.pth** (148MB) - Realistisch
- **4x_foolhardy_Remacri.pth** (64MB) - Allround
- **8x_NMKD-Superscale_150000_G.pth** (64MB) - 8x Upscaling

**Qualität:** Sehr gute Auswahl für verschiedene Anwendungsfälle

---

## 8. Custom Nodes (Extensions)

### **Management & Workflow**
- **comfyui-manager** - Node-Manager
- **comfyui-easy-use** - Vereinfachte Bedienung
- **efficiency-nodes-comfyui** - Workflow-Optimierung
- **rgthree-comfy** - UI-Verbesserungen

### **ControlNet & Animation**
- **comfyui_controlnet_aux** - ControlNet-Hilfsfunktionen
- **comfyui-advanced-controlnet** - Erweiterte ControlNet-Features
- **comfyui-animatediff-evolved** - Animation
- **comfyui-frame-interpolation** - Frame-Interpolation

### **Bildverarbeitung**
- **comfyui-impact-pack** - Bildverbesserung
- **comfyui_ultimatesdupscale** - Professionelles Upscaling
- **comfyui_ipadapter_plus** - IP-Adapter Integration
- **comfyui-detail-daemon** - Detail-Enhancement

### **AI-Integration**
- **ComfyUI-QwenVL** - Qwen Vision-Language
- **comfyui-hunyuan-3d-2** - 3D-Generation
- **comfyui-wd14-tagger** - Auto-Tagging

### **Utility**
- **comfyui-depthanythingv2** - Tiefenerkennung
- **comfyui-videohelpersuite** - Video-Tools
- **comfyui-image-saver** - Erweiterte Speicheroptionen

---

## 9. GlimpseAI Optimierungsempfehlungen

### ✅ **SEHR GUT - Bereits optimal ausgestattet:**

1. **Flux-Ecosystem ist vollständig**
   - Alle wichtigen Flux-UNet-Modelle vorhanden
   - Passende VAEs und Text-Encoder
   - Spezialisierte Flux-LoRAs

2. **ControlNet-Abdeckung**
   - Canny + Depth für SDXL
   - Umfangreiche Pose-Daten für Character-Animation
   - Control-LoRA-System

3. **Upscaling-Pipeline**
   - Mehrere hochwertige ESRGAN-Modelle
   - Verschiedene Spezialisierungen (scharf, realistisch, allround)

### ⚠️ **EMPFOHLENE ERGÄNZUNGEN:**

#### **Flux-ControlNets fehlen komplett**
**Problem:** Nur SDXL ControlNets, keine Flux-kompatiblen
**Lösung:** 
- **InstantX Flux ControlNets** hinzufügen:
  - `flux-dev-controlnet-canny-v3.safetensors`
  - `flux-dev-controlnet-depth-v3.safetensors`  
  - `flux-dev-controlnet-pose-v3.safetensors`
  - `flux-dev-controlnet-union-pro.safetensors` (Multi-ControlNet)

#### **Fehlendes Flux-Inpainting**
**Problem:** Kein Inpainting-Modell für Flux
**Lösung:**
- `flux-dev-inpainting.safetensors` (~23GB)

#### **IP-Adapter für Flux**
**Problem:** IP-Adapter nur für SD 1.5/SDXL, nicht Flux
**Lösung:**
- `ip-adapter-flux-dev.safetensors`

### 🔧 **WORKFLOW-OPTIMIERUNGEN:**

1. **Haupt-Pipeline:** Flux + Flux-ControlNets + Flux-LoRAs
2. **Fallback:** SDXL für spezielle Fälle
3. **Upscaling:** 4x-UltraSharp für die meisten Fälle
4. **Animation:** Bestehende AnimateDiff + Pose-ControlNets

### 💾 **Speicher-Management:**
- **Aktuelle Nutzung:** ~330GB
- **Mit empfohlenen Ergänzungen:** ~360GB
- **Empfehlung:** Ältere SD 1.5 Modelle archivieren bei Platzmangel

---

## 10. Fazit

**Adis ComfyUI-Installation ist bereits sehr professionell aufgesetzt!**

✅ **Stärken:**
- Vollständiges Flux-Ecosystem (modernste Technologie)
- Massive LoRA-Sammlung (16GB+)
- Umfangreiche Checkpoint-Vielfalt
- Professionelle Custom-Node-Ausstattung
- Gute Upscaling-Pipeline

❌ **Hauptschwäche:**
- **Flux-ControlNets fehlen komplett** - das ist der kritische Punkt für GlimpseAI

**Priorität 1:** InstantX Flux-ControlNet-Familie installieren
**Priorität 2:** Flux-Inpainting-Modell
**Priorität 3:** IP-Adapter für Flux

Mit diesen Ergänzungen wäre die Installation für GlimpseAI optimal und zukunftssicher!