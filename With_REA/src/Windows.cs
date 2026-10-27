using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WithRea.StickyMemo
{
    public sealed class NotebookWindow : Form
    {
        private readonly NoteStore store;
        private readonly ListBox list = new ListBox();
        private readonly Dictionary<string, MemoWindow> windows = new Dictionary<string, MemoWindow>();
        public Button NewButton { get; private set; }
        public bool SuppressDialogs { get; set; }
        public List<MemoWindow> MemoWindows { get { return windows.Values.ToList(); } }

        public NotebookWindow(NoteStore store)
        {
            this.store = store;
            Text = "Sticky Memo · 메모 목록";
            Size = new Size(430, 440); MinimumSize = new Size(360, 300);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Malgun Gothic", 10);
            var title = new Label { Text = "내 메모", Dock = DockStyle.Top, Height = 52,
                Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Padding = new Padding(12, 10, 0, 0) };
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 3, 0, 0) };
            NewButton = new Button { Text = "+ 새 메모", AutoSize = true, Name = "NewMemo" };
            var open = new Button { Text = "열기", AutoSize = true };
            var all = new Button { Text = "모두 열기", AutoSize = true };
            NewButton.Click += delegate { NewMemo(); };
            open.Click += delegate { Note note = list.SelectedItem as Note; if (note != null) OpenMemo(note.Id); };
            all.Click += delegate { OpenAll(); };
            toolbar.Controls.AddRange(new Control[] { NewButton, open, all });
            list.Dock = DockStyle.Fill; list.IntegralHeight = false; list.Name = "MemoList";
            list.DoubleClick += delegate { Note note = list.SelectedItem as Note; if (note != null) OpenMemo(note.Id); };
            var hint = new Label { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(10),
                Text = "입력 후 자동 저장됩니다.\n메모의 X는 창 닫기 · 삭제는 메모 안의 삭제 버튼" };
            Controls.Add(list); Controls.Add(toolbar); Controls.Add(title); Controls.Add(hint);
            RefreshList();
            Shown += delegate { OpenAll(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                // Validate all saves before closing any window; a failed save keeps text accessible.
                foreach (MemoWindow window in MemoWindows)
                    if (!window.FlushPending()) { e.Cancel = true; return; }
                foreach (MemoWindow window in MemoWindows) window.Close();
            };
        }

        public MemoWindow NewMemo()
        {
            try
            {
                Note note = store.Create(); RefreshList(); return OpenMemo(note.Id);
            }
            catch (Exception e) { ShowError(e); return null; }
        }
        public MemoWindow OpenMemo(string id)
        {
            MemoWindow existing;
            if (windows.TryGetValue(id, out existing))
            {
                if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
                existing.Show(); existing.Activate(); return existing;
            }
            Note note = store.Snapshot().Single(n => n.Id == id);
            var window = new MemoWindow(this, store, note);
            windows.Add(id, window);
            window.FormClosed += delegate { windows.Remove(id); RefreshList(); };
            window.Show(); return window;
        }
        public void OpenAll() { foreach (Note note in store.Snapshot()) OpenMemo(note.Id); }
        public void RefreshList()
        {
            string selected = list.SelectedItem is Note ? ((Note)list.SelectedItem).Id : null;
            list.BeginUpdate(); list.Items.Clear();
            foreach (Note note in store.Snapshot())
            {
                int index = list.Items.Add(note);
                if (note.Id == selected) list.SelectedIndex = index;
            }
            list.EndUpdate();
        }
        public void ShowError(Exception error)
        {
            if (!SuppressDialogs) MessageBox.Show(this, error.Message, "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N)) { NewMemo(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    public sealed class MemoWindow : Form
    {
        private static readonly Color[] Palette = { Color.FromArgb(255, 245, 177), Color.FromArgb(218, 239, 213),
            Color.FromArgb(217, 233, 255), Color.FromArgb(251, 220, 234) };
        private readonly NotebookWindow notebook;
        private readonly NoteStore store;
        private readonly Timer timer = new Timer { Interval = 450 };
        private readonly Label status = new Label();
        private readonly ComboBox colors = new ComboBox();
        private bool dirty;
        private bool ready;
        private bool deleted;
        public string NoteId { get; private set; }
        public TextBox Editor { get; private set; }
        public Button DeleteButton { get; private set; }
        public Button NewButton { get; private set; }
        public string SaveStatus { get { return status.Text; } }

        public MemoWindow(NotebookWindow notebook, NoteStore store, Note note)
        {
            this.notebook = notebook; this.store = store; NoteId = note.Id;
            Text = "메모 · Sticky Memo"; MinimumSize = new Size(240, 200); MaximumSize = new Size(5000, 5000);
            Font = new Font("Malgun Gothic", 10);
            StartPosition = FormStartPosition.Manual;
            Bounds = VisibleBounds(new Rectangle(note.X, note.Y, note.Width, note.Height));
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(5) };
            NewButton = new Button { Text = "+", Width = 34, AccessibleName = "새 메모" };
            DeleteButton = new Button { Text = "삭제", Width = 50, AccessibleName = "이 메모 삭제" };
            colors.DropDownStyle = ComboBoxStyle.DropDownList; colors.Width = 80;
            colors.Items.AddRange(new object[] { "노랑", "초록", "파랑", "분홍" }); colors.SelectedIndex = note.Color;
            colors.AccessibleName = "메모 색상";
            NewButton.Click += delegate { notebook.NewMemo(); };
            DeleteButton.Click += delegate
            {
                if (notebook.SuppressDialogs || MessageBox.Show(this, "이 메모를 영구 삭제할까요?", "메모 삭제",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                    DeleteMemo();
            };
            toolbar.Controls.AddRange(new Control[] { NewButton, colors, DeleteButton });
            status.Dock = DockStyle.Bottom; status.Height = 28; status.Padding = new Padding(6, 3, 0, 0);
            status.Text = "저장됨";
            Editor = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true,
                AcceptsTab = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                Font = new Font("Malgun Gothic", 12), Text = note.Text, Name = "MemoText",
                MaxLength = NoteStore.MaxBytes, AccessibleName = "메모 내용" };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
            body.Controls.Add(Editor);
            Controls.Add(body); Controls.Add(toolbar); Controls.Add(status);
            ApplyColor();
            Editor.TextChanged += delegate { MarkDirty(); };
            colors.SelectedIndexChanged += delegate { ApplyColor(); MarkDirty(); };
            LocationChanged += delegate { if (WindowState == FormWindowState.Normal) MarkDirty(); };
            SizeChanged += delegate { if (WindowState == FormWindowState.Normal) MarkDirty(); };
            timer.Tick += delegate { FlushPending(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!deleted && !FlushPending()) e.Cancel = true; };
            FormClosed += delegate { timer.Dispose(); };
            ready = true;
            // Persist screen-clamped bounds on next save, even without an edit.
            if (Bounds != new Rectangle(note.X, note.Y, note.Width, note.Height)) MarkDirty();
        }

        private static Rectangle VisibleBounds(Rectangle saved)
        {
            Rectangle screen = Screen.FromRectangle(saved).WorkingArea;
            int width = Math.Max(240, Math.Min(saved.Width, screen.Width));
            int height = Math.Max(200, Math.Min(saved.Height, screen.Height));
            return new Rectangle(Math.Max(screen.Left, Math.Min(saved.X, screen.Right - width)),
                Math.Max(screen.Top, Math.Min(saved.Y, screen.Bottom - height)), width, height);
        }
        private void ApplyColor() { BackColor = Palette[colors.SelectedIndex]; Editor.BackColor = BackColor; }
        private void MarkDirty()
        {
            if (!ready || deleted) return;
            dirty = true; status.Text = "저장 대기…"; timer.Stop(); timer.Interval = 450; timer.Start();
        }
        public bool FlushPending()
        {
            timer.Stop();
            if (!dirty || deleted) return true;
            try
            {
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                store.Update(NoteId, Editor.Text, bounds.X, bounds.Y, bounds.Width, bounds.Height, colors.SelectedIndex);
                dirty = false; status.Text = "저장됨"; notebook.RefreshList(); return true;
            }
            catch (Exception error)
            {
                status.Text = "저장 실패 · 2초 후 재시도";
                status.AccessibleDescription = error.Message;
                timer.Interval = 2000; timer.Start(); return false;
            }
        }
        private void DeleteMemo()
        {
            try { store.Delete(NoteId); deleted = true; timer.Stop(); Close(); notebook.RefreshList(); }
            catch (Exception error) { notebook.ShowError(error); }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N)) { notebook.NewMemo(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { FlushPending(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
