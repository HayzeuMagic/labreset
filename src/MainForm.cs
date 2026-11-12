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

        ListView lvZips, lvResults;
        Label lblZipFolder, lblStatus, lblCount;
        TextBox txtDest, txtScanFolders, txtLog;
        NumericUpDown numMatch;
        CheckBox chkOwnFolder, chkClean;
        Button btnStop, btnDelete;
        ProgressBar progress;
        SplitContainer splitMain, splitLR;
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
            ClientSize = new Size(1280, 820);
            MinimumSize = new Size(960, 640);
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

            // ---- 2. Extract
            txtDest = new TextBox { Width = 300 };
            chkOwnFolder = new CheckBox { Text = "Put each archive in its own folder (if it doesn't already have one)", AutoSize = true, Margin = new Padding(3, 5, 16, 3) };
            chkClean = new CheckBox { Text = "Remove the old copy first (clean reset)", AutoSize = true, Margin = new Padding(3, 5, 3, 3) };
            var btnExtract = Btn("Extract ticked archives", Extract_Click);
            btnExtract.Font = bold;
            var gExtract = new GroupBox { Text = "2. Extract", Height = 128, Padding = new Padding(8, 4, 8, 4) };
            Stack(gExtract, null, new Control[] {
                Row(Lbl("Destination folder:"), txtDest, Btn("Browse...", BrowseDest_Click)),
                Row(chkOwnFolder, chkClean),
                Row(btnExtract) }, null);

            // ---- 3. Scan & delete
            txtScanFolders = new TextBox { Width = 250 };
            numMatch = new NumericUpDown { Minimum = 10, Maximum = 100, Width = 55, Margin = new Padding(3, 4, 3, 3) };
            var btnScan = Btn("Scan", Scan_Click);
            btnScan.Font = bold;
            btnStop = Btn("Stop", (s, e) => { if (cts != null) { cts.Cancel(); statusText = "Stopping..."; } }, false);
            btnStop.Enabled = false;
            var scanRow1 = Row(Lbl("Search only in (separate with ;   leave empty = all drives):"), txtScanFolders,
                               Lbl("Minimum match %:"), numMatch, btnScan, btnStop);
            lvResults = MakeList(new[] { "Path", "Type", "From archive", "Match", "Date modified" },
                                 new[] { 360, 55, 120, 125, 115 });
            lvResults.ColumnClick += Results_ColumnClick;
            lvResults.ItemChecked += (s, e) => UpdateCount();
            lvResults.DoubleClick += (s, e) => { if (lvResults.SelectedItems.Count > 0) ShowInExplorer(((ScanResult)lvResults.SelectedItems[0].Tag).AccessPath); };
            lblCount = Lbl("");
            btnDelete = Btn("Delete ticked items...", Delete_Click);
            btnDelete.ForeColor = Color.DarkRed;
            btnDelete.Font = bold;
            var resRow = Row(Btn("Tick all", (s, e) => SetAllChecked(lvResults, true), false),
                             Btn("Tick none", (s, e) => SetAllChecked(lvResults, false), false), btnDelete, lblCount);
            var resHint = new Label { Text = "Double-click a row to open it in Explorer. Click a column title to sort.", Height = 18, ForeColor = SystemColors.GrayText };
            var gScan = Group("3. Find and delete extracted lab files", lvResults, new Control[] { scanRow1 }, new Control[] { resRow, resHint });

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
            splitLR = new SplitContainer { Dock = DockStyle.Fill };
            splitLR.Panel1.Controls.Add(gZip);
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
                splitMain.SplitterDistance = Math.Max(300, splitMain.Height - 190);
                splitLR.SplitterDistance = 450;
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
            List<string> sources = ExistingSourceFolders();
            if (!File.Exists(settingsFile))
            {
                // First run on this PC: use whichever drive has the VMs.
                settings.Destination = sources.Count > 0 ? sources[0] : SourceFolders[0];
                settings.ScanFolders = string.Join("; ", new[] { @"C:\Users" }.Concat(sources));
            }
            else if (sources.Count > 0)
            {
                // Settings copied from a PC whose VMs were on the other drive: point at the drive this PC uses.
                foreach (string missing in SourceFolders.Where(f => !sources.Contains(f)))
                {
                    if (string.Equals(Paths.Normalize(settings.Destination), missing, StringComparison.OrdinalIgnoreCase)) settings.Destination = sources[0];
                    settings.ScanFolders = System.Text.RegularExpressions.Regex.Replace(settings.ScanFolders,
                        System.Text.RegularExpressions.Regex.Escape(missing) + @"(?=\s*(;|$))", sources[0].Replace("$", "$$"),
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }
            txtDest.Text = settings.Destination;
            txtScanFolders.Text = settings.ScanFolders;
            numMatch.Value = Math.Max(numMatch.Minimum, Math.Min(numMatch.Maximum, settings.MinMatch));
            chkOwnFolder.Checked = settings.OwnFolder;
            chkClean.Checked = settings.CleanFirst;
            lblZipFolder.Text = "Library: " + zipFolder;
        }

        void SaveSettings()
        {
            try
            {
                settings.Destination = txtDest.Text.Trim();
                settings.ScanFolders = txtScanFolders.Text.Trim();
                settings.MinMatch = (int)numMatch.Value;
                settings.OwnFolder = chkOwnFolder.Checked;
                settings.CleanFirst = chkClean.Checked;
                settings.CheckedZips = lvZips.CheckedItems.Cast<ListViewItem>().Select(i => i.Text).ToList();
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
            progressDone = progress.Maximum;
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

        // Lab PCs that already have the VM archives keep them in one of these folders.
        static readonly string[] SourceFolders = { @"C:\__RivanVMs", @"D:\__RivanVMs" };
        List<string> archivePaths = new List<string>();

        static List<string> ExistingSourceFolders()
        {
            var l = new List<string>();
            foreach (string f in SourceFolders)
            {
                try { if (Directory.Exists(f)) l.Add(f); } catch { }
            }
            return l;
        }

        // The app folder, its library and every archive file can never be deleted.
        List<string> ProtectTree()
        {
            var l = new List<string> { zipFolder, appDir };
            l.AddRange(archivePaths);
            return l;
        }

        List<string> ProtectExact()
        {
            var l = new List<string>();
            if (Safety.CheckDestination(txtDest.Text) == null) l.Add(Paths.Normalize(txtDest.Text));
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

        // ================================================================== 2. extract
        const string Here = PcNames.ThisPc;

        void BrowseDest_Click(object sender, EventArgs e)
        {
            using (var d = new FolderBrowserDialog { Description = "Choose the folder to extract the lab archives into.", SelectedPath = txtDest.Text })
                if (d.ShowDialog(this) == DialogResult.OK) txtDest.Text = d.SelectedPath;
        }

        async void Extract_Click(object sender, EventArgs e)
        {
            List<ZipSignature> zips = CheckedZips();
            if (zips.Count == 0) { Msg("Tick the archives you want to extract (section 1)."); return; }
            string destErr = Safety.CheckDestination(txtDest.Text);
            if (destErr != null) { Msg(destErr, MessageBoxIcon.Warning); return; }
            string dest = Paths.Normalize(txtDest.Text);
            txtDest.Text = dest;
            bool own = chkOwnFolder.Checked, clean = chkClean.Checked;

            var sbq = new StringBuilder();
            sbq.AppendLine("Extract " + zips.Count + " archive(s)?\n");
            foreach (ZipSignature z in zips.Take(12)) sbq.AppendLine("  " + z.FileName + "  ->  " + FileOps.ResultFolder(z, dest, own));
            if (zips.Count > 12) sbq.AppendLine("  ...and " + (zips.Count - 12) + " more");
            sbq.AppendLine();
            sbq.Append(clean ? "Old copies are removed first (clean reset)." : "Files with the same name are overwritten.");
            sbq.Append("\nClose VMware/VirtualBox first if these VMs are open.");
            if (MessageBox.Show(this, sbq.ToString(), "Confirm extract", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            SaveSettings();
            BeginBusy("Extracting...", zips.Count * 100);
            CancellationToken ct = cts.Token;
            List<string> tree = ProtectTree();
            int ok = 0, failed = 0;
            Logger.Write("=== EXTRACT " + zips.Count + " archive(s) to " + dest);
            try
            {
                await Task.Run(() =>
                {
                    for (int idx = 0; idx < zips.Count; idx++)
                    {
                        ZipSignature z = zips[idx];
                        int baseDone = idx * 100;
                        ct.ThrowIfCancellationRequested();
                        statusText = "Extracting " + z.FileName + " (" + (idx + 1) + " of " + zips.Count + ")...";
                        progressDone = baseDone;
                        try
                        {
                            int n = FileOps.ExtractArchive(z, Here, dest, own, clean, tree, Logger.Write, pct =>
                            {
                                pct = Math.Max(0, Math.Min(100, pct));
                                progressDone = baseDone + pct;
                                statusText = "Extracting " + z.FileName + " (" + (idx + 1) + " of " + zips.Count + ")...  " + pct + "%";
                            }, ct);
                            Logger.Write("Extracted " + z.FileName + " -> " + FileOps.ResultFolder(z, dest, own) + "  (" + n + " files)");
                            ok++;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            failed++;
                            Logger.Write("FAILED extracting " + z.FileName + ": " + ex.Message +
                                         (ex is IOException ? "  (is the VM still open, or is the disk full?)" : ""));
                        }
                        progressDone = baseDone + 100;
                    }
                }, ct);
            }
            catch (OperationCanceledException) { Logger.Write("Extract stopped by user."); }
            finally
            {
                string summary = "Extract finished: " + ok + " OK, " + failed + " failed.";
                Logger.Write("=== " + summary);
                EndBusy(summary);
            }
        }

        // ================================================================== 3. scan
        string ParseFolders(string text, out List<string> folders)
        {
            folders = new List<string>();
            foreach (string raw in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw.Trim().Length == 0) continue;
                string p = Paths.Normalize(raw);
                if (p == null || !Paths.IsDrivePath(p))
                    return "\"" + raw.Trim() + "\" is not a full folder path. Use paths like C:\\Users, separated by ;";
                if (!folders.Contains(p, StringComparer.OrdinalIgnoreCase)) folders.Add(p);
            }
            // drop folders that are inside another listed folder
            List<string> all = folders;
            folders = all.Where(f => !all.Any(o => !o.Equals(f, StringComparison.OrdinalIgnoreCase) && Paths.IsSameOrUnder(f, o))).ToList();
            return null;
        }

        async void Scan_Click(object sender, EventArgs e)
        {
            List<ZipSignature> zips = CheckedZips();
            if (zips.Count == 0)
            {
                zips = AllZips();
                if (zips.Count == 0) { Msg("Import your lab archives first (section 1)."); return; }
                Logger.Write("No archives ticked - looking for files from all " + zips.Count + " archive(s).");
            }
            List<string> folders;
            string ferr = ParseFolders(txtScanFolders.Text, out folders);
            if (ferr != null) { Msg(ferr, MessageBoxIcon.Warning); return; }

            SaveSettings();
            lvResults.Items.Clear();
            UpdateCount();
            int min = (int)numMatch.Value;
            List<string> tree = ProtectTree(), exact = ProtectExact();
            BeginBusy("Scanning...", 1);
            progress.Style = ProgressBarStyle.Marquee;
            CancellationToken ct = cts.Token;
            var sc = new Scanner(Here, zips, min, tree, exact, ct);
            sc.Found = r => UI(() => AddResult(r));
            sc.Status = p => statusText = "Scanning:  " + p;
            sc.Log = Logger.Write;
            Logger.Write(string.Format("=== SCAN for files from {0} archive(s) in {1} (minimum match {2}%)",
                zips.Count, folders.Count == 0 ? "all drives" : string.Join("; ", folders), min));
            try
            {
                await Task.Run(() => sc.Run(folders), ct);
            }
            catch (OperationCanceledException) { Logger.Write("Scan stopped by user."); }
            catch (Exception ex) { Logger.Write("Scan error: " + ex.Message); }
            finally
            {
                progress.Style = ProgressBarStyle.Blocks;
                string summary = string.Format("Scan finished: {0} folders searched, {1} item(s) found{2}.", sc.FoldersScanned, sc.ItemsFound,
                    sc.FoldersUnreadable > 0 ? ", " + sc.FoldersUnreadable + " folder(s) could not be read" : "");
                Logger.Write("=== " + summary);
                EndBusy(summary);
            }
        }

        void AddResult(ScanResult r)
        {
            var it = new ListViewItem(r.LocalPath) { Tag = r };
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
                if (Column == 3) r = ra.Percent.CompareTo(rb.Percent);
                else if (Column == 4) r = ra.Modified.CompareTo(rb.Modified);
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

        // ================================================================== 3. delete
        async void Delete_Click(object sender, EventArgs e)
        {
            List<ListViewItem> items = lvResults.CheckedItems.Cast<ListViewItem>().ToList();
            if (items.Count == 0) { Msg("Tick the items you want to delete first."); return; }
            List<ScanResult> results = items.Select(i => (ScanResult)i.Tag).ToList();

            var sb = new StringBuilder();
            sb.AppendFormat("Permanently delete {0} item(s)?\n\n", results.Count);
            foreach (ScanResult r in results.Take(12)) sb.AppendLine("  " + r.LocalPath);
            if (results.Count > 12) sb.AppendLine("  ...and " + (results.Count - 12) + " more");
            sb.Append("\nDeleted files do NOT go to the Recycle Bin. This cannot be undone.");
            if (MessageBox.Show(this, sb.ToString(), "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            BeginBusy("Deleting...", results.Count);
            CancellationToken ct = cts.Token;
            List<string> tree = ProtectTree(), exact = ProtectExact();
            var deleted = new ConcurrentBag<ScanResult>();
            int failed = 0, skipped = 0;
            Logger.Write("=== DELETE " + results.Count + " item(s)");
            try
            {
                await Task.Run(() =>
                {
                    foreach (ScanResult r in results)
                    {
                        ct.ThrowIfCancellationRequested();
                        statusText = "Deleting:  " + r.LocalPath;
                        string why = Safety.WhyProtected(r.LocalPath, tree, exact);
                        if (why != null)
                        {
                            skipped++;
                            Logger.Write("SKIPPED (protected: " + why + ")  " + r.LocalPath);
                        }
                        else
                        {
                            try
                            {
                                if (!FileOps.Exists(r.LocalPath)) Logger.Write("Already gone: " + r.LocalPath);
                                else
                                {
                                    FileOps.DeletePath(r.LocalPath);
                                    Logger.Write("Deleted " + r.LocalPath);
                                }
                                deleted.Add(r);
                            }
                            catch (Exception ex)
                            {
                                failed++;
                                Logger.Write("FAILED to delete " + r.LocalPath + ": " + ex.Message +
                                             (ex is IOException ? "  (a file may still be open - close it and try again)" : ""));
                            }
                        }
                        StepDone();
                    }
                }, ct);
            }
            catch (OperationCanceledException) { Logger.Write("Delete stopped by user."); }
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
