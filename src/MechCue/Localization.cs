using System.Globalization;
using System.Text.Json;

namespace MechCue;

public static class UiText
{
    static Dictionary<string, T> Read<T>(string name)
    {
        var assembly = typeof(UiText).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name)));
        return JsonSerializer.Deserialize<Dictionary<string, T>>(stream!)!;
    }
    static readonly Dictionary<string, string> english = Read<string>("en.json");
    static readonly Dictionary<string, string[]> commands = Read<string[]>("commands.json");
    static readonly string preferencePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MechCue", "ui-language.json");
    public static string Mode { get; private set; } = LoadMode();
    public static bool IsJapanese => Resolve(Mode, CultureInfo.CurrentUICulture) == "ja";
    public static string Resolve(string mode, CultureInfo windowsLanguage) => mode == "ja" ? "ja" : mode == "en" ? "en" : windowsLanguage.TwoLetterISOLanguageName == "ja" ? "ja" : "en";
    public static event Action? Changed;
    static string LoadMode()
    {
        try { var value = JsonSerializer.Deserialize<string>(File.ReadAllText(preferencePath)); return value is "ja" or "en" ? value : "auto"; }
        catch { return "auto"; }
    }
    public static void SetMode(string mode, bool save = true)
    {
        if (mode is not ("auto" or "ja" or "en")) throw new ArgumentException("Unsupported UI language");
        Mode = mode;
        if (save) { Directory.CreateDirectory(Path.GetDirectoryName(preferencePath)!); File.WriteAllText(preferencePath, JsonSerializer.Serialize(mode)); }
        Changed?.Invoke();
    }
    public static string Text(string text)
    {
        if (IsJapanese) return text;
        if (english.TryGetValue(text, out var exact)) return exact;
        foreach (var entry in english.OrderByDescending(e => e.Key.Length)) text = text.Replace(entry.Key, entry.Value, StringComparison.Ordinal);
        return text;
    }
    public static string CommandLabel(string key) => Text(commands.TryGetValue(key, out var item) ? item[0] : key);
    public static string CommandHint(string key) => commands.TryGetValue(key, out var item) ? item[IsJapanese ? 1 : 2] : Text(key);
}

