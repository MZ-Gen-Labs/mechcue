namespace MechCue;

public partial class MainForm
{
    bool fullToolbarReady, fittingFullToolbar;
    void ConfigureFullToolbar()
    {
        fullToolbarReady = true;
        top.SizeChanged += (_, _) => FitFullToolbar();
        FitFullToolbar();
    }
    void FitFullToolbar()
    {
        if (!fullToolbarReady || compact || fittingFullToolbar) return;
        fittingFullToolbar = true;
        top.SuspendLayout();
        try
        {
            top.WrapContents = false;
            bool narrow = top.ClientSize.Width < 1100;
            time.Width = narrow ? 80 : 85; speed.Width = narrow ? 50 : 55;
            foreach (Control control in top.Controls)
            {
                control.Margin = new Padding(narrow ? 0 : 1, 3, narrow ? 0 : 1, 3);
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
        finally { top.ResumeLayout(true); fittingFullToolbar = false; }
    }
}
