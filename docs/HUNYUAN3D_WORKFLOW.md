# Hunyuan3D Workflow – Image to 3D Mesh

## Overview
Two-step pipeline: Image → Monochrome/Clean Model → 3D Mesh (GLB)

### Step 1: Monochrome Model (monochrome_model.json)
Uses **Flux Kontext** to convert any image into a clean, untextured architectural model view.

**Pipeline:**
1. Load Flux Kontext (UNETLoader → flux1-dev-kontext_fp8_scaled.safetensors)
2. DualCLIPLoader (clip_l + T5XXL)
3. VAELoader (ae.safetensors)
4. Input image + Reference image → ImageStitch (side by side)
5. VAEEncode → ReferenceLatent
6. CLIPTextEncode: "convert image into a clean monochrome architectural model..."
7. FluxGuidance (2.5) → KSampler (20 steps, euler, simple)
8. VAEDecode → SaveImage

**Dependencies:**
- flux1-dev-kontext_fp8_scaled.safetensors (~17GB) in models/diffusion_models/
- clip_l.safetensors in models/clip/
- T5XXL (fp16 or fp8) in models/clip/
- ae.safetensors in models/vae/
- ~20GB VRAM (fp8_scaled)

### Step 2: 3D Mesh Generation (hy3d_multiview_example.json)
Uses **Hunyuan3D v2** to generate multi-view images and reconstruct a 3D mesh.

**Pipeline:**
1. **Delight** – Remove lighting/shadows from input (Hy3DDelightImage)
2. **Multi-View Generation** – Generate 6 camera views (front/left/right/back/top/bottom)
3. **MeshGen** – Reconstruct 3D mesh from multi-views (Hy3DGenerateMeshMultiView)
4. **UV Wrap** – Generate UV coordinates (Hy3DMeshUVWrap)
5. **Texture Baking** – Bake textures from views onto mesh
6. **Texture Inpainting** – Fill gaps (CV2InpaintTexture)
7. **Export** – GLB format (Hy3DExportMesh)

**Dependencies:**
- hunyuan3d-delight-v2-0 (auto-download via DownloadAndLoadHy3DDelightModel)
- hunyuan3d-paint-v2-0 (auto-download via DownloadAndLoadHy3DPaintModel)
- flux1-dev-fp8.safetensors (for Hy3DModelLoader)
- 4x_foolhardy_Remacri.pth (upscale model, optional)
- TransparentBGSession+ (background removal)
- ComfyUI-Hunyuan3D custom nodes
- ~24GB+ VRAM recommended

### Custom Nodes Required
- **ComfyUI-Hunyuan3D** – Core 3D generation nodes
  - Hy3DModelLoader, Hy3DDelightImage, Hy3DSampleMultiView
  - Hy3DGenerateMeshMultiView, Hy3DMeshUVWrap, Hy3DBakeFromMultiview
  - Hy3DMeshVerticeInpaintTexture, Hy3DApplyTexture, Hy3DExportMesh
  - Hy3DRenderMultiView, Hy3DRenderSingleView, Hy3DPostprocessMesh
  - Hy3DCameraConfig, Hy3DVAEDecode, Hy3DDiffusersSchedulerConfig
  - DownloadAndLoadHy3DDelightModel, DownloadAndLoadHy3DPaintModel
- **ComfyUI-Impact-Pack** – ImageRemoveBackground+, MaskPreview+, etc.
- **ComfyUI_essentials** – ImageResize+, ImageStitch, etc.
- **comfyui-tooling-nodes** – Preview3D
- **FluxKontextImageScale** – For Kontext workflow

### Output
- Untextured mesh: `3D/Hy3D_XXXXX.glb`
- Textured mesh: `3D/Hy3D_textured_XXXXX.glb`
- Can be imported directly into Rhino via `Import` command