public partial class MainForm
{
    readonly ToolTip commandHints = new() { ShowAlways = true, InitialDelay = 400, AutoPopDelay = 12000 };
    readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    readonly Dictionary<Control, string> sourceText = new();
    bool translating;
    string CurrentKind => kind.SelectedItem as string ?? "距離拘束";
    void ConfigureLanguage()
    {
        language.Items.AddRange(["自動 / Auto", "日本語", "English"]);
        language.SelectedIndex = UiText.Mode == "ja" ? 1 : UiText.Mode == "en" ? 2 : 0;
        language.SelectedIndexChanged += (_, _) => { if (!translating) Guard(() => UiText.SetMode(language.SelectedIndex == 1 ? "ja" : language.SelectedIndex == 2 ? "en" : "auto")); };
        kind.FormattingEnabled = true; kind.Format += (_, e) => e.Value = UiText.Text(Convert.ToString(e.ListItem) ?? "");
        target.FormattingEnabled = true; target.Format += (_, e) => e.Value = UiText.Text(Convert.ToString(e.ListItem) ?? "");
        WatchText(this);
        UiText.Changed += RefreshLanguage;
        Disposed += (_, _) => { UiText.Changed -= RefreshLanguage; commandHints.Dispose(); };
        RefreshLanguage();
    }
    void WatchText(Control control)
    {
        if (control == name || control == legend || control == language || control is ListControl or DataGridView or TextBoxBase or NumericUpDown) return;
        if (!sourceText.ContainsKey(control))
        {
            sourceText[control] = control.Text;
            control.TextChanged += (_, _) =>
            {
                if (translating) return;
                sourceText[control] = control.Text;
                ApplyText(control);
            };
            control.ControlAdded += (_, e) => { if (e.Control != null) WatchText(e.Control); };
            control.Disposed += (_, _) => sourceText.Remove(control);
        }
        foreach (Control child in control.Controls) WatchText(child);
        ApplyText(control);
    }
    void ApplyText(Control control)
    {
        if (!sourceText.TryGetValue(control, out var original)) return;
        bool previous = translating; translating = true;
        control.Text = control is Button && control.Tag is string key ? UiText.CommandLabel(key) : UiText.Text(original);
        if ((control.Parent == compactBar || control.Parent == top) && control is Button button)
        {
            if (button.Tag is "▶ 再生" or "停止" or "元に戻す" or "やり直す" or "最小表示")
            {
                button.Text = ""; button.AutoSize = false; button.Size = new(32, 28);
                button.AccessibleName = button.Tag is "やり直す" ? (UiText.IsJapanese ? "やり直す" : "Redo") : UiText.CommandLabel((string)button.Tag);
            }
            else if (Equals(button.Tag, "編集画面へ戻る")) button.Text = UiText.IsJapanese ? "戻る" : "Back";
            else if (Equals(button.Tag, "ウィンドウ位置")) button.Text = "▦";
        }
        if (control == live) control.Text = UiText.IsJapanese ? "反映" : "Apply";
        if (control == collision) control.Text = UiText.IsJapanese ? "干渉" : "Collision";
        if (control == loop) control.Text = UiText.IsJapanese ? "反復" : "Loop";
        if (control == autoApply) control.Text = UiText.IsJapanese ? "自動" : "Auto";
        if (control == reviewMode) control.Text = UiText.IsJapanese ? "表示" : "View";
        if (control.Tag is "menu-cad") control.Text = "CAD ▾";
        if (control.Tag is "menu-settings") control.Text = UiText.IsJapanese ? "設定 ▾" : "Settings ▾";
        if (control is Button) commandHints.SetToolTip(control, control.Tag is "やり直す" ? (UiText.IsJapanese ? "取り消したグラフ編集をやり直します。" : "Redo the undone graph edit.") : UiText.CommandHint(control.Tag as string ?? original));
        if (control is Button cornerButton && Equals(cornerButton.Tag, "ウィンドウ位置"))
        {
            cornerButton.AccessibleName = UiText.IsJapanese ? "表示位置" : "Window corner";
            commandHints.SetToolTip(cornerButton, UiText.IsJapanese ? "表示位置を選択。Ctrl+Shift+1〜9（テンキー配置）でも移動できます。" : "Choose position; Ctrl+Shift+1–9 uses numeric keypad layout.");
        }
        translating = previous;
    }
    void RefreshLanguage()
    {
        foreach (var control in sourceText.Keys.ToArray()) ApplyText(control);
        translating = true; language.SelectedIndex = UiText.Mode == "ja" ? 1 : UiText.Mode == "en" ? 2 : 0; translating = false;
        foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = UiText.Text(column.Name == "Time" ? "時間 [s]" : "変位 [mm] / 角度 [°]");
        bool previousLoading = loading; loading = true;
        try
        {
            foreach (var combo in new[] { kind, target, overlay })
            {
                var selected = combo.SelectedItem;
                var items = combo.Items.Cast<object>().ToArray();
                combo.BeginUpdate(); combo.Items.Clear(); combo.Items.AddRange(items);
                combo.SelectedItem = selected; combo.EndUpdate();
            }
        }
        finally { loading = previousLoading; }
        RefreshMenus();
        RefreshPlayback();
        FitFullToolbar();
        plot.RefreshLanguage();
        if (compact) FitCompactBar(); plot.Invalidate();
    }
    void StartPlayback()
    {
        try
        {
        Commit();
        double end = tracks.Max(t => t.Points[^1].Time);
        using (PauseCadReflection())
        {
            if ((double)time.Value >= end - 0.0005) time.Value = (decimal)tracks.Min(t => t.Points[0].Time);
            if (autoApply.Checked)
            {
                if (!bridge.Connected) throw new InvalidOperationException(UiText.IsJapanese ? "自動反映にはSolid Edgeへの接続が必要です。" : "Connect to Solid Edge before automatic Apply.");
                live.Checked = true;
            }
        }
        // Let a failed initial pose abort playback, rather than swallowing it
        // in the checkbox event and starting the timer after the failure.
        if (live.Checked) Drive((double)time.Value);
        playStart = (double)time.Value; watch.Restart(); timer.Start(); RefreshPlayback();
        plot.FollowTime(playStart);
        }
        catch { PausePlayback(); live.Checked = autoApply.Checked = false; throw; }
    }
}
