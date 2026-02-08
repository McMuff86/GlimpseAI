using Rhino;
using Rhino.Commands;
using Rhino.UI;
using GlimpseAI.UI;

namespace GlimpseAI.Commands;

/// <summary>
/// Command to open the Glimpse AI settings dialog.
/// User types "GlimpseSettings" in Rhino command line.
/// </summary>
public class GlimpseSettingsCommand : Command
{
    public static GlimpseSettingsCommand Instance { get; private set; }

    public GlimpseSettingsCommand()
    {
        Instance = this;
    }

    public override string EnglishName => "GlimpseSettings";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var dialog = new GlimpseSettingsDialog();
        dialog.ShowModal(RhinoEtoApp.MainWindow);
        return Result.Success;
    }
}
