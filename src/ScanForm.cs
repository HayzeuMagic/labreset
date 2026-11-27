// Lab Reset - "Scan network" window. C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LabReset
{
    /// <summary>
    /// Finds the PCs that are switched on in the network so they can be added to the list
    /// (for example another floor's PCs whose IP addresses you don't know).
    /// </summary>
    public class ScanForm : Form
    {
        readonly HashSet<string> inList;
        readonly TextBox txtRange;
        readonly ListView lv;
        readonly Label lblStatus;
        readonly ProgressBar progress;
        readonly Button btnScan, btnStop, btnAdd;
        CancellationTokenSource cts;
        int done, total, found;

        /// <summary>The IP addresses the teacher ticked and chose to add.</summary>
        public List<string> Selected = new List<string>();

        public string Range { get { return txtRange.Text.Trim(); } }

        class IpSorter : IComparer
        {
            public int Compare(object a, object b)
            {
                return NetScan.SortKey((string)((ListViewItem)a).Tag).CompareTo(NetScan.SortKey((string)((ListViewItem)b).Tag));
            }
        }

        public ScanForm(IEnumerable<string> pcsInList, string range, Font font)
        {
            inList = new HashSet<string>(pcsInList, StringComparer.OrdinalIgnoreCase);
            Text = "Scan network for PCs";
            Font = font;
            ClientSize = new Size(760, 540);
            MinimumSize = new Size(620, 400);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;

            var info = new Label
            {
                Dock = DockStyle.Top, Height = 52,
                Text = "Finds the PCs that are switched on and connected to the network, so you can add them without knowing their IP addresses.\n" +
                       "It starts with this PC's own network. If the other floor uses different addresses (for example 10.3.4.x), type that range instead or add it after a comma."
            };
            txtRange = new TextBox { Width = 380, Text = string.IsNullOrWhiteSpace(range) ? NetScan.DefaultRange() : range };
            txtRange.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && btnScan.Enabled) { e.SuppressKeyPress = true; Scan_Click(s, e); } };
            btnScan = new Button { Text = "Scan", AutoSize = true, Font = new Font(font, FontStyle.Bold) };
            btnScan.Click += Scan_Click;
            btnStop = new Button { Text = "Stop", AutoSize = true, Enabled = false };
            btnStop.Click += (s, e) => { if (cts != null) cts.Cancel(); };
            var btnReset = new Button { Text = "This PC's network", AutoSize = true };
            btnReset.Click += (s, e) => txtRange.Text = NetScan.DefaultRange();
            var rangeRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 2, 0, 2) };
            rangeRow.Controls.AddRange(new Control[] { new Label { Text = "Addresses:", AutoSize = true, Margin = new Padding(3, 7, 3, 0) }, txtRange, btnScan, btnStop, btnReset });
            var hint = new Label
            {
                Dock = DockStyle.Top, Height = 18, ForeColor = SystemColors.GrayText, AutoEllipsis = true,
                Text = "Examples:  10.3.3.0/24  (a whole network)    10.3.4.1-254    10.3.4.{100-199}    10.3.3.0/24, 10.3.4.0/24"
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, GridLines = true, HideSelection = false,
                ListViewItemSorter = new IpSorter()
            };
            lv.Columns.Add("PC", 55);
            lv.Columns.Add("IP address", 105);
            lv.Columns.Add("Computer name", 150);
            lv.Columns.Add("Found by", 130);
            lv.Columns.Add("Note", 300);
            // PCs that are already in the list can't be ticked again
            lv.ItemCheck += (s, e) => { if (inList.Contains((string)lv.Items[e.Index].Tag)) e.NewValue = CheckState.Unchecked; };
            lv.ItemChecked += (s, e) => UpdateAddButton();

            progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 14 };
            lblStatus = new Label { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(0, 4, 0, 0), AutoEllipsis = true,
                                    Text = "Click Scan. Scanning 254 addresses takes about 20 seconds." };

            btnAdd = new Button { Text = "Add ticked PCs to the list", AutoSize = true, Font = new Font(font, FontStyle.Bold), Enabled = false };
            btnAdd.Click += (s, e) =>
            {
                Selected = lv.CheckedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).Where(ip => !inList.Contains(ip)).ToList();
                DialogResult = DialogResult.OK;
            };
            var tickAll = new Button { Text = "Tick all", AutoSize = true };
            tickAll.Click += (s, e) => SetAll(true);
            var tickNone = new Button { Text = "Tick none", AutoSize = true };
            tickNone.Click += (s, e) => SetAll(false);
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
            bottom.Controls.AddRange(new Control[] { btnAdd, tickAll, tickNone, close });
            CancelButton = close;

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 8) };
            body.Controls.Add(lv);
            body.Controls.Add(hint);
            body.Controls.Add(rangeRow);
            body.Controls.Add(info);
            body.Controls.Add(lblStatus);
            body.Controls.Add(progress);
            body.Controls.Add(bottom);
            Controls.Add(body);
        }

        void SetAll(bool value)
        {
            lv.BeginUpdate();
            foreach (ListViewItem i in lv.Items) i.Checked = value && !inList.Contains((string)i.Tag);
            lv.EndUpdate();
            UpdateAddButton();
        }

        void UpdateAddButton()
        {
            int n = lv.CheckedItems.Count;
            btnAdd.Enabled = n > 0 && btnStop.Enabled == false;
            btnAdd.Text = n > 0 ? "Add " + n + " ticked PC(s) to the list" : "Add ticked PCs to the list";
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Scan_Click(this, e); // start straight away - that's what the window is for
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (cts != null) cts.Cancel();
            base.OnFormClosing(e);
        }

        void UI(Action a)
        {
            if (IsDisposed || Disposing) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        async void Scan_Click(object sender, EventArgs e)
        {
            var errors = new List<string>();
            List<string> ips = NetScan.ParseTargets(txtRange.Text, errors);
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join("\n", errors.Take(12)), "Lab Reset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (ips.Count == 0)
            {
                if (errors.Count == 0) MessageBox.Show(this, "Type the addresses to scan first, e.g. 10.3.3.0/24", "Lab Reset");
                return;
            }

            lv.Items.Clear();
            done = 0; found = 0; total = ips.Count;
            progress.Maximum = total;
            progress.Value = 0;
            btnScan.Enabled = false; txtRange.Enabled = false; btnStop.Enabled = true;
            UpdateAddButton();
            cts = new CancellationTokenSource();
            Logger.Write("=== SCAN NETWORK " + txtRange.Text.Trim() + " (" + total + " addresses)");
            var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (s, a) => ShowProgress("Scanning");
            timer.Start();
            bool stopped = false;
            try
            {
                await NetScan.Scan(ips, cts.Token, r => UI(() => AddResult(r)), () => Interlocked.Increment(ref done));
            }
            catch (OperationCanceledException) { stopped = true; }
            finally { timer.Stop(); timer.Dispose(); }
            if (IsDisposed) return;

            btnScan.Enabled = true; txtRange.Enabled = true; btnStop.Enabled = false;
            UpdateAddButton();
            int newPcs = lv.Items.Cast<ListViewItem>().Count(i => !inList.Contains((string)i.Tag));
            lblStatus.Text = (stopped ? "Stopped. " : "Finished. ") + found + " device(s) found, " + newPcs + " not in the list yet. " +
                             "Ticked = file sharing is on, so Lab Reset can most likely shut it down.";
            progress.Value = stopped ? progress.Value : progress.Maximum;
            Logger.Write("=== SCAN " + (stopped ? "stopped" : "finished") + ": " + found + " device(s) found, " + newPcs + " not in the list yet.");
        }

        void ShowProgress(string what)
        {
            int d = Math.Min(done, total);
            progress.Value = d;
            lblStatus.Text = what + "... " + d + " of " + total + " addresses checked, " + found + " device(s) found.";
        }

        void AddResult(ScanResult r)
        {
            found++;
            bool already = inList.Contains(r.Ip);
            string note;
            if (already) note = "Already in the list";
            else if (r.Sharing) note = "File sharing on - Lab Reset can most likely reach it";
            else if (r.Ping) note = "Answers ping, but file sharing is off/blocked";
            else note = "Firewall blocks everything - might be a printer or phone";
            string num = PcNames.Number(r.Ip);
            var it = new ListViewItem(num == r.Ip ? "" : num) { Tag = r.Ip };
            it.SubItems.Add(r.Ip);
            it.SubItems.Add(r.Name);
            it.SubItems.Add(r.FoundBy);
            it.SubItems.Add(note);
            if (already) it.ForeColor = SystemColors.GrayText;
            else if (!r.LooksLikeLabPc) it.ForeColor = Color.DarkGoldenrod;
            lv.Items.Add(it);
            lv.Sort();
            it.Checked = !already && r.LooksLikeLabPc;
            Logger.Write("  " + r.Ip + (r.Name.Length > 0 ? " (" + r.Name + ")" : "") + ": " + r.FoundBy + (already ? " - already in the list" : ""));
        }
    }
}
