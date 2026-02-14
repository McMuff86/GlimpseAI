namespace GlimpseAI.Models;

/// <summary>
/// Style presets for auto-prompt generation, combining with scene analysis.
/// </summary>
public enum StylePreset
{
    /// <summary>Photorealistic architectural rendering style.</summary>
    Architecture,
    
    /// <summary>Oil painting, dramatic, artistic interpretation.</summary>
    Artistic,
    
    /// <summary>Highly detailed PBR textures and materials.</summary>
    Textured,
    
    /// <summary>Golden hour, cinematic, volumetric light.</summary>
    Dramatic,
    
    /// <summary>Clean minimalist, scandinavian, soft light.</summary>
    Minimal,
    
    /// <summary>Vegetation, landscape integration.</summary>
    Nature,
    
    /// <summary>Interior design, cozy, warm lighting.</summary>
    Interior,
    
    /// <summary>User-defined custom style suffix.</summary>
    Custom
}