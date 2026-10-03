namespace MechCue;

public partial class MainForm
{
    bool fullToolbarReady;
    void ConfigureFullToolbar()
    {
        fullToolbarReady = true;
        FitFullToolbar();
    }
    void FitFullToolbar()
    {
        if (!fullToolbarReady || compact) return;
        top.SuspendLayout();
        try
        {
            top.WrapContents = false;
            time.Width = 85; speed.Width = 55;
            foreach (Control control in top.Controls)
            {
                control.Margin = new Padding(1, 3, 1, 3);
                if (control is Button button && button.Tag is "menu-cad" or "menu-settings" or "データ ▾" or "表示 ▾")
                {
                    button.AutoSize = false;
                    button.Size = new Size(Math.Max(43, TextRenderer.MeasureText(button.Text, button.Font).Width + 8), 28);
                }
                if (control is FlowLayoutPanel modes)
                {
                    modes.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    foreach (Control mode in modes.Controls) mode.Margin = new Padding(1, 3, 1, 3);
                }
            }
        }
        finally { top.ResumeLayout(true); }
    }
}
