using System;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;

namespace GlimpseAI.UI;

/// <summary>
/// Main dockable panel for Glimpse AI.
/// Displays the AI-rendered preview and controls.
/// </summary>
[System.Runtime.InteropServices.Guid("B4E8C1D3-2F5A-4E7B-8C9D-3A1F6E5D4B2C")]
public class GlimpsePanel : Panel, IPanel
{
    private readonly uint _documentSerialNumber;

    /// <summary>
    /// Gets the panel GUID for registration.
    /// </summary>
    public static Guid PanelId => typeof(GlimpsePanel).GUID;

    /// <summary>
    /// Creates a new Glimpse AI panel.
    /// </summary>
    /// <param name="documentSerialNumber">The document this panel is associated with.</param>
    public GlimpsePanel(uint documentSerialNumber)
    {
        _documentSerialNumber = documentSerialNumber;
        Content = BuildUI();
    }

    /// <summary>
    /// Builds the initial panel UI layout.
    /// This is a placeholder — full UI will be built in the UI agent task.
    /// </summary>
    private Control BuildUI()
    {
        var layout = new DynamicLayout();
        layout.BeginVertical();
        layout.AddRow(new Label
        {
            Text = "Glimpse AI Preview",
            Font = new Font(SystemFont.Bold, 12),
            TextAlignment = TextAlignment.Center
        });
        layout.AddRow(new Label
        {
            Text = "Panel loaded. Full UI coming soon.",
            TextAlignment = TextAlignment.Center,
            TextColor = Colors.Gray
        });
        layout.EndVertical();
        layout.Add(null); // spacer

        return layout;
    }

    #region IPanel Implementation

    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
    {
        RhinoApp.WriteLine("Glimpse AI panel shown.");
    }

    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason)
    {
        // Pause updates when hidden
    }

    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
    {
        // Cleanup
    }

    #endregion
}
