// Lab Reset - main window. C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LabReset
{
    public class MainForm : Form
    {
        readonly string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        readonly string settingsFile;
        readonly AppSettings settings;
        string zipFolder;

        ListView lvZips, lvPcs, lvResults;
        Label lblZipFolder, lblStatus, lblCount;
        TextBox txtPcAdd, txtLog;
        NumericUpDown numMatch;
        CheckBox chkOwnFolder, chkClean;
        Button btnStop, btnDelete;
        ProgressBar progress;
        SplitContainer splitMain, splitLR, splitLeft;
        readonly List<Control> busyDisabled = new List<Control>();
        readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer { Interval = 250 };
        readonly ResultSorter sorter = new ResultSorter();

        CancellationTokenSource cts;
        volatile string statusText = "Ready.";
        int progressDone;
        bool busy;

        public MainForm()
        {
            settingsFile = Path.Combine(appDir, "LabReset.settings.ini");
            Logger.Dir = Path.Combine(appDir, "Logs");
            settings = AppSettings.Load(settingsFile);
            zipFolder = Path.Combine(appDir, "Library");          // archives are stored inside the app
            SevenZip.ExePath = Path.Combine(appDir, @"7zip\7z.exe"); // bundled 7-Zip for .7z/.rar/.tar.gz
            Net.User = settings.NetUser;

            BuildUi();
            LoadSettingsIntoUi();
            Logger.Line += OnLogLine;
            uiTimer.Tick += (s, e) => UpdateStatus();
        }

        // ================================================================== UI construction
        static FlowLayoutPanel Row(params Control[] controls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 2, 0, 2) };
            f.Controls.AddRange(controls);
            return f;
        }

        static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 0) };
        }

        Button Btn(string text, EventHandler click, bool disableWhenBusy = true)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(4, 1, 4, 1) };
            b.Click += click;
            if (disableWhenBusy) busyDisabled.Add(b);
            return b;
        }

        static ListView MakeList(string[] names, int[] widths)
        {
            var lv = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, GridLines = true };
            for (int i = 0; i < names.Length; i++) lv.Columns.Add(names[i], widths[i]);
            return lv;
        }

        /// <summary>Docks controls: tops[0] is the top-most row, bottoms[0] is the bottom-most row, fill takes the rest.</summary>
        static void Stack(Control parent, Control fill, Control[] tops, Control[] bottoms)
        {
            if (fill != null) { fill.Dock = DockStyle.Fill; parent.Controls.Add(fill); }
            if (tops != null) for (int i = tops.Length - 1; i >= 0; i--) { tops[i].Dock = DockStyle.Top; parent.Controls.Add(tops[i]); }
            if (bottoms != null) for (int i = bottoms.Length - 1; i >= 0; i--) { bottoms[i].Dock = DockStyle.Bottom; parent.Controls.Add(bottoms[i]); }
        }

        static GroupBox Group(string title, Control fill, Control[] tops, Control[] bottoms)
        {
            var g = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6) };
            Stack(g, fill, tops, bottoms);
            return g;
        }

        void BuildUi()
        {
            Text = "Lab Reset  -  " + Environment.MachineName;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(1280, 860);
            MinimumSize = new Size(1000, 680);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var bold = new Font(Font, FontStyle.Bold);

            // ---- 1. Archive library
            lblZipFolder = new Label { Height = 20, AutoEllipsis = true, ForeColor = SystemColors.GrayText };
            lvZips = MakeList(new[] { "Archive", "Source", "Files", "Size" }, new[] { 195, 100, 45, 65 });
            lvZips.AllowDrop = true;
            lvZips.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            lvZips.DragDrop += (s, e) => { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files != null && !busy) ImportFiles(files); };
            var btnImport = Btn("Import archive...", ImportZips_Click);
            btnImport.Font = bold;
            var zipButtons = Row(btnImport, Btn("Remove imported", RemoveZips_Click),
                                 Btn("Open library folder", (s, e) => OpenFolder(zipFolder), false),
                                 Btn("Refresh", RefreshButton_Click));
            var gZip = Group("1. Lab archives   (tick the ones to use)", lvZips, new Control[] { lblZipFolder }, new Control[] { zipButtons });

            // ---- 2. Lab computers
            txtPcAdd = new TextBox { Width = 200 };
            txtPcAdd.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddPcs_Click(s, e); } };
            var pcAddRow = Row(Lbl("IP address:"), txtPcAdd, Btn("Add", AddPcs_Click));
            var pcHint = new Label { Text = "e.g. 10.3.3.162 (= PC 62), several: 10.3.3.162, 10.3.3.163   range: 10.3.3.{101-130}", Height = 18, AutoEllipsis = true, ForeColor = SystemColors.GrayText };
            lvPcs = MakeList(new[] { "PC", "IP address", "VMs folder", "Status" }, new[] { 70, 95, 105, 300 });
            lvPcs.Sorting = SortOrder.Ascending;
            var pcButtons = Row(Btn("Remove", RemovePcs_Click), Btn("Tick all", (s, e) => SetAllChecked(lvPcs, true), false),
                                Btn("Tick none", (s, e) => SetAllChecked(lvPcs, false), false), Btn("Test connection", TestPcs_Click),
                                Btn("Network login...", NetLogin_Click));
            var gPc = Group("2. Lab computers   (tick the PCs to work on)", lvPcs, new Control[] { pcAddRow, pcHint }, new Control[] { pcButtons });

            // ---- 3. Extract
            chkOwnFolder = new CheckBox { Text = "Put each archive in its own folder (if it doesn't already have one)", AutoSize = true, Margin = new Padding(3, 5, 16, 3) };
            chkClean = new CheckBox { Text = "Remove the old copy first (clean reset)", AutoSize = true, Margin = new Padding(3, 5, 3, 3) };
            var btnExtract = Btn("Extract ticked archives on ticked PCs", Extract_Click);
            btnExtract.Font = bold;
            var gExtract = new GroupBox { Text = "3. Extract", Height = 128, Padding = new Padding(8, 4, 8, 4) };
            Stack(gExtract, null, new Control[] {
                Row(Lbl("Extracts into the " + VmFolder.Name + " folder on each ticked PC - on whichever drive it is (C:, D:, ...).")),
                Row(chkOwnFolder, chkClean),
                Row(btnExtract) }, null);

            // ---- 4. Scan & delete
            numMatch = new NumericUpDown { Minimum = 10, Maximum = 100, Width = 55, Margin = new Padding(3, 4, 3, 3) };
            var btnScan = Btn("Scan ticked PCs", Scan_Click);
            btnScan.Font = bold;
            btnStop = Btn("Stop", (s, e) => { if (cts != null) { cts.Cancel(); statusText = "Stopping..."; } }, false);
            btnStop.Enabled = false;
            var scanRow1 = Row(Lbl("Searches every drive of each ticked PC for its " + VmFolder.Name + " folder.   Minimum match %:"),
                               numMatch, btnScan, btnStop);
            lvResults = MakeList(new[] { "PC", "Path", "Type", "From archive", "Match", "Date modified" },
                                 new[] { 80, 300, 50, 120, 120, 110 });
            lvResults.ColumnClick += Results_ColumnClick;
            lvResults.ItemChecked += (s, e) => UpdateCount();
            lvResults.DoubleClick += (s, e) => { if (lvResults.SelectedItems.Count > 0) ShowInExplorer(((ScanResult)lvResults.SelectedItems[0].Tag).AccessPath); };
            lblCount = Lbl("");
            btnDelete = Btn("Delete ticked items...", Delete_Click);
            btnDelete.ForeColor = Color.DarkRed;
            btnDelete.Font = bold;
            var resRow = Row(Btn("Tick all", (s, e) => SetAllChecked(lvResults, true), false),
                             Btn("Tick none", (s, e) => SetAllChecked(lvResults, false), false), btnDelete, lblCount);
            var resHint = new Label { Text = "Only things inside " + VmFolder.Name + " are ever deleted (never the archives). Double-click a row to open it.", Height = 18, ForeColor = SystemColors.GrayText };
            var gScan = Group("4. Find and delete extracted VMs", lvResults, new Control[] { scanRow1 }, new Control[] { resRow, resHint });

            // ---- status
            progress = new ProgressBar { Height = 16 };
            lblStatus = new Label { Height = 22, AutoEllipsis = true, Text = "Ready.", Padding = new Padding(0, 4, 0, 0) };
            var statusPanel = new Panel { Height = 44, Padding = new Padding(3, 4, 3, 0) };
            Stack(statusPanel, null, new Control[] { progress, lblStatus }, null);

            // ---- log
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = SystemColors.Window, Font = new Font("Consolas", 9f) };
            var logButtons = Row(Btn("Open log folder", (s, e) => OpenFolder(Logger.Dir), false), Btn("Clear", (s, e) => txtLog.Clear(), false));
            var gLog = Group("Log", txtLog, null, new Control[] { logButtons });

            // ---- layout
            splitLeft = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            splitLeft.Panel1.Controls.Add(gZip);
            splitLeft.Panel2.Controls.Add(gPc);
            splitLR = new SplitContainer { Dock = DockStyle.Fill };
            splitLR.Panel1.Controls.Add(splitLeft);
            Stack(splitLR.Panel2, gScan, new Control[] { gExtract }, new Control[] { statusPanel });
            splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Padding = new Padding(4) };
            splitMain.Panel1.Controls.Add(splitLR);
            splitMain.Panel2.Controls.Add(gLog);
            Controls.Add(splitMain);
        }

        public void ApplySplitters()
        {
            try
            {
                splitMain.SplitterDistance = Math.Max(300, splitMain.Height - 170);
                splitLR.SplitterDistance = 470;
                splitLeft.SplitterDistance = Math.Max(150, splitLeft.Height / 2);
            }
            catch { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplySplitters();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Logger.Write("Lab Reset started on " + Environment.MachineName + ". Library: " + zipFolder);
            if (!IsAdmin()) Logger.Write("WARNING: not running as administrator - deleting/extracting may fail. Right-click LabReset.exe > Run as administrator.");
            if (SevenZip.Find() == null) Logger.Write("WARNING: 7-Zip was not found (7zip\\7z.exe) - .7z, .rar and .tar.gz archives can't be used.");
            if (!string.IsNullOrEmpty(Net.User)) Logger.Write("Network login user is '" + Net.User + "'. Enter its password with 'Network login...' before working on lab PCs.");
            StartupRefresh();
        }

        static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        // ================================================================== settings
        void LoadSettingsIntoUi()
        {
            numMatch.Value = Math.Max(numMatch.Minimum, Math.Min(numMatch.Maximum, settings.MinMatch));
            chkOwnFolder.Checked = settings.OwnFolder;
            chkClean.Checked = settings.CleanFirst;
            lblZipFolder.Text = "Library: " + zipFolder;

            bool thisPcChecked = true;
            foreach (var p in settings.Pcs) if (p.Key == PcNames.ThisPc) thisPcChecked = p.Value;
            AddPcRow(PcNames.ThisPc, thisPcChecked);
            foreach (var p in settings.Pcs)
                if (p.Key != PcNames.ThisPc) AddPcRow(p.Key, p.Value);
        }

        void SaveSettings()
        {
            try
            {
                settings.MinMatch = (int)numMatch.Value;
                settings.OwnFolder = chkOwnFolder.Checked;
                settings.CleanFirst = chkClean.Checked;
                settings.NetUser = Net.User ?? "";
                settings.CheckedZips = lvZips.CheckedItems.Cast<ListViewItem>().Select(i => i.Text).ToList();
                settings.Pcs = lvPcs.Items.Cast<ListViewItem>().Select(i => new KeyValuePair<string, bool>((string)i.Tag, i.Checked)).ToList();
                settings.Save(settingsFile);
            }
            catch (Exception ex) { Logger.Write("Could not save settings: " + ex.Message); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                if (MessageBox.Show(this, "Something is still running. Stop it and close Lab Reset?", "Lab Reset",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                if (cts != null) cts.Cancel();
            }
            SaveSettings();
            Net.DisconnectAll();
            base.OnFormClosing(e);
        }

        // ================================================================== helpers
        void Msg(string text, MessageBoxIcon icon = MessageBoxIcon.Information)
        {
            MessageBox.Show(this, text, "Lab Reset", MessageBoxButtons.OK, icon);
        }

        void UI(Action a)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                if (InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch (InvalidOperationException) { }
        }

        void OnLogLine(string line)
        {
            UI(() =>
            {
                if (txtLog.TextLength > 2000000) txtLog.Clear();
                txtLog.AppendText(line + Environment.NewLine);
            });
        }

        static void SetAllChecked(ListView lv, bool value)
        {
            lv.BeginUpdate();
            foreach (ListViewItem i in lv.Items) i.Checked = value;
            lv.EndUpdate();
        }

        static void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch { }
        }

        static void ShowInExplorer(string path)
        {
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        static ParallelOptions Par(CancellationToken ct, int max)
        {
            return new ParallelOptions { MaxDegreeOfParallelism = max, CancellationToken = ct };
        }

        void BeginBusy(string text, int total)
        {
            busy = true;
            cts = new CancellationTokenSource();
            progressDone = 0;
            progress.Value = 0;
            progress.Maximum = Math.Max(1, total);
            statusText = text;
            foreach (Control c in busyDisabled) c.Enabled = false;
            btnStop.Enabled = true;
            UseWaitCursor = false;
            uiTimer.Start();
            UpdateStatus();
        }

        void EndBusy(string text)
        {
            busy = false;
            uiTimer.Stop();
            foreach (Control c in busyDisabled) c.Enabled = true;
            btnStop.Enabled = false;
            statusText = text;
            progressDone = 0;
            progress.Value = 0;
            UpdateStatus();
        }

        void StepDone() { Interlocked.Increment(ref progressDone); }

        void UpdateStatus()
        {
            lblStatus.Text = statusText;
            progress.Value = Math.Max(0, Math.Min(progress.Maximum, progressDone));
        }

        List<ZipSignature> CheckedZips()
        {
            return lvZips.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag as ZipSignature).Where(z => z != null).ToList();
        }

        List<ZipSignature> AllZips()
        {
            return lvZips.Items.Cast<ListViewItem>().Select(i => i.Tag as ZipSignature).Where(z => z != null).ToList();
        }

        List<string> archivePaths = new List<string>();

        /// <summary>This PC's own __RivanVMs folder(s), whose archives are listed in the library.</summary>
        static List<string> ExistingSourceFolders()
        {
            return VmFolder.Find(PcNames.ThisPc, CancellationToken.None);
        }

        // The app folder, its library and every archive file can never be deleted.
        List<string> ProtectTree()
        {
            var l = new List<string> { zipFolder, appDir };
            l.AddRange(archivePaths);
            return l;
        }

        // ================================================================== 1. library
        string CacheDir { get { return Path.Combine(zipFolder, ".cache"); } }
        string BuiltInListFile { get { return Path.Combine(zipFolder, "builtin.txt"); } }

        HashSet<string> ReadBuiltIn()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(BuiltInListFile))
                    foreach (string l in File.ReadAllLines(BuiltInListFile))
                        if (l.Trim().Length > 0 && !l.TrimStart().StartsWith("#")) set.Add(l.Trim());
            }
            catch { }
            return set;
        }

        async void StartupRefresh()
        {
            BeginBusy("Reading the archive library...", 1);
            progress.Style = ProgressBarStyle.Marquee;
            try { await RefreshZips(settings.CheckedZips); }
            finally
            {
                progress.Style = ProgressBarStyle.Blocks;
                EndBusy("Ready.");
            }
        }

        async void RefreshButton_Click(object sender, EventArgs e)
        {
            BeginBusy("Reading the archive library...", 1);
            progress.Style = ProgressBarStyle.Marquee;
            try { await RefreshZips(null); }
            finally
            {
                progress.Style = ProgressBarStyle.Blocks;
                EndBusy("Ready.");
            }
        }

        async Task RefreshZips(IEnumerable<string> tickNames)
        {
            var ticked = new HashSet<string>(tickNames ?? lvZips.CheckedItems.Cast<ListViewItem>().Select(i => i.Text), StringComparer.OrdinalIgnoreCase);
            List<string> sources = ExistingSourceFolders();
            lblZipFolder.Text = "Library: " + zipFolder + (sources.Count > 0 ? "   +   " + string.Join(", ", sources) : "");
            string lib = zipFolder, cache = CacheDir;
            HashSet<string> builtin = ReadBuiltIn();
            // name, archive, error, source ("Built-in" / "Imported" / "C:\__RivanVMs")
            List<Tuple<string, ZipSignature, string, string>> loaded = await Task.Run(() =>
            {
                var list = new List<Tuple<string, ZipSignature, string, string>>();
                try
                {
                    Directory.CreateDirectory(lib);
                    var di = Directory.CreateDirectory(cache);
                    di.Attributes |= FileAttributes.Hidden;
                }
                catch (Exception ex) { Logger.Write("Cannot open the library folder " + lib + ": " + ex.Message); }

                var files = new List<Tuple<string, string>>(); // path, source
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (string f in Directory.GetFiles(lib).Where(x => ZipSignature.IsArchive(Path.GetFileName(x))))
                        if (seen.Add(Path.GetFileName(f))) files.Add(Tuple.Create(f, builtin.Contains(Path.GetFileName(f)) ? "Built-in" : "Imported"));
                }
                catch { }
                foreach (string src in sources)
                {
                    try
                    {
                        foreach (string f in Directory.GetFiles(src).Where(x => ZipSignature.IsArchive(Path.GetFileName(x))))
                            if (seen.Add(Path.GetFileName(f))) files.Add(Tuple.Create(f, src));
                    }
                    catch (Exception ex) { Logger.Write("Cannot read " + src + ": " + ex.Message); }
                }

                foreach (var pf in files.OrderBy(x => Path.GetFileName(x.Item1), StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileName(pf.Item1);
                    statusText = "Reading " + name + " (only slow the first time)...";
                    try
                    {
                        ZipSignature z = ZipSignature.Load(pf.Item1, cache);
                        z.BuiltIn = pf.Item2 != "Imported";
                        list.Add(Tuple.Create(name, z, (string)null, pf.Item2));
                    }
                    catch (Exception ex)
                    {
                        Logger.Write("Cannot read " + name + ": " + ex.Message);
                        list.Add(Tuple.Create(name, (ZipSignature)null, ex.Message, pf.Item2));
                    }
                }
                return list;
            });

            archivePaths = loaded.Where(t => t.Item2 != null).Select(t => t.Item2.ZipPath).ToList();
            lvZips.BeginUpdate();
            lvZips.Items.Clear();
            foreach (var t in loaded)
            {
                var item = new ListViewItem(t.Item1);
                ZipSignature z = t.Item2;
                item.SubItems.Add(t.Item4);
                if (z != null)
                {
                    item.Tag = z;
                    item.SubItems.Add(z.Files.Count.ToString());
                    item.SubItems.Add(FormatSize(z.Size));
                }
                else
                {
                    item.SubItems.Add("error");
                    item.SubItems.Add("");
                    item.ForeColor = Color.Firebrick;
                    item.ToolTipText = t.Item3;
                }
                item.Checked = ticked.Contains(item.Text) && item.Tag != null;
                lvZips.Items.Add(item);
            }
            lvZips.EndUpdate();
        }

        static string FormatSize(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
            return Math.Max(1, b / 1024) + " KB";
        }

        void ImportZips_Click(object sender, EventArgs e)
        {
            using (var d = new OpenFileDialog
            {
                Filter = "Archives (*.zip, *.7z, *.rar, *.tar, *.tar.gz, *.tgz)|*.zip;*.7z;*.rar;*.tar;*.gz;*.tgz|All files (*.*)|*.*",
                Multiselect = true,
                Title = "Choose archives to import into Lab Reset"
            })
                if (d.ShowDialog(this) == DialogResult.OK) ImportFiles(d.FileNames);
        }

        /// <summary>Copies archives into the app's Library folder so they stay in the app permanently.</summary>
        async void ImportFiles(IEnumerable<string> files)
        {
            var ticked = new HashSet<string>(lvZips.CheckedItems.Cast<ListViewItem>().Select(i => i.Text), StringComparer.OrdinalIgnoreCase);
            HashSet<string> builtin = ReadBuiltIn();
            var toCopy = new List<string>();
            foreach (string src in files)
            {
                string name = Path.GetFileName(src);
                if (!ZipSignature.IsArchive(name)) { Msg(name + " is not a supported archive (.zip, .7z, .rar, .tar, .tar.gz).", MessageBoxIcon.Warning); continue; }
                string dest = Path.Combine(zipFolder, name);
                if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) { ticked.Add(name); continue; }
                if (builtin.Contains(name)) { Msg(name + " is already built into Lab Reset.", MessageBoxIcon.Information); continue; }
                if (File.Exists(dest))
                {
                    DialogResult r = MessageBox.Show(this, name + " is already in the library. Replace it with the new one?", "Lab Reset",
                                                     MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) return;
                    if (r == DialogResult.No) continue;
                }
                toCopy.Add(src);
            }
            if (toCopy.Count == 0) { await RefreshZips(ticked); return; }

            long total = Math.Max(1, toCopy.Sum(f => new FileInfo(f).Length));
            BeginBusy("Importing...", 100);
            CancellationToken ct = cts.Token;
            string lib = zipFolder, cache = CacheDir;
            int ok = 0;
            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(lib);
                    long done = 0;
                    var buf = new byte[4 << 20];
                    foreach (string src in toCopy)
                    {
                        string name = Path.GetFileName(src);
                        string dest = Path.Combine(lib, name);
                        string tmp = dest + ".importing";
                        try
                        {
                            using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                            using (var o = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                            {
                                int r;
                                while ((r = i.Read(buf, 0, buf.Length)) > 0)
                                {
                                    ct.ThrowIfCancellationRequested();
                                    o.Write(buf, 0, r);
                                    done += r;
                                    progressDone = (int)(done * 100 / total);
                                    statusText = "Importing " + name + "...  " + FormatSize(done) + " of " + FormatSize(total);
                                }
                            }
                            statusText = "Checking " + name + "...";
                            ZipSignature.Load(tmp, null); // make sure it is a readable archive
                            if (File.Exists(dest)) File.Delete(dest);
                            File.Move(tmp, dest);
                            File.SetLastWriteTime(dest, File.GetLastWriteTime(src));
                            ticked.Add(name);
                            ok++;
                            Logger.Write("Imported " + name + " into the library (" + FormatSize(new FileInfo(dest).Length) + ").");
                        }
                        catch (OperationCanceledException) { TryDelete(tmp); throw; }
                        catch (Exception ex)
                        {
                            TryDelete(tmp);
                            Logger.Write("FAILED to import " + name + ": " + ex.Message);
                            UI(() => Msg(name + " could not be imported:\n" + ex.Message, MessageBoxIcon.Warning));
                        }
                    }
                }, ct);
            }
            catch (OperationCanceledException) { Logger.Write("Import stopped by user."); }
            finally
            {
                progress.Style = ProgressBarStyle.Marquee;
                statusText = "Reading the archive library...";
                UpdateStatus();
            }
            try { await RefreshZips(ticked); }
            finally
            {
                progress.Style = ProgressBarStyle.Blocks;
                EndBusy("Import finished: " + ok + " of " + toCopy.Count + " archive(s) imported.");
                SaveSettings();
            }
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

        async void RemoveZips_Click(object sender, EventArgs e)
        {
            var items = lvZips.SelectedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0) { Msg("Click an imported archive to select it, then click Remove."); return; }
            if (items.Any(i => i.SubItems[1].Text != "Imported"))
            {
                Msg("Built-in archives (and the ones in __RivanVMs) can't be removed from Lab Reset.\nOnly archives you imported yourself can be removed.");
                items = items.Where(i => i.SubItems[1].Text == "Imported").ToList();
                if (items.Count == 0) return;
            }
            if (MessageBox.Show(this, "Remove " + items.Count + " imported archive(s) from Lab Reset?\n\n" + string.Join("\n", items.Select(i => "  " + i.Text)) +
                                "\n\nThey are deleted from the library. You can import them again later.", "Lab Reset",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            foreach (ListViewItem i in items)
            {
                try
                {
                    File.Delete(Path.Combine(zipFolder, i.Text));
                    TryDelete(Path.Combine(CacheDir, i.Text + ".list"));
                    Logger.Write("Removed imported archive " + i.Text + " from the library.");
                }
                catch (Exception ex) { Logger.Write("Could not remove " + i.Text + ": " + ex.Message); }
            }
            await RefreshZips(null);
            SaveSettings();
        }

        // ================================================================== 2. lab computers
        ListViewItem AddPcRow(string pc, bool isChecked)
        {
            var it = new ListViewItem(PcNames.Number(pc)) { Checked = isChecked, Tag = pc };
            it.SubItems.Add(pc == PcNames.ThisPc ? Environment.MachineName : pc);
            it.SubItems.Add("");
            it.SubItems.Add("");
            lvPcs.Items.Add(it);
            return it;
        }

        List<string> CheckedPcs()
        {
            return lvPcs.CheckedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToList();
        }

        void SetPcInfo(string pc, string folders, string status)
        {
            UI(() =>
            {
                foreach (ListViewItem i in lvPcs.Items)
                {
                    if (!string.Equals((string)i.Tag, pc, StringComparison.OrdinalIgnoreCase)) continue;
                    if (folders != null) i.SubItems[2].Text = folders;
                    if (status != null)
                    {
                        i.SubItems[3].Text = status;
                        i.ForeColor = status.StartsWith("OK") ? Color.DarkGreen : status.StartsWith("Working") ? SystemColors.WindowText : Color.Firebrick;
                    }
                }
            });
        }

        void AddPcs_Click(object sender, EventArgs e)
        {
            var errors = new List<string>();
            List<string> names = PcNames.Expand(txtPcAdd.Text, errors);
            if (errors.Count > 0) Msg(string.Join("\n", errors.Take(12)), MessageBoxIcon.Warning);
            int added = 0;
            foreach (string n in names)
            {
                if (lvPcs.Items.Cast<ListViewItem>().Any(i => string.Equals((string)i.Tag, n, StringComparison.OrdinalIgnoreCase))) continue;
                if (PcNames.IsLocal(n)) { Logger.Write(n + " is this PC - it is already in the list as 'This PC'."); continue; }
                AddPcRow(n, true);
                added++;
            }
            if (added > 0)
            {
                Logger.Write("Added " + added + " computer(s).");
                txtPcAdd.Clear();
                SaveSettings();
            }
            else if (errors.Count == 0 && names.Count == 0) Msg("Type an IP address first, e.g. 10.3.3.162");
        }

        void RemovePcs_Click(object sender, EventArgs e)
        {
            var items = lvPcs.SelectedItems.Cast<ListViewItem>().Where(i => (string)i.Tag != PcNames.ThisPc).ToList();
            if (items.Count == 0) { Msg("Click a PC to select it (hold Ctrl or Shift for several), then click Remove.\n'This PC' can't be removed - just untick it."); return; }
            foreach (ListViewItem i in items) lvPcs.Items.Remove(i);
            SaveSettings();
        }

        async void TestPcs_Click(object sender, EventArgs e)
        {
            List<string> pcs = CheckedPcs();
            if (pcs.Count == 0) { Msg("Tick the PCs to test first."); return; }
            BeginBusy("Testing connections...", pcs.Count);
            CancellationToken ct = cts.Token;
            Logger.Write("=== TEST CONNECTION on " + pcs.Count + " PC(s)");
            int ok = 0;
            try
            {
                foreach (string pc in pcs) SetPcInfo(pc, "", "Working...");
                await Task.Run(() => Parallel.ForEach(pcs, Par(ct, 16), pc =>
                {
                    string r, folders = "";
                    try
                    {
                        r = Net.Test(pc);
                        if (r.StartsWith("OK"))
                        {
                            List<string> f = VmFolder.Find(pc, ct);
                            folders = f.Count > 0 ? string.Join(", ", f) : "not found";
                            if (f.Count == 0) r = "No " + VmFolder.Name + " folder on any drive";
                            else Interlocked.Increment(ref ok);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { r = "Error - " + ex.Message; }
                    SetPcInfo(pc, folders, r);
                    Logger.Write("  " + PcNames.Display(pc) + ": " + r + (folders.Length > 0 ? "   [" + folders + "]" : ""));
                    StepDone();
                }));
            }
            catch (OperationCanceledException) { Logger.Write("Test stopped."); }
            finally { EndBusy("Connection test finished: " + ok + " of " + pcs.Count + " PC(s) ready."); }
            Logger.Write("=== TEST finished: " + ok + " of " + pcs.Count + " PC(s) ready.");
        }

        void NetLogin_Click(object sender, EventArgs e)
        {
            using (var f = new Form
            {
                Text = "Network login for lab computers", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(470, 250), MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font
            })
            {
                var info = new Label
                {
                    Location = new Point(12, 10), Size = new Size(446, 92),
                    Text = "Only needed if 'Test connection' says 'Access is denied'.\n\n" +
                           "Enter an administrator account that exists on every lab PC, e.g.  .\\LabAdmin  (a local account with the same password on each PC). " +
                           "The password is kept in memory only and is never saved."
                };
                var lu = new Label { Text = "User name:", Location = new Point(12, 112), AutoSize = true };
                var tu = new TextBox { Location = new Point(110, 109), Width = 340, Text = Net.User ?? "" };
                var lp = new Label { Text = "Password:", Location = new Point(12, 146), AutoSize = true };
                var tp = new TextBox { Location = new Point(110, 143), Width = 340, UseSystemPasswordChar = true, Text = Net.Password ?? "" };
                var ok = new Button { Text = "Use this login", Location = new Point(110, 196), AutoSize = true, DialogResult = DialogResult.OK };
                var clear = new Button { Text = "Don't use a login", Location = new Point(230, 196), AutoSize = true, DialogResult = DialogResult.Abort };
                var cancel = new Button { Text = "Cancel", Location = new Point(370, 196), AutoSize = true, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { info, lu, tu, lp, tp, ok, clear, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                DialogResult r = f.ShowDialog(this);
                if (r == DialogResult.Cancel) return;
                Net.DisconnectAll();
                if (r == DialogResult.OK && tu.Text.Trim().Length > 0)
                {
                    Net.User = tu.Text.Trim();
                    Net.Password = tp.Text;
                    Logger.Write("Network login set to user '" + Net.User + "'.");
                }
                else
                {
                    Net.User = null;
                    Net.Password = null;
                    Logger.Write("Network login cleared - your own Windows account will be used.");
                }
                SaveSettings();
            }
        }

        /// <summary>Checks access and finds the PC's __RivanVMs folder(s). Returns null (and logs why) if the PC can't be used.</summary>
        List<string> PreparePc(string pc, CancellationToken ct)
        {
            string name = PcNames.Display(pc);
            string err = Net.CheckAccess(pc);
            if (err != null)
            {
                Logger.Write("UNREACHABLE " + name + ": " + err);
                SetPcInfo(pc, null, "Unreachable - " + err);
                return null;
            }
            List<string> folders = VmFolder.Find(pc, ct);
            SetPcInfo(pc, folders.Count > 0 ? string.Join(", ", folders) : "not found", null);
            if (folders.Count == 0)
            {
                Logger.Write("SKIPPED " + name + ": no " + VmFolder.Name + " folder on any drive.");
                SetPcInfo(pc, null, "No " + VmFolder.Name + " folder on any drive");
                return null;
            }
            SetPcInfo(pc, null, "Working...");
            return folders;
        }

        // ================================================================== 3. extract
        async void Extract_Click(object sender, EventArgs e)
        {
            List<ZipSignature> zips = CheckedZips();
            if (zips.Count == 0) { Msg("Tick the archives you want to extract (section 1)."); return; }
            List<string> pcs = CheckedPcs();
            if (pcs.Count == 0) { Msg("Tick at least one PC (section 2)."); return; }
            bool own = chkOwnFolder.Checked, clean = chkClean.Checked;

            var sbq = new StringBuilder();
            sbq.AppendLine("Extract " + zips.Count + " archive(s) on " + pcs.Count + " PC(s)?\n");
            sbq.AppendLine("PCs: " + string.Join(", ", pcs.Take(15).Select(PcNames.Number)) + (pcs.Count > 15 ? ", ..." : ""));
            sbq.AppendLine();
            foreach (ZipSignature z in zips.Take(12))
                sbq.AppendLine("  " + z.FileName + "  ->  " + FileOps.ResultFolder(z, @"X:\" + VmFolder.Name, own).Replace(@"X:\", @"?:\"));
            if (zips.Count > 12) sbq.AppendLine("  ...and " + (zips.Count - 12) + " more");
            sbq.AppendLine();
            sbq.AppendLine("?: is the drive where each PC has its " + VmFolder.Name + " folder.");
            sbq.Append(clean ? "Old copies are removed first (clean reset)." : "Files with the same name are overwritten.");
            sbq.Append("\nVMware must be closed on those PCs.");
            if (MessageBox.Show(this, sbq.ToString(), "Confirm extract", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            SaveSettings();
            BeginBusy("Extracting...", pcs.Count * zips.Count * 100);
            CancellationToken ct = cts.Token;
            List<string> tree = ProtectTree();
            int ok = 0, failed = 0, skipped = 0;
            Logger.Write("=== EXTRACT " + zips.Count + " archive(s) on " + pcs.Count + " PC(s)");
            try
            {
                await Task.Run(() => Parallel.ForEach(pcs, Par(ct, 4), pc =>
                {
                    string name = PcNames.Display(pc);
                    List<string> folders = PreparePc(pc, ct);
                    if (folders == null)
                    {
                        Interlocked.Increment(ref skipped);
                        Interlocked.Add(ref progressDone, zips.Count * 100);
                        return;
                    }
                    string dest = folders[0];
                    int pcFailed = 0;
                    foreach (ZipSignature z in zips)
                    {
                        ct.ThrowIfCancellationRequested();
                        int reported = 0;
                        statusText = "Extracting " + z.FileName + " on " + PcNames.Number(pc) + "...";
                        try
                        {
                            int n = FileOps.ExtractArchive(z, pc, dest, own, clean, tree, Logger.Write, pct =>
                            {
                                pct = Math.Max(0, Math.Min(100, pct));
                                if (pct > reported) { Interlocked.Add(ref progressDone, pct - reported); reported = pct; }
                                statusText = "Extracting " + z.FileName + " on " + PcNames.Number(pc) + "...  " + pct + "%";
                            }, ct);
                            Logger.Write("Extracted " + z.FileName + " -> " + name + "  " + FileOps.ResultFolder(z, dest, own) + "  (" + n + " files)");
                            Interlocked.Increment(ref ok);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            pcFailed++;
                            Logger.Write("FAILED extracting " + z.FileName + " on " + name + ": " + ex.Message +
                                         (ex is IOException ? "  (is the VM still open, or is the disk full?)" : ""));
                        }
                        Interlocked.Add(ref progressDone, 100 - reported);
                    }
                    SetPcInfo(pc, null, pcFailed == 0 ? "OK - extracted" : "Extract had errors - see log");
                }), ct);
            }
            catch (OperationCanceledException) { Logger.Write("Extract stopped by user."); }
            catch (AggregateException ax) { Logger.Write("Extract error: " + ax.InnerException.Message); }
            finally
            {
                string summary = "Extract finished: " + ok + " OK, " + failed + " failed, " + skipped + " PC(s) skipped.";
                Logger.Write("=== " + summary);
                EndBusy(summary);
            }
        }

        // ================================================================== 4. scan
        async void Scan_Click(object sender, EventArgs e)
        {
            List<ZipSignature> zips = CheckedZips();
            if (zips.Count == 0)
            {
                zips = AllZips();
                if (zips.Count == 0) { Msg("Import your lab archives first (section 1)."); return; }
                Logger.Write("No archives ticked - looking for VMs from all " + zips.Count + " archive(s).");
            }
            List<string> pcs = CheckedPcs();
            if (pcs.Count == 0) { Msg("Tick at least one PC (section 2)."); return; }

            SaveSettings();
            lvResults.Items.Clear();
            UpdateCount();
            int min = (int)numMatch.Value;
            List<string> tree = ProtectTree();
            BeginBusy("Scanning...", pcs.Count);
            CancellationToken ct = cts.Token;
            int found = 0, skipped = 0;
            Logger.Write(string.Format("=== SCAN {0} PC(s) for VMs from {1} archive(s) (minimum match {2}%)", pcs.Count, zips.Count, min));
            try
            {
                await Task.Run(() => Parallel.ForEach(pcs, Par(ct, 8), pc =>
                {
                    string name = PcNames.Display(pc);
                    try
                    {
                        List<string> folders = PreparePc(pc, ct);
                        if (folders == null) { Interlocked.Increment(ref skipped); return; }
                        var sc = new Scanner(pc, zips, min, tree, folders, ct);
                        sc.Found = r => UI(() => AddResult(r));
                        sc.Status = p => statusText = "Scanning " + PcNames.Number(pc) + ":  " + p;
                        sc.Log = Logger.Write;
                        sc.Run(folders);
                        Interlocked.Add(ref found, sc.ItemsFound);
                        Logger.Write(string.Format("Scanned {0} [{1}]: {2} item(s) found{3}.", name, string.Join(", ", folders), sc.ItemsFound,
                            sc.FoldersUnreadable > 0 ? ", " + sc.FoldersUnreadable + " folder(s) could not be read" : ""));
                        SetPcInfo(pc, null, "OK - scanned, " + sc.ItemsFound + " found");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Logger.Write("ERROR scanning " + name + ": " + ex.Message);
                        SetPcInfo(pc, null, "Error - " + ex.Message);
                    }
                    finally { StepDone(); }
                }), ct);
            }
            catch (OperationCanceledException) { Logger.Write("Scan stopped by user."); }
            catch (AggregateException ax) { Logger.Write("Scan error: " + ax.InnerException.Message); }
            finally
            {
                string summary = "Scan finished: " + found + " item(s) found" + (skipped > 0 ? ", " + skipped + " PC(s) skipped" : "") + ".";
                Logger.Write("=== " + summary);
                EndBusy(summary);
            }
        }

        void AddResult(ScanResult r)
        {
            var it = new ListViewItem(PcNames.Number(r.Computer)) { Tag = r };
            it.SubItems.Add(r.LocalPath);
            it.SubItems.Add(r.IsFolder ? "Folder" : "File");
            it.SubItems.Add(r.ZipName);
            it.SubItems.Add(r.Percent + "%  (" + r.Matched + "/" + r.Checked + " files)");
            it.SubItems.Add(r.Modified == DateTime.MinValue ? "" : r.Modified.ToString("yyyy-MM-dd HH:mm"));
            if (r.Percent < 90) it.ForeColor = Color.DarkGoldenrod;
            lvResults.Items.Add(it);
            UpdateCount();
        }

        void UpdateCount()
        {
            lblCount.Text = lvResults.Items.Count + " found,  " + lvResults.CheckedItems.Count + " ticked";
        }

        class ResultSorter : System.Collections.IComparer
        {
            public int Column;
            public bool Descending;
            public int Compare(object x, object y)
            {
                var a = (ListViewItem)x;
                var b = (ListViewItem)y;
                var ra = (ScanResult)a.Tag;
                var rb = (ScanResult)b.Tag;
                int r;
                if (Column == 4) r = ra.Percent.CompareTo(rb.Percent);
                else if (Column == 5) r = ra.Modified.CompareTo(rb.Modified);
                else r = string.Compare(a.SubItems[Column].Text, b.SubItems[Column].Text, StringComparison.OrdinalIgnoreCase);
                return Descending ? -r : r;
            }
        }

        void Results_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (sorter.Column == e.Column) sorter.Descending = !sorter.Descending;
            else { sorter.Column = e.Column; sorter.Descending = false; }
            lvResults.ListViewItemSorter = sorter;
            lvResults.Sort();
        }

        // ================================================================== 4. delete
        async void Delete_Click(object sender, EventArgs e)
        {
            List<ListViewItem> items = lvResults.CheckedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0) { Msg("Tick the items you want to delete first."); return; }
            List<ScanResult> results = items.Select(i => (ScanResult)i.Tag).ToList();
            var pcList = results.Select(r => r.Computer).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var sb = new StringBuilder();
            sb.AppendFormat("Permanently delete {0} item(s) on {1} PC(s)?\n\n", results.Count, pcList.Count);
            foreach (ScanResult r in results.Take(12)) sb.AppendLine("  " + PcNames.Number(r.Computer) + "   " + r.LocalPath);
            if (results.Count > 12) sb.AppendLine("  ...and " + (results.Count - 12) + " more");
            sb.Append("\nDeleted files do NOT go to the Recycle Bin. This cannot be undone.");
            if (MessageBox.Show(this, sb.ToString(), "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            BeginBusy("Deleting...", results.Count);
            CancellationToken ct = cts.Token;
            List<string> tree = ProtectTree();
            var deleted = new ConcurrentBag<ScanResult>();
            int failed = 0, skipped = 0;
            Logger.Write("=== DELETE " + results.Count + " item(s) on " + pcList.Count + " PC(s)");
            try
            {
                var groups = results.GroupBy(r => r.Computer, StringComparer.OrdinalIgnoreCase).ToList();
                await Task.Run(() => Parallel.ForEach(groups, Par(ct, 8), g =>
                {
                    string pc = g.Key, name = PcNames.Display(pc);
                    string err = Net.CheckAccess(pc);
                    if (err != null)
                    {
                        Logger.Write("UNREACHABLE " + name + ": " + err + "  (" + g.Count() + " item(s) not deleted)");
                        Interlocked.Add(ref failed, g.Count());
                        Interlocked.Add(ref progressDone, g.Count());
                        return;
                    }
                    foreach (ScanResult r in g)
                    {
                        ct.ThrowIfCancellationRequested();
                        statusText = "Deleting on " + PcNames.Number(pc) + ":  " + r.LocalPath;
                        string why = Safety.WhyNotDeletable(r.LocalPath, PcNames.IsLocal(pc) ? tree : null);
                        if (why != null)
                        {
                            Interlocked.Increment(ref skipped);
                            Logger.Write("SKIPPED (protected: " + why + ") " + name + "  " + r.LocalPath);
                        }
                        else
                        {
                            string access = Paths.ToAccess(pc, r.LocalPath);
                            try
                            {
                                if (!FileOps.Exists(access)) Logger.Write("Already gone: " + name + "  " + r.LocalPath);
                                else
                                {
                                    FileOps.DeletePath(access);
                                    Logger.Write("Deleted " + name + "  " + r.LocalPath);
                                }
                                deleted.Add(r);
                            }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref failed);
                                Logger.Write("FAILED to delete " + name + "  " + r.LocalPath + ": " + ex.Message +
                                             (ex is IOException ? "  (is the VM still open on that PC?)" : ""));
                            }
                        }
                        StepDone();
                    }
                }), ct);
            }
            catch (OperationCanceledException) { Logger.Write("Delete stopped by user."); }
            catch (AggregateException ax) { Logger.Write("Delete error: " + ax.InnerException.Message); }
            finally
            {
                var gone = new HashSet<ScanResult>(deleted);
                lvResults.BeginUpdate();
                foreach (ListViewItem i in items) if (gone.Contains((ScanResult)i.Tag)) lvResults.Items.Remove(i);
                lvResults.EndUpdate();
                UpdateCount();
                string summary = "Delete finished: " + gone.Count + " deleted, " + failed + " failed, " + skipped + " skipped (protected).";
                Logger.Write("=== " + summary);
                EndBusy(summary);
            }
        }
    }
}
