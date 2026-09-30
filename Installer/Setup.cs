using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MechCueInstaller
{
    static class Program
    {
#if ADDIN_ONLY
        public static readonly bool AddInOnly = true;
#else
        public static readonly bool AddInOnly = false;
#endif
        public static string ReleaseVersion
        {
            get { using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ReleaseVersion.txt")) using (var reader = new StreamReader(stream)) return reader.ReadToEnd().Trim(); }
        }
        [STAThread] static void Main(string[] args)
        {
            if (args.Contains("--self-test")) { Test(); return; }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(args.Contains("--uninstall")));
        }
        public static byte[] Resource()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.zip"))
            using (var memory = new MemoryStream()) { stream.CopyTo(memory); return memory.ToArray(); }
        }
        public static string SafePath(string root, string relative)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(prefix, relative));
            if (Path.IsPathRooted(relative) || !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("不正なパッケージのパスです。");
            return full;
        }
        static void Test()
        {
            string root = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "test-install");
            using (var memory = new MemoryStream(Resource()))
            using (var zip = new ZipArchive(memory))
            {
                foreach (var entry in zip.Entries) { SafePath(root, entry.FullName); using (var s = entry.Open()) { var buffer = new byte[4096]; while (s.Read(buffer, 0, buffer.Length) > 0) { } } }
                if (!Program.AddInOnly && !zip.Entries.Any(e => e.FullName == "MechCue.exe")) throw new IOException("Missing standalone executable");
                if (Program.AddInOnly && zip.Entries.Any(e => e.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) throw new IOException("Executable must not be included in add-in-only payload");
                foreach (var required in SetupForm.Required) if (!zip.Entries.Any(e => e.FullName == required)) throw new IOException("Missing " + required);
                foreach (var required in new[] { "LICENSE", "THIRD_PARTY_NOTICES.md", "Example-Sequence.json" }) if (!zip.Entries.Any(e => e.FullName == required)) throw new IOException("Missing " + required);
                if (zip.Entries.Any(e => e.FullName.Contains("CADTeam"))) throw new IOException("Vendor binary must not be packaged");
            }
            bool rejected = false; try { SafePath(root, "../outside.dll"); } catch (IOException) { rejected = true; }
            if (!rejected) throw new IOException("Unsafe path accepted");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "installer-test-result.txt"), "PASS: " + (Program.AddInOnly ? "add-in only, no standalone EXE; " : "add-in and standalone; ") + "payload integrity, required files, vendor dependency policy, traversal rejection");
        }
    }
    class SetupForm : Form
    {
        const string Clsid = "{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}";
        const string ClassKey = @"SOFTWARE\Classes\CLSID\" + Clsid;
        const string ProgKey = @"SOFTWARE\Classes\MechCue.TimeChartAddIn";
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MechCue.TimeChart";
        public static readonly string[] Required = { "MechCue.AddIn.comhost.dll", "MechCue.AddIn.dll", "MechCue.AddIn.deps.json", "MechCue.AddIn.runtimeconfig.json", "MechCue.dll", "MechCue.runtimeconfig.json", "MechCue.deps.json" };
        readonly TextBox destination = new TextBox { Width = 550 };
        readonly TextBox solidEdge = new TextBox { Width = 550 };
        readonly Label status = new Label { Width = 620, Height = 100 };
        readonly Button install = new Button { Text = "インストール / 更新", AutoSize = true };
        readonly bool uninstall;
        public SetupForm(bool uninstall)
        {
            this.uninstall = uninstall; Text = "MechCue セットアップ " + Program.ReleaseVersion; ClientSize = new Size(690, 480);
            Font = new Font("Yu Gothic UI", 10); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(new Label { Text = Program.AddInOnly ? "MechCue アドイン版" : "MechCue アドイン ＋ 独立版", Font = new Font(Font.FontFamily, 17, FontStyle.Bold), Width = 630, Height = 44 });
            panel.Controls.Add(new Label { Text = "Solid Edgeを終了してから実行してください。グラフファイルは保存しておいてください。", Width = 630, Height = 40 });
            panel.Controls.Add(new Label { Text = "インストール先", AutoSize = true }); panel.Controls.Add(destination);
            panel.Controls.Add(new Label { Text = "Solid Edgeのインストールフォルダー", AutoSize = true }); panel.Controls.Add(solidEdge);
            var browse = new Button { Text = "Solid Edgeフォルダーを選択", AutoSize = true };
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog()) if (dialog.ShowDialog() == DialogResult.OK) solidEdge.Text = dialog.SelectedPath; }; panel.Controls.Add(browse);
            panel.Controls.Add(new Label { Text = "対応：Windows 64bit / Solid Edge 2026 / .NET 8 Desktop Runtime", Width = 630, Height = 30 });
            panel.Controls.Add(install); panel.Controls.Add(status); Controls.Add(panel);
            destination.Text = InstalledPath() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MechCue");
            solidEdge.Text = FindSolidEdge();
            if (uninstall) { install.Text = "アンインストール"; destination.ReadOnly = true; solidEdge.Enabled = browse.Enabled = false; }
            install.Click += delegate
            {
                install.Enabled = false;
                try
                {
                    if (Process.GetProcessesByName("Edge").Length > 0) throw new IOException("Solid Edgeが起動しています。作業を保存して終了し、もう一度実行してください。");
                    if (uninstall) RemoveInstallation(); else Install();
                }
                catch (Exception ex) { status.Text = "失敗：" + ex.Message; MessageBox.Show(this, ex.Message, "MechCue セットアップ", MessageBoxButtons.OK, MessageBoxIcon.Information); install.Enabled = true; }
            };
        }
        static RegistryKey Machine() { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); }
        static RegistryKey User() { return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64); }
        static string InstalledPath() { using (var root = Machine()) using (var key = root.OpenSubKey(UninstallKey)) return key == null ? null : key.GetValue("InstallLocation") as string; }
        static string FindSolidEdge()
        {
            var candidates = new List<string>();
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64))
            using (var prog = root.OpenSubKey(@"SolidEdge.Application\CLSID"))
            {
                if (prog != null) using (var server = root.OpenSubKey(@"CLSID\" + prog.GetValue("") + @"\LocalServer32"))
                {
                    string command = server == null ? "" : Convert.ToString(server.GetValue(""));
                    int end = command.IndexOf("Edge.exe", StringComparison.OrdinalIgnoreCase);
                    if (end >= 0) { string exe = command.Substring(0, end + 8).Trim().Trim('"'); candidates.Add(Path.GetDirectoryName(Path.GetDirectoryName(exe))); }
                }
            }
            candidates.Add(@"C:\Siemens\Solid Edge 2026"); candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Siemens", "Solid Edge 2026"));
            return candidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "Program", "Edge.exe"))) ?? "";
        }
        static bool HasDesktopRuntime()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
            return Directory.Exists(directory) && Directory.GetDirectories(directory).Any(p => Path.GetFileName(p).StartsWith("8."));
        }
        void Install()
        {
            if (!Environment.Is64BitOperatingSystem) throw new IOException("64bitのWindowsが必要です。");
            if (!HasDesktopRuntime()) throw new IOException(".NET 8 Desktop Runtime（x64）が必要です。インストール後に再実行してください。");
            if (!File.Exists(Path.Combine(solidEdge.Text, "Program", "Edge.exe"))) throw new IOException("Solid Edge本体が見つかりません。インストールフォルダーを選んでください。");
            string target = Path.GetFullPath(destination.Text.Trim());
            if (target.TrimEnd('\\').Length <= 3 || target.IndexOf('"') >= 0) throw new IOException("有効なインストール先を指定してください。");
            string cadRoot = Path.GetFullPath(solidEdge.Text).TrimEnd('\\');
            if (string.Equals(target.TrimEnd('\\'), cadRoot, StringComparison.OrdinalIgnoreCase) || target.StartsWith(cadRoot + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Solid Edge本体のフォルダーとは別の場所を指定してください。");
            using (var machine = Machine()) using (var previous = machine.OpenSubKey(UninstallKey))
            {
                string mode = Program.AddInOnly ? "AddInOnly" : "Full";
                if (previous != null && Convert.ToString(previous.GetValue("InstallMode", "Full")) != mode)
                    throw new IOException("別の構成のMechCueが導入済みです。既存版をアンインストールしてから、今回のインストーラーを実行してください。");
            }
            Directory.CreateDirectory(target);
            var backups = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var memory = new MemoryStream(Program.Resource())) using (var zip = new ZipArchive(memory))
                foreach (var entry in zip.Entries) using (var stream = entry.Open()) using (var data = new MemoryStream()) { stream.CopyTo(data); files.Add(Program.SafePath(target, entry.FullName), data.ToArray()); }
            string setupPath = Program.SafePath(target, "MechCue-Setup.exe");
            if (!string.Equals(setupPath, Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase)) files.Add(setupPath, File.ReadAllBytes(Assembly.GetExecutingAssembly().Location));
            string manifestPath = Program.SafePath(target, "installed-files.txt");
            string manifestText = string.Join(Environment.NewLine, files.Keys.Select(p => Path.GetFileName(p)).Concat(new[] { "MechCue-Setup.exe" }).Distinct());
            files.Add(manifestPath, Encoding.UTF8.GetBytes(manifestText));
            foreach (var file in files.Keys) backups[file] = File.Exists(file) ? File.ReadAllBytes(file) : null;
            using (var machine = Machine()) using (var user = User())
            {
                var snapshots = new[] { new Snapshot(machine, ClassKey), new Snapshot(machine, ProgKey), new Snapshot(machine, UninstallKey), new Snapshot(user, ClassKey), new Snapshot(user, ProgKey) };
                try
                {
                    foreach (var file in files) { Directory.CreateDirectory(Path.GetDirectoryName(file.Key)); File.WriteAllBytes(file.Key, file.Value); }
                    Register(machine, target);
                    using (var existing = user.OpenSubKey(ClassKey)) if (existing != null) Register(user, target);
                    using (var key = machine.CreateSubKey(UninstallKey))
                    {
                        key.SetValue("DisplayName", Program.AddInOnly ? "MechCue タイムチャート（アドイン版）" : "MechCue タイムチャート"); key.SetValue("InstallMode", Program.AddInOnly ? "AddInOnly" : "Full"); key.SetValue("DisplayVersion", Program.ReleaseVersion); key.SetValue("Publisher", "MechCue contributors");
                        key.SetValue("InstallLocation", target); key.SetValue("UninstallString", "\"" + setupPath + "\" --uninstall"); key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
                    }
                }
                catch
                {
                    foreach (var snapshot in snapshots) snapshot.Restore();
                    foreach (var file in backups) { if (file.Value == null) { if (File.Exists(file.Key)) File.Delete(file.Key); } else File.WriteAllBytes(file.Key, file.Value); }
                    throw;
                }
            }
            status.Text = "インストールが完了しました。Solid Edgeを起動し、MechCueのタイムチャートを開いてください。" + (Program.AddInOnly ? "" : "\n独立版：" + Path.Combine(target, "MechCue.exe"));
            install.Text = "完了";
        }
        static void Register(RegistryKey root, string target)
        {
            using (var key = root.CreateSubKey(ClassKey))
            {
                key.SetValue("", "MechCue Time Chart"); key.SetValue("AutoConnect", 1); key.SetValue("409", "MechCue Time Chart"); key.SetValue("411", "MechCue Time Chart");
                using (var server = key.CreateSubKey("InprocServer32")) { server.SetValue("", Program.SafePath(target, "MechCue.AddIn.comhost.dll")); server.SetValue("ThreadingModel", "Both"); }
                using (var prog = key.CreateSubKey("ProgID")) prog.SetValue("", "MechCue.TimeChartAddIn");
                using (var category = key.CreateSubKey(@"Implemented Categories\{26B1D2D1-2B03-11D2-B589-080036E8B802}")) { }
                using (var environment = key.CreateSubKey(@"Environment Categories\{26618395-09D6-11D1-BA07-080036230602}")) { }
                using (var summary = key.CreateSubKey("Summary")) { summary.SetValue("409", "Time-displacement assembly control"); summary.SetValue("411", "Time-displacement assembly control"); }
            }
            using (var prog = root.CreateSubKey(ProgKey + @"\CLSID")) prog.SetValue("", Clsid);
        }
        void RemoveInstallation()
        {
            string target = InstalledPath();
            if (string.IsNullOrEmpty(target)) throw new IOException("インストーラーによる登録がありません。");
            if (MessageBox.Show(this, "MechCueをアンインストールします。保存したグラフは残します。", "MechCue", MessageBoxButtons.OKCancel) != DialogResult.OK) { install.Enabled = true; return; }
            string manifest = Program.SafePath(target, "installed-files.txt");
            if (!File.Exists(manifest)) throw new IOException("インストール記録が見つかりません。登録解除を中止しました。");
            var files = File.ReadAllLines(manifest).Select(p => Program.SafePath(target, p)).ToList();
            using (var machine = Machine()) using (var user = User())
            {
                foreach (var root in new[] { machine, user })
                {
                    using (var key = root.OpenSubKey(ClassKey + @"\InprocServer32"))
                        if (key != null && string.Equals(Convert.ToString(key.GetValue("")), Program.SafePath(target, "MechCue.AddIn.comhost.dll"), StringComparison.OrdinalIgnoreCase))
                        { root.DeleteSubKeyTree(ClassKey, false); root.DeleteSubKeyTree(ProgKey, false); }
                }
                machine.DeleteSubKeyTree(UninstallKey, false);
            }
            foreach (string file in files)
                if (File.Exists(file) && !string.Equals(file, Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            File.Delete(manifest);
            status.Text = "アンインストールが完了しました。保存したグラフと、実行中のセットアップEXEは残しています。"; install.Text = "完了";
        }
        sealed class Snapshot
        {
            readonly RegistryKey root; readonly string path; readonly bool existed;
            readonly Dictionary<string, Dictionary<string, Tuple<object, RegistryValueKind>>> data = new Dictionary<string, Dictionary<string, Tuple<object, RegistryValueKind>>>();
            public Snapshot(RegistryKey root, string path) { this.root = root; this.path = path; using (var key = root.OpenSubKey(path)) { existed = key != null; if (existed) Read(key, ""); } }
            void Read(RegistryKey key, string relative)
            {
                data[relative] = key.GetValueNames().ToDictionary(n => n, n => Tuple.Create(key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames), key.GetValueKind(n)));
                foreach (var name in key.GetSubKeyNames()) using (var child = key.OpenSubKey(name)) Read(child, relative.Length == 0 ? name : relative + "\\" + name);
            }
            public void Restore()
            {
                root.DeleteSubKeyTree(path, false);
                if (!existed) return;
                foreach (var item in data) using (var key = root.CreateSubKey(path + (item.Key.Length == 0 ? "" : "\\" + item.Key)))
                    foreach (var value in item.Value) key.SetValue(value.Key, value.Value.Item1, value.Value.Item2);
            }
        }
    }
}
