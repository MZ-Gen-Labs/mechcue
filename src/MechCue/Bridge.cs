using System.Reflection;
using System.Runtime.InteropServices;

namespace MechCue;

public sealed record Target(string Label, object Com, string Property, string? Display = null)
{
    public override string ToString() => Display ?? Label;
}
public sealed class Bridge
{
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)] static extern int CLSIDFromProgID(string id, out Guid clsid);
    [DllImport("oleaut32.dll", PreserveSig = false)] static extern void GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object app);
    object? app, doc;
    readonly object? hostedApplication;
    readonly Dictionary<Track, (Target Target, object Original)> bindings = new();
    public Bridge() { }
    public Bridge(object hostedApplication) { this.hostedApplication = hostedApplication; }
    internal Bridge(object application, object document) { app = application; doc = document; }
    public bool Connected => doc != null;
    public int BindingCount => bindings.Count;
    public string? BoundLabel(Track track) => bindings.TryGetValue(track, out var b) ? b.Target.Label : null;
    public List<Target> TargetsFromSelection(string kind)
    {
        Check();
        var selection = Get(doc!, "SelectSet");
        if (Convert.ToInt32(Get(selection, "Count")) != 1) throw new InvalidOperationException("Solid Edgeでトップレベルの部品を1つ選択してください。");
        var selected = GetItem(selection, 1);
        var parts = Targets("部品移動");
        var part = parts.FirstOrDefault(p => Equals(p.Com, selected));
        if (part == null) throw new InvalidOperationException("面やエッジではなく、パスファインダでトップレベルの部品を選んでください。");
        if (kind.StartsWith("部品")) return [part];
        var related = Get(part.Com, "Relations3d");
        var relations = new List<object>();
        for (int i = 1; i <= Convert.ToInt32(Get(related, "Count")); i++) relations.Add(GetItem(related, i));
        return Targets(kind).Where(t => relations.Any(r => Equals(r, t.Com))).ToList();
    }
    public void Highlight(Target target)
    {
        Check();
        var selection = Get(doc!, "SelectSet"); Call(selection, "RemoveAll"); Call(selection, "Add", target.Com);
    }
    public string LoadRegisteredAddIn()
    {
        Check();
        var addIns = Get(app!, "AddIns");
        try { Call(addIns, "Update"); }
        catch (TargetInvocationException ex) when (ex.InnerException?.HResult == unchecked((int)0x80004001)) { /* Solid Edge 2026 does not implement refresh. */ }
        var addIn = addIns.GetType().InvokeMember("Item", BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, addIns, ["{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}"])
            ?? throw new InvalidOperationException("新しいアドイン登録がSolid Edgeにまだ読み込まれていません。作業を保存してSolid Edgeを再起動してください。");
        Set(addIn, "Connect", true);
        return "Add-in Connect = " + Get(addIn, "Connect");
    }
    public string InspectAddIns()
    {
        Check(); var collection = Get(app!, "AddIns"); var lines = new List<string>();
        for (int i = 1; i <= Convert.ToInt32(Get(collection, "Count")); i++)
        {
            var item = GetItem(collection, i);
            lines.Add($"{Get(item, "GUID")} | {Get(item, "Description")} | Connect={Get(item, "Connect")}");
        }
        return string.Join(Environment.NewLine, lines);
    }
    public string Inspect()
    {
        Check();
        var lines = new List<string> { Convert.ToString(Get(doc!, "FullName")) ?? "アセンブリ" };
        foreach (string kind in new[] { "距離拘束", "角度拘束", "部品移動" })
            foreach (var t in Targets(kind))
                lines.Add($"{kind}: {t.Label} = {(t.Property == "Matrix" ? "relations:" + Get(Get(t.Com, "Relations3d"), "Count") : Get(t.Com, t.Property))}");
        return string.Join(Environment.NewLine, lines);
    }
    public string TestDistance()
    {
        Check();
        var target = Targets("距離拘束").FirstOrDefault(t => Math.Abs(Convert.ToDouble(Get(t.Com, t.Property))) > 0.0001)
            ?? throw new InvalidOperationException("小さな変更で確認できる距離拘束がありません。");
        double original = Convert.ToDouble(Get(target.Com, target.Property));
        var track = new Track { Points = [new(0, original * 1000), new(1, original * 1000 + 1)] };
        var parts = Targets("部品移動");
        var before = parts.Select(t => Matrix(t.Com)).ToList();
        Bind(track, target);
        string result;
        try
        {
            Apply(1);
            double actual = Convert.ToDouble(Get(target.Com, target.Property));
            if (Math.Abs(actual - original - 0.001) > 1e-8) throw new InvalidOperationException("拘束値が指定値になりませんでした。");
            var after = parts.Select(t => Matrix(t.Com)).ToList();
            var changed = parts.Where((t, i) => before[i].Zip(after[i]).Any(p => Math.Abs(p.First - p.Second) > 1e-8)).Select(t => t.Label);
            result = $"{target.Label}: {original * 1000:0.###} → {actual * 1000:0.###} mm\n姿勢が変化した部品: {string.Join(", ", changed)}";
        }
        finally { Restore(); }
        double restored = Convert.ToDouble(Get(target.Com, target.Property));
        if (Math.Abs(restored - original) > 1e-8) throw new InvalidOperationException("復元後の距離が元の値と一致しません。");
        for (int i = 0; i < parts.Count; i++)
            if (before[i].Zip(Matrix(parts[i].Com)).Any(p => Math.Abs(p.First - p.Second) > 1e-7))
                throw new InvalidOperationException("部品姿勢が元に戻っていません: " + parts[i].Label);
        bindings.Clear();
        return result + "\n復元確認: 距離と全部品の姿勢が元の値に一致。保存は行っていません。";
    }
    static object Get(object o, string p) => o.GetType().InvokeMember(p, BindingFlags.GetProperty, null, o, null)!;
    static void Set(object o, string p, object v) => o.GetType().InvokeMember(p, BindingFlags.SetProperty, null, o, [v]);
    static object Call(object o, string p, params object[] args) => o.GetType().InvokeMember(p, BindingFlags.InvokeMethod, null, o, args)!;
    public string Connect()
    {
        if (Connected)
        {
            try { Check(false); }
            catch (InvalidOperationException) when (!Connected) { }
            if (Connected) throw new InvalidOperationException("接続中です。基準状態に戻して切断してから再接続してください。");
        }
        object application;
        if (hostedApplication != null) application = hostedApplication;
        else
        {
            Marshal.ThrowExceptionForHR(CLSIDFromProgID("SolidEdge.Application", out var id));
            GetActiveObject(ref id, IntPtr.Zero, out application);
        }
        var document = Get(application, "ActiveDocument");
        // Validate the assembly interface without changing the model.
        Get(document, "Occurrences"); Get(document, "Relations3d");
        app = application; doc = document;
        return Convert.ToString(Get(doc, "Name")) ?? "アセンブリ";
    }
    public List<Target> Targets(string kind)
    {
        Check(); var list = new List<Target>();
        bool part = kind.StartsWith("部品");
        var collection = Get(doc!, part ? "Occurrences" : "Relations3d");
        for (int i = 1; i <= Convert.ToInt32(Get(collection, "Count")); i++)
        {
            var item = GetItem(collection, i);
            string property = part ? "Matrix" : kind == "距離拘束" ? "Offset" : "Angle";
            try
            {
                if (!part) Get(item, property);
                string label = part ? Convert.ToString(Get(item, "Name"))! : $"拘束 #{i} / {property}";
                string display = label;
                if (!part)
                {
                    double value = Convert.ToDouble(Get(item, property)) * (property == "Angle" ? 180 / Math.PI : 1000);
                    string unit = property == "Angle" ? "°" : "mm";
                    string names = "";
                    try { names = $" / {Get(Get(item, "Occurrence1"), "Name")} ↔ {Get(Get(item, "Occurrence2"), "Name")}"; } catch { }
                    display = $"{label}: {value:0.###} {unit}{names}";
                }
                list.Add(new Target(label, item, property, display));
            }
            catch (Exception) { /* The relation does not expose the required property. */ }
        }
        return list;
    }
    static object GetItem(object c, int i) => c.GetType().InvokeMember("Item", BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, c, [i])!;
    static double[] Matrix(object o)
    {
        object[] args = [new double[16]];
        var modifier = new ParameterModifier(1); modifier[0] = true;
        o.GetType().InvokeMember("GetMatrix", BindingFlags.InvokeMethod, null, o, args, [modifier], null, null);
        return ((Array)args[0]).Cast<double>().ToArray();
    }
    public void Bind(Track track, Target target)
    {
        Check();
        track.Validate();
        string expected = track.Kind.StartsWith("部品") ? "Matrix" : track.Kind == "角度拘束" ? "Angle" : "Offset";
        if (target.Property != expected) throw new InvalidOperationException("駆動方法と選択した対象が一致しません。");
        if (bindings.TryGetValue(track, out var old) && Equals(old.Target.Com, target.Com)) return;
        if (bindings.Any(pair => !ReferenceEquals(pair.Key, track) && Equals(pair.Value.Target.Com, target.Com))) throw new InvalidOperationException("この対象は別の機構に登録済みです。既存の割り当ては維持しています。");
        if (target.Property == "Matrix" && Convert.ToInt32(Get(Get(target.Com, "Relations3d"), "Count")) > 0)
            throw new InvalidOperationException("拘束がある部品は直接駆動できません。拘束駆動を選ぶか、Solid Edgeで直接駆動用の自由な部品を用意してください。");
        // Validate the new target before changing the previous driver.
        if (target.Property == "Matrix") Matrix(target.Com); else Get(target.Com, target.Property);
        if (bindings.ContainsKey(track)) RestoreBinding(old);
        var original = target.Property == "Matrix" ? (object)Matrix(target.Com) : Get(target.Com, target.Property);
        bindings[track] = (target, original);
    }
    void RestoreBinding((Target Target, object Original) binding)
    {
        if (binding.Target.Property == "Matrix") Call(binding.Target.Com, "PutMatrix", binding.Original, true);
        else Set(binding.Target.Com, binding.Target.Property, binding.Original);
    }
    public void Unbind(Track track)
    {
        Check(false);
        if (!bindings.TryGetValue(track, out var binding)) return;
        RestoreBinding(binding);
        bindings.Remove(track);
        Call(Get(Get(app!, "ActiveWindow"), "View"), "Update");
    }
    void ForgetConnection() { bindings.Clear(); doc = null; app = null; }
    void Check(bool requireActive = true)
    {
        if (doc == null || app == null) throw new InvalidOperationException("先にSolid Edgeに接続してください。");
        try
        {
            var documents = Get(app, "Documents");
            bool open = false;
            for (int i = 1; i <= Convert.ToInt32(Get(documents, "Count")); i++)
                if (Equals(GetItem(documents, i), doc)) { open = true; break; }
            if (!open)
            {
                ForgetConnection();
                throw new InvalidOperationException("接続していたアセンブリが閉じられました。古い接続を解除しました。アセンブリを開いて再接続・駆動先の再登録をしてください。");
            }
            Get(doc, "Occurrences");
            if (requireActive && !Equals(Get(app, "ActiveDocument"), doc))
                throw new InvalidOperationException("接続時のアセンブリをアクティブにしてください。切断・終了はそのまま行えます。");
        }
        catch (Exception ex) when (IsLostConnection(ex))
        {
            ForgetConnection();
            throw new InvalidOperationException("Solid Edgeとの接続が失われたため、古い接続を解除しました。アセンブリを開いて再接続・駆動先の再登録をしてください。", ex);
        }
    }
    static bool IsLostConnection(Exception ex)
    {
        while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
        return ex is InvalidComObjectException || ex is COMException com &&
            com.HResult != unchecked((int)0x80010001) && com.HResult != unchecked((int)0x8001010A);
    }
    public void Apply(double time)
    {
        Check();
        if (bindings.Count == 0) throw new InvalidOperationException("駆動先が未登録です。グラフを選び、対象を選択して『駆動先を登録』を押してください。");
        foreach (var (track, b) in bindings)
        {
            double value = track.At(time);
            if (b.Target.Property == "Matrix") Call(b.Target.Com, "PutMatrix", Transform.Apply((double[])b.Original, track.Kind, track.Axis, value), true);
            else Set(b.Target.Com, b.Target.Property, value * (b.Target.Property == "Angle" ? Math.PI / 180 : 0.001));
        }
        Call(Get(Get(app!, "ActiveWindow"), "View"), "Update");
    }
    public void Restore()
    {
        Check(false);
        foreach (var b in bindings.Values)
            if (b.Target.Property == "Matrix") Call(b.Target.Com, "PutMatrix", b.Original, true);
            else Set(b.Target.Com, b.Target.Property, b.Original);
        Call(Get(Get(app!, "ActiveWindow"), "View"), "Update");
    }
    public string? Disconnect()
    {
        if (!Connected) return null;
        try { Restore(); return null; }
        catch (Exception ex) { return "接続は解除しました。基準状態の復元は確認できませんでした：" + (ex.InnerException ?? ex).Message; }
        finally { ForgetConnection(); }
    }
}
