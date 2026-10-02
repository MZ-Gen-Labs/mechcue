using System.Reflection;
using System.Runtime.InteropServices;

namespace MechCue;

public sealed record Target(string Label, object Com, string Property, string? Display = null)
{
    public override string ToString() => Display ?? Label;
}
public sealed partial class Bridge
{
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)] static extern int CLSIDFromProgID(string id, out Guid clsid);
    [DllImport("oleaut32.dll", PreserveSig = false)] static extern void GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object app);
    object? app, doc;
    readonly object? hostedApplication;
    sealed record Binding(Target Target, object Original, List<(object Relation, bool Suppress)> Grounds) { public bool Active { get; set; } = true; }
    readonly Dictionary<Track, Binding> bindings = new();
    public Bridge() { }
    public Bridge(object hostedApplication) { this.hostedApplication = hostedApplication; }
    internal Bridge(object application, object document) { app = application; doc = document; }
    public bool Connected => doc != null;
    public int BindingCount => bindings.Count + conceptTracks.Count;
    public string? BoundLabel(Track track) => ConceptLabel(track) ?? (bindings.TryGetValue(track, out var b) ? b.Target.Label : null);
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
    public void ClearSelection() { Check(); Call(Get(doc!, "SelectSet"), "RemoveAll"); }
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
    public void Bind(Track track, Target target, bool activate = true, bool conceptNested = false)
    {
        Check();
        if(concept!=null && !conceptNested) throw new InvalidOperationException("概略軸の登録中は通常の駆動先を追加できません。");
        track.Validate();
        string expected = track.Kind.StartsWith("部品") ? "Matrix" : track.Kind == "角度拘束" ? "Angle" : "Offset";
        if (target.Property != expected) throw new InvalidOperationException("駆動方法と選択した対象が一致しません。");
        if (bindings.TryGetValue(track, out var old) && Equals(old.Target.Com, target.Com)) return;
        if (bindings.Any(pair => !ReferenceEquals(pair.Key, track) && Equals(pair.Value.Target.Com, target.Com))) throw new InvalidOperationException("この対象は別の機構に登録済みです。既存の割り当ては維持しています。");
        var grounds = new List<(object Relation, bool Suppress)>();
        if (target.Property == "Matrix")
        {
            bool hasOtherConstraints = false;
            var relations = Get(target.Com, "Relations3d");
            for (int i = 1; i <= Convert.ToInt32(Get(relations, "Count")); i++)
            {
                var relation = GetItem(relations, i);
                // SolidEdgeFramework.ObjectType.igGroundRelation3d (Siemens API).
                if (Convert.ToInt32(Get(relation, "Type")) == 1959028688)
                    grounds.Add((relation, Convert.ToBoolean(Get(relation, "Suppress"))));
                else hasOtherConstraints = true;
            }
            if (hasOtherConstraints && !(track.Kind == "部品座標" && grounds.Count > 0))
                throw new InvalidOperationException("固定拘束がある部品は『部品座標』で登録できます。固定拘束がない部品の他の拘束は、拘束駆動を選んでください。");
        }
        // Validate the new target before changing the previous driver.
        if (target.Property == "Matrix") Matrix(target.Com); else Get(target.Com, target.Property);
        if (old != null) RestoreBinding(old);
        var original = target.Property == "Matrix" ? (object)Matrix(target.Com) : Get(target.Com, target.Property);
        try
        {
            if (activate) foreach (var g in grounds) Set(g.Relation, "Suppress", true);
            bindings[track] = new Binding(target, original, grounds) { Active = activate };
            unresolved.Remove(track);
        }
        catch
        {
            foreach (var g in grounds) Set(g.Relation, "Suppress", g.Suppress);
            if (old is { Active: true }) foreach (var g in old.Grounds) Set(g.Relation, "Suppress", true);
            throw;
        }
    }
    void RestoreBinding(Binding binding)
    {
        if (!binding.Active) return;
        var errors = new List<Exception>();
        try
        {
            if (binding.Target.Property == "Matrix") Call(binding.Target.Com, "PutMatrix", binding.Original, true);
            else Set(binding.Target.Com, binding.Target.Property, binding.Original);
        }
        catch (Exception ex) { errors.Add(ex); }
        foreach (var g in binding.Grounds)
            try { Set(g.Relation, "Suppress", g.Suppress); } catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0) throw new AggregateException("基準姿勢または固定拘束を復元できませんでした。", errors);
    }
    public void Unbind(Track track)
    {
        Check(false);
        if(IsConcept(track)) throw new InvalidOperationException("概略軸の個別解除はできません。切断で一括解除できます。");
        unresolved.Remove(track);
        if (!bindings.TryGetValue(track, out var binding)) return;
        RestoreBinding(binding);
        bindings.Remove(track);
        nestedKeyPaths.Remove(track);
        Call(Get(Get(app!, "ActiveWindow"), "View"), "Update");
    }
    void ForgetConnection() { DetachDocumentEvents(); ClearConcept(); bindings.Clear(); nestedKeyPaths.Clear(); unresolved.Clear(); doc = null; app = null; }
    void Check(bool requireActive = true)
    {
        if (doc == null || app == null) throw new InvalidOperationException("先にSolid Edgeに接続してください。");
        try
        {
            var documents = Get(app, "Documents");
            bool open = false;
            for (int i = 1; i <= Convert.ToInt32(Get(documents, "Count")); i++)
                if (ApplicationEventSink.SameDocument(GetItem(documents, i), doc)) { open = true; break; }
            if (!open)
            {
                ForgetConnection();
                throw new InvalidOperationException("接続していたアセンブリが閉じられました。古い接続を解除しました。アセンブリを開いて再接続・駆動先の再登録をしてください。");
            }
            Get(doc, "Occurrences");
            if (requireActive && !ApplicationEventSink.SameDocument(Get(app, "ActiveDocument"), doc))
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
    public double CurrentValue(Track track, Target target)
    {
        Check();
        if (target.Property != "Matrix")
            return Convert.ToDouble(Get(target.Com, target.Property)) * (target.Property == "Angle" ? 180 / Math.PI : 1000);
        if (track.Kind == "部品座標") return Matrix(target.Com)[12 + (track.Axis == "X" ? 0 : track.Axis == "Y" ? 1 : 2)] * 1000;
        // Existing relative modes use the registered pose as their zero.
        if (bindings.TryGetValue(track, out var binding) && Equals(binding.Target.Com, target.Com))
        {
            var current = Matrix(target.Com); var basis = (double[])binding.Original;
            int a = track.Axis == "X" ? 0 : track.Axis == "Y" ? 1 : 2;
            if (track.Kind == "部品移動") return (current[12 + a] - basis[12 + a]) * 1000;
            int u = (a + 1) % 3, v = (a + 2) % 3;
            double sine = 0, cosine = 0;
            for (int row = 0; row < 3; row++)
            {
                int offset = row * 4;
                sine += basis[offset + u] * current[offset + v] - basis[offset + v] * current[offset + u];
                cosine += basis[offset + u] * current[offset + u] + basis[offset + v] * current[offset + v];
            }
            return Math.Atan2(sine, cosine) * 180 / Math.PI;
        }
        return 0;
    }
    public void ApplyChecked(double time)
    {
        Check();
        var conceptBefore = ConceptSnapshot();
        var before = bindings.Values.Select(b => (b.Target, Value: b.Target.Property == "Matrix" ? (object)Matrix(b.Target.Com) : Get(b.Target.Com, b.Target.Property))).ToList();
        // Refuse to start from an interfering or unverified position.
        EnsureNoInterference();
        try { Apply(time); EnsureNoInterference(); }
        catch (Exception failure)
        {
            try
            {
                RestoreConceptSnapshot(conceptBefore);
                foreach (var b in before)
                    if (b.Target.Property == "Matrix") Call(b.Target.Com, "PutMatrix", b.Value, true);
                    else Set(b.Target.Com, b.Target.Property, b.Value);
                Call(Get(Get(app!, "ActiveWindow"), "View"), "Update");
            }
            catch (Exception rollback) { throw new InvalidOperationException("停止しましたが直前の姿勢へ戻せませんでした。Solid Edgeを確認してください。 " + failure.Message, rollback); }
            throw;
        }
    }
    internal static Array InterferenceSet(object[] parts)
    {
        // VB Object() is SAFEARRAY(IDispatch), not SAFEARRAY(VARIANT).
        // Solid Edge also supports non-zero-based automation arrays.
        var set = Array.CreateInstance(typeof(DispatchWrapper), [parts.Length], [1]);
        for (int i = 0; i < parts.Length; i++) set.SetValue(new DispatchWrapper(parts[i]), i + 1);
        return set;
    }
    void EnsureNoInterference()
    {
        var occurrences = Get(doc!, "Occurrences");
        var parts = Enumerable.Range(1, Convert.ToInt32(Get(occurrences, "Count"))).Select(i => GetItem(occurrences, i)).ToArray();
        if (parts.Length == 0) throw new InvalidOperationException("干渉チェック対象の部品がありません。");
        object[] args = [parts.Length, InterferenceSet(parts), 0, Type.Missing, Type.Missing, Type.Missing, false, Type.Missing, Type.Missing, 0, null!, null!, null!, null!, Type.Missing];
        var modifier = new ParameterModifier(args.Length);
        foreach (int i in new[] { 1, 2, 9, 10, 11, 12, 13 }) modifier[i] = true;
        try { doc!.GetType().InvokeMember("CheckInterference", BindingFlags.InvokeMethod, null, doc, args, [modifier], null, null); }
        catch (Exception ex) when ((ex.InnerException ?? ex).HResult == unchecked((int)0x80020005))
        {
            throw new InvalidOperationException("干渉チェックを実行できませんでした（Solid Edge APIの引数型不一致）。干渉の有無は未確認です。", ex);
        }
        int status = Convert.ToInt32(args[2]);
        if (status == 1) return;
        if (status is 2 or 3 or 4)
        {
            var names = new List<string>();
            foreach (int index in new[] { 10, 11 })
                if (args[index] is Array array)
                    foreach (var part in array) { try { names.Add(Convert.ToString(Get(part!, "Name")) ?? "部品"); } catch { names.Add("名称取得不可"); } }
            throw new InvalidOperationException("干渉または干渉の可能性を検出したため停止しました。 " + string.Join(" / ", names.Distinct().Take(8)));
        }
        throw new InvalidOperationException("干渉チェックを完了できなかったため停止しました。解析状態: " + status);
    }
    public void Apply(double time)
    {
        Check();
        if (BindingCount == 0) throw new InvalidOperationException("駆動先が未登録です。グラフを選び、対象を選択して『駆動先を登録』を押してください。");
        foreach(var (track,keyPath) in nestedKeyPaths)
        {
            var (occurrence,parent)=ResolveOccurrenceKeyPath(doc!,keyPath);
            if(!Equals(occurrence,bindings[track].Target.Com)||Convert.ToBoolean(Get(parent,"ReadOnly")))throw new InvalidOperationException("Nested identity changed or parent is read-only; stop and reassign");
        }
        ApplyConcept(time);
        foreach (var (track, b) in bindings)
        {
            double value = track.At(time);
            b.Active = true;
            foreach (var ground in b.Grounds) Set(ground.Relation, "Suppress", true);
            if (b.Target.Property == "Matrix") Call(b.Target.Com, "PutMatrix", Transform.Apply((double[])b.Original, track.Kind, track.Axis, value), true);
            else Set(b.Target.Com, b.Target.Property, value * (b.Target.Property == "Angle" ? Math.PI / 180 : 0.001));
            if(nestedKeyPaths.ContainsKey(track)&&!ConceptSame(Matrix(b.Target.Com),Transform.Apply((double[])b.Original,track.Kind,track.Axis,value)))throw new InvalidOperationException("Nested solver rejected motion; stop and inspect constraints");
        }
        Call(Get(Get(app!, "ActiveWindow"), "View"), "Update");
    }
    public void Restore()
    {
        Check(false);
        RestoreConceptBaseline();
        foreach (var b in bindings.Values)
        {
            b.Active = true;
            foreach (var ground in b.Grounds) Set(ground.Relation, "Suppress", true);
            if (b.Target.Property == "Matrix") Call(b.Target.Com, "PutMatrix", b.Original, true);
            else Set(b.Target.Com, b.Target.Property, b.Original);
        }
        Call(Get(Get(app!, "ActiveWindow"), "View"), "Update");
    }
    public string? Disconnect()
    {
        if (!Connected) return null;
        try
        {
            Check(false);
            var errors = new List<Exception>();
            try { RestoreConceptBaseline(); } catch(Exception ex) { errors.Add(ex); }
            foreach (var b in bindings.Values)
                try { RestoreBinding(b); } catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 0) throw new AggregateException("基準姿勢または固定拘束の復元に失敗しました。", errors);
            Call(Get(Get(app!, "ActiveWindow"), "View"), "Update");
            return null;
        }
        catch (Exception ex) { return "接続は解除しました。基準状態の復元は確認できませんでした：" + (ex.InnerException ?? ex).Message; }
        finally { ForgetConnection(); }
    }
}
