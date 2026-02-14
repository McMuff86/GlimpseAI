namespace GlimpseAI.Models;

/// <summary>
/// Prompt mode selection for auto-prompt feature.
/// </summary>
public enum PromptMode
{
    /// <summary>User provides manual prompt text.</summary>
    Manual,
    
    /// <summary>Auto-prompt based on scene materials and viewport (no AI vision).</summary>
    AutoBasic,
    
    /// <summary>Auto-prompt using Florence2 vision model to analyze viewport.</summary>
    AutoVision
}