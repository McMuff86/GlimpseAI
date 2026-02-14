using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using GlimpseAI.Models;

namespace GlimpseAI.Services;

/// <summary>
/// Builds auto-prompts from scene materials and style presets.
/// </summary>
public static class AutoPromptBuilder
{
    /// <summary>Style-Preset Templates mapping each preset to prefix and suffix text.</summary>
    private static readonly Dictionary<StylePreset, (string prefix, string suffix)> StyleTemplates = new()
    {
        [StylePreset.Architecture] = ("", "photorealistic architectural rendering, professional architectural photography, natural lighting, ambient occlusion, 8k uhd"),
        [StylePreset.Artistic] = ("", "oil painting style, dramatic lighting, artistic interpretation, masterpiece, gallery quality"),
        [StylePreset.Textured] = ("", "highly detailed textures, PBR materials, wood grain, concrete texture, glass reflections, metal surfaces, 8k uhd"),
        [StylePreset.Dramatic] = ("", "golden hour lighting, dramatic shadows, cinematic composition, volumetric light, lens flare, 8k"),
        [StylePreset.Minimal] = ("", "clean minimalist design, scandinavian style, white space, soft diffused lighting, elegant simplicity"),
        [StylePreset.Nature] = ("", "lush vegetation, landscape integration, green architecture, trees, plants, photorealistic, natural environment"),
        [StylePreset.Interior] = ("", "interior design, cozy atmosphere, warm lighting, furniture, decoration, architectural digest style"),
        [StylePreset.Custom] = ("", "")
    };

    /// <summary>
    /// Builds an auto-prompt from scene materials + style preset.
    /// </summary>
    /// <param name="doc">Rhino document to analyze.</param>
    /// <param name="style">Style preset to apply.</param>
    /// <param name="customSuffix">Custom suffix when style is Custom.</param>
    /// <returns>Generated prompt text.</returns>
    public static string BuildFromScene(RhinoDoc doc, StylePreset style, string customSuffix = "")
    {
        var parts = new List<string>();
        
        // Detect materials in the scene
        var materialKeywords = DetectMaterials(doc);
        if (materialKeywords.Count > 0)
            parts.Add(string.Join(", ", materialKeywords));
        
        // Detect viewport type
        var viewType = DetectViewType(doc);
        if (!string.IsNullOrEmpty(viewType))
            parts.Add(viewType);
        
        // Add style template
        var template = StyleTemplates[style];
        if (!string.IsNullOrEmpty(template.prefix))
            parts.Insert(0, template.prefix);
        
        if (style == StylePreset.Custom && !string.IsNullOrEmpty(customSuffix))
            parts.Add(customSuffix);
        else if (!string.IsNullOrEmpty(template.suffix))
            parts.Add(template.suffix);
        
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Builds a prompt using Florence2 vision caption + style preset.
    /// Returns the prompt text to use.
    /// </summary>
    /// <param name="caption">Vision-generated caption from Florence2.</param>
    /// <param name="style">Style preset to apply.</param>
    /// <param name="customSuffix">Custom suffix when style is Custom.</param>
    /// <returns>Combined prompt text.</returns>
    public static string CombineVisionCaption(string caption, StylePreset style, string customSuffix = "")
    {
        var parts = new List<string>();
        
        if (!string.IsNullOrEmpty(caption))
            parts.Add(caption);
        
        var template = StyleTemplates[style];
        if (style == StylePreset.Custom && !string.IsNullOrEmpty(customSuffix))
            parts.Add(customSuffix);
        else if (!string.IsNullOrEmpty(template.suffix))
            parts.Add(template.suffix);
        
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Detects materials in the scene and converts them to prompt keywords.
    /// </summary>
    /// <param name="doc">Rhino document to analyze.</param>
    /// <returns>List of material-based keywords.</returns>
    private static List<string> DetectMaterials(RhinoDoc doc)
    {
        var keywords = new List<string>();
        if (doc == null) return keywords;
        
        var materialTable = doc.Materials;
        foreach (var mat in materialTable)
        {
            var name = mat.Name?.ToLowerInvariant() ?? "";
            if (name.Contains("wood") || name.Contains("holz")) 
                keywords.Add("warm wood textures, natural grain");
            if (name.Contains("glass") || name.Contains("glas")) 
                keywords.Add("crystal clear glass, reflections");
            if (name.Contains("concrete") || name.Contains("beton")) 
                keywords.Add("exposed concrete, brutalist texture");
            if (name.Contains("metal") || name.Contains("metall")) 
                keywords.Add("brushed metal surfaces");
            if (name.Contains("stone") || name.Contains("stein")) 
                keywords.Add("natural stone texture");
            if (name.Contains("brick") || name.Contains("ziegel")) 
                keywords.Add("brick wall texture");
            if (name.Contains("plaster") || name.Contains("putz")) 
                keywords.Add("smooth plaster walls");
        }
        
        // Deduplicate
        return keywords.Distinct().ToList();
    }

    /// <summary>
    /// Detects the current viewport type for prompt enhancement.
    /// </summary>
    /// <param name="doc">Rhino document to analyze.</param>
    /// <returns>View type description or empty string.</returns>
    private static string DetectViewType(RhinoDoc doc)
    {
        var view = doc?.Views?.ActiveView;
        if (view == null) return "";
        
        var vp = view.ActiveViewport;
        if (vp.IsPerspectiveProjection)
            return "perspective view, architectural visualization";
        if (vp.IsParallelProjection)
        {
            var name = vp.Name?.ToLowerInvariant() ?? "";
            if (name.Contains("top")) return "top view, floor plan visualization";
            if (name.Contains("front")) return "front elevation, architectural drawing";
            if (name.Contains("right") || name.Contains("left")) return "side elevation";
        }
        return "";
    }
}