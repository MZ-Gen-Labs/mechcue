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
        top.Controls.Add(new Label { Text = "言語", AutoSize = true }); top.Controls.Add(language);
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
        if (control.Parent == compactBar && control is Button button)
        {
            if (Equals(button.Tag, "▶ 再生") || Equals(button.Tag, "停止")) button.Text = "";
            else if (Equals(button.Tag, "編集画面へ戻る")) button.Text = UiText.IsJapanese ? "戻る" : "Back";
        }
        if (compact && control == live) control.Text = UiText.IsJapanese ? "反映" : "Apply";
        if (compact && control == collision) control.Text = UiText.IsJapanese ? "干渉" : "Collision";
        if (compact && control == loop) control.Text = UiText.IsJapanese ? "反復" : "Loop";
        if (control is Button) commandHints.SetToolTip(control, UiText.CommandHint(control.Tag as string ?? original));
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
            foreach (var combo in new[] { kind, target })
            {
                var selected = combo.SelectedItem;
                var items = combo.Items.Cast<object>().ToArray();
                combo.BeginUpdate(); combo.Items.Clear(); combo.Items.AddRange(items);
                combo.SelectedItem = selected; combo.EndUpdate();
            }
        }
        finally { loading = previousLoading; }
        plot.RefreshLanguage();
        if (compact) FitCompactBar(); plot.Invalidate();
    }
    void StartPlayback()
    {
        Commit();
        double end = tracks.Max(t => t.Points[^1].Time);
        if ((double)time.Value >= end - 0.0005) time.Value = (decimal)tracks.Min(t => t.Points[0].Time);
        playStart = (double)time.Value; watch.Restart(); timer.Start();
    }
}
