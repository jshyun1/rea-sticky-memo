using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WithRea.StickyMemo;

internal static class Tests
{
    private static int passed;
    private static int failed;
    private static string root;
    private const string Sample = "한글 메모 📝\r\nsecond line\t\"quoted\" \\ path";

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 2 && args[0] == "--ui-write") return UiStage(args[1], false);
        if (args.Length == 2 && args[0] == "--ui-read") return UiStage(args[1], true);
        if (args.Length != 1) return 2;
        root = Path.Combine(args[0], "test-runs", DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Check("CRUD, Unicode and restart", Crud);
        Check("Snapshot isolation", SnapshotIsolation);
        Check("Exclusive store ownership", ExclusiveLease);
        Check("Invalid files preserved", InvalidFiles);
        Check("Atomic save failure preserves prior state", SaveFailure);
        Check("Backup is previous committed state", Backup);
        Check("Oversize edit is rejected without loss", Oversize);
        Check("Two real UI processes: autosave, close, delete, restore", UiProcesses);
        Check("Packaged app startup and graceful exit", AppSmoke);
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
        Console.WriteLine("Artifacts: " + root);
        return failed == 0 ? 0 : 1;
    }

    private static void Check(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }
    private static void Expect(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    private static void Throws(Action action)
    {
        bool threw = false;
        try { action(); } catch { threw = true; }
        Expect(threw, "Expected an exception");
    }
    private static string Dir(string name) { return Path.Combine(root, name); }
    private static void Edit(NoteStore store, Note note, string text)
    {
        store.Update(note.Id, text, 210, 180, 360, 330, 2);
    }
    private static void Crud()
    {
        string id;
        using (var store = new NoteStore(Dir("crud")))
        {
            Expect(store.Snapshot().Count == 0, "New store must be empty");
            Note a = store.Create(), b = store.Create(); id = a.Id;
            Expect(a.Id != b.Id, "IDs must differ");
            Edit(store, a, Sample); Edit(store, b, "discard"); store.Delete(b.Id);
        }
        using (var restored = new NoteStore(Dir("crud")))
        {
            Note a = restored.Snapshot().Single();
            Expect(a.Id == id && a.Text == Sample && a.Width == 360 && a.Color == 2 && a.X == 210, "Restart content/bounds/color mismatch");
            restored.Delete(id);
        }
        using (var empty = new NoteStore(Dir("crud"))) Expect(empty.Snapshot().Count == 0, "Deleted last note resurrected");
    }
    private static void SnapshotIsolation()
    {
        using (var store = new NoteStore(Dir("snapshot")))
        {
            Note original = store.Create(); original.Text = "outside edit";
            List<Note> snapshot = store.Snapshot(); snapshot[0].Text = "mutated"; snapshot.Clear();
            Expect(store.Snapshot().Single().Text == "", "Caller mutated store without commit");
            Throws(delegate { store.Delete(Guid.NewGuid().ToString()); });
            Expect(store.Snapshot().Count == 1, "Unknown delete changed state");
        }
    }
    private static void ExclusiveLease()
    {
        using (var first = new NoteStore(Dir("lease")))
            Throws(delegate { using (var second = new NoteStore(Dir("lease"))) second.Create(); });
        using (var reopened = new NoteStore(Dir("lease"))) reopened.Create();
    }
    private static void InvalidFiles()
    {
        string dir = Dir("invalid"); Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "notes.json");
        foreach (string invalid in new[] { "{bad", "{\"Version\":2,\"Notes\":[]}", "{\"Version\":1,\"Notes\":null}",
            "{\"Version\":1,\"Notes\":[null]}" })
        {
            File.WriteAllText(path, invalid);
            byte[] before = File.ReadAllBytes(path);
            Throws(delegate { using (var store = new NoteStore(dir)) store.Create(); });
            Expect(before.SequenceEqual(File.ReadAllBytes(path)), "Invalid file was modified");
        }
        // Detect duplicate IDs and invalid geometry in an otherwise valid document.
        File.Delete(path);
        using (var store = new NoteStore(dir)) { store.Create(); store.Create(); }
        string valid = File.ReadAllText(path);
        string[] ids;
        using (var store = new NoteStore(dir)) ids = store.Snapshot().Select(n => n.Id).ToArray();
        File.WriteAllText(path, valid.Replace(ids[1], ids[0]));
        Throws(delegate { using (var store = new NoteStore(dir)) store.Create(); });
        Expect(File.ReadAllText(path).Contains(ids[0]), "Duplicate file not preserved");
        File.WriteAllText(path, valid.Replace("\"Width\":320", "\"Width\":0"));
        Throws(delegate { using (var store = new NoteStore(dir)) store.Create(); });
    }
    private static void SaveFailure()
    {
        using (var store = new NoteStore(Dir("failure")))
        {
            Note note = store.Create(); Edit(store, note, "old");
            byte[] prior = File.ReadAllBytes(store.DataPath);
            using (var blocker = new FileStream(store.DataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Throws(delegate { Edit(store, note, "new"); });
                Throws(delegate { store.Delete(note.Id); });
                Expect(store.Snapshot().Single().Text == "old", "Failed update/delete changed model");
                Expect(prior.SequenceEqual(File.ReadAllBytes(store.DataPath)), "Failed replace changed disk");
            }
            Edit(store, note, "retry succeeded");
            Expect(store.Snapshot().Single().Text == "retry succeeded", "Retry failed");
            Expect(Directory.GetFiles(store.DirectoryPath, "*.tmp").Length == 0, "Temporary file leaked");
        }
    }
    private static void Backup()
    {
        using (var store = new NoteStore(Dir("backup")))
        {
            Note note = store.Create(); Edit(store, note, "previous");
            byte[] prior = File.ReadAllBytes(store.DataPath); Edit(store, note, "latest");
            Expect(prior.SequenceEqual(File.ReadAllBytes(store.BackupPath)), "Backup differs from previous version");
        }
    }
    private static void Oversize()
    {
        using (var store = new NoteStore(Dir("oversize")))
        {
            Note note = store.Create(); Edit(store, note, "retained");
            Throws(delegate { Edit(store, note, new string('x', NoteStore.MaxBytes + 1)); });
            Expect(store.Snapshot().Single().Text == "retained", "Oversize save lost old text");
        }
    }
    private static void RunChild(string mode, string dir)
    {
        var start = new ProcessStartInfo(Application.ExecutablePath, mode + " \"" + dir + "\"") {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using (Process process = Process.Start(start))
        {
            if (!process.WaitForExit(30000)) { process.Kill(); throw new Exception("UI stage timed out"); }
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            File.WriteAllText(Path.Combine(root, mode.Substring(2) + ".log"), output);
            Expect(process.ExitCode == 0, mode + ": " + output);
        }
    }
    private static void UiProcesses()
    {
        string directory = Dir("ui"); RunChild("--ui-write", directory); RunChild("--ui-read", directory);
    }
    private static int UiStage(string dir, bool restoring)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Exception failure = null;
        using (var store = new NoteStore(dir))
        using (var book = new NotebookWindow(store))
        using (var clock = new System.Windows.Forms.Timer { Interval = 150 })
        {
            book.SuppressDialogs = true;
            int step = 0;
            MemoWindow first = null;
            Stopwatch elapsed = Stopwatch.StartNew();
            clock.Tick += delegate
            {
                try
                {
                    if (restoring)
                    {
                        Expect(book.MemoWindows.Count == 2, "Two windows were not restored");
                        List<string> texts = book.MemoWindows.Select(w => w.Editor.Text).ToList();
                        Expect(texts.Contains(Sample + "\r\nshutdown") && texts.Contains("second"), "Separate process restore mismatch");
                        Expect(store.Snapshot().All(n => n.Text != "deleted before timer"), "Deleted note restored");
                        Capture(book, Path.Combine(dir, "restored-windows.png"));
                        clock.Stop(); book.Close(); return;
                    }
                    if (step == 0)
                    {
                        Expect(book.MemoWindows.Count == 0, "Fresh store unexpectedly has windows");
                        book.NewButton.PerformClick(); first = book.MemoWindows.Single(); first.Editor.Text = Sample;
                        first.NewButton.PerformClick();
                        MemoWindow second = book.MemoWindows.Single(w => w != first); second.Editor.Text = "second";
                        second.NewButton.PerformClick();
                        MemoWindow discarded = book.MemoWindows.Single(w => w != first && w != second);
                        discarded.Editor.Text = "deleted before timer"; discarded.DeleteButton.PerformClick();
                        Expect(book.MemoWindows.Count == 2 && store.Snapshot().Count == 2, "Delete button did not remove note/window");
                        elapsed.Restart(); step = 1; return;
                    }
                    if (step == 1 && elapsed.ElapsedMilliseconds >= 1200)
                    {
                        Expect(store.Snapshot().Any(n => n.Text == Sample), "Autosave timer did not save text");
                        Expect(first.SaveStatus == "저장됨", "Autosave status mismatch");
                        // Closing a note preserves it; reopening does not duplicate the live window.
                        string id = first.NoteId; first.Close();
                        Expect(store.Snapshot().Count == 2 && book.MemoWindows.Count == 1, "Close deleted data");
                        first = book.OpenMemo(id); Expect(book.OpenMemo(id) == first, "Duplicate live window");
                        // Real sharing violation: keep dirty editor and cancel app close, then retry.
                        using (var blocker = new FileStream(store.DataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            first.Editor.AppendText("\r\nretry");
                            Expect(!first.FlushPending() && first.SaveStatus.Contains("실패"), "UI did not report failed save");
                            book.Close();
                            Expect(!book.IsDisposed && !first.IsDisposed, "Close discarded unsaved text");
                        }
                        elapsed.Restart(); step = 2; return;
                    }
                    if (step == 2 && elapsed.ElapsedMilliseconds >= 2600)
                    {
                        Expect(store.Snapshot().Single(n => n.Id == first.NoteId).Text == Sample + "\r\nretry", "UI failed retry after write error");
                        first.Editor.Text = Sample;
                        first.Editor.AppendText("\r\nshutdown");
                        Capture(book, Path.Combine(dir, "created-windows.png"));
                        clock.Stop(); book.Close(); // Pending edit must be flushed without waiting for debounce.
                    }
                }
                catch (Exception error)
                {
                    failure = error; clock.Stop();
                    foreach (MemoWindow window in book.MemoWindows) window.Dispose();
                    book.Dispose(); Application.ExitThread();
                }
            };
            book.Shown += delegate { clock.Start(); };
            Application.Run(book);
        }
        if (failure != null) { Console.Error.WriteLine(failure); return 1; }
        Console.WriteLine(restoring ? "UI restore assertions passed" : "UI create/edit/autosave/delete/close assertions passed");
        return 0;
    }
    private static void Capture(NotebookWindow book, string path)
    {
        var forms = new List<Form> { book }; forms.AddRange(book.MemoWindows);
        using (var image = new Bitmap(forms.Sum(f => f.Width + 12), forms.Max(f => f.Height) + 24))
        using (Graphics graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.FromArgb(232, 235, 240)); int x = 6;
            foreach (Form form in forms)
            {
                using (var rendered = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
                    graphics.DrawImageUnscaled(rendered, x, 12); x += form.Width + 12;
                }
            }
            image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    private static void AppSmoke()
    {
        string executable = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "StickyMemo.exe");
        using (Process process = Process.Start(new ProcessStartInfo(executable, "--data-dir \"" + Dir("app-smoke") + "\"") { UseShellExecute = false }))
        {
            try
            {
                Expect(process.WaitForInputIdle(10000), "App did not reach idle");
                // Input idle can precede creation of Application.Run's first window.
                Stopwatch wait = Stopwatch.StartNew();
                while (!process.HasExited && process.MainWindowHandle == IntPtr.Zero && wait.ElapsedMilliseconds < 10000)
                {
                    Thread.Sleep(50); process.Refresh();
                }
                Expect(!process.HasExited && process.MainWindowHandle != IntPtr.Zero, "App has no main window");
                Expect(process.CloseMainWindow(), "Could not request normal shutdown");
                Expect(process.WaitForExit(10000) && process.ExitCode == 0, "App failed normal shutdown");
            }
            finally { if (!process.HasExited) process.Kill(); }
        }
    }
}
