using Rhino;
using Rhino.Commands;
using Rhino.UI;
using GlimpseAI.UI;

namespace GlimpseAI.Commands;

/// <summary>
/// Command to open/toggle the Glimpse AI panel.
/// User types "Glimpse" in Rhino command line.
/// </summary>
public class GlimpseCommand : Command
{
    public static GlimpseCommand Instance { get; private set; }

    public GlimpseCommand()
    {
        Instance = this;
        // Register the panel in the command constructor (correct Rhino pattern)
        Panels.RegisterPanel(PlugIn, typeof(GlimpsePanel), "Glimpse AI", null);
    }

    public override string EnglishName => "Glimpse";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var panelId = GlimpsePanel.PanelId;
        bool visible = Panels.IsPanelVisible(panelId);

        if (visible)
            Panels.ClosePanel(panelId);
        else
            Panels.OpenPanel(panelId);

        return Result.Success;
    }
}
