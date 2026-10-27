using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace WithRea.StickyMemo
{
    [DataContract]
    public sealed class Note
    {
        [DataMember] public string Id;
        [DataMember] public string Text;
        [DataMember] public int X;
        [DataMember] public int Y;
        [DataMember] public int Width;
        [DataMember] public int Height;
        [DataMember] public int Color;
        [DataMember] public string UpdatedUtc;
        public Note Copy() { return (Note)MemberwiseClone(); }
        public override string ToString()
        {
            string first = (Text ?? "").Split(new[] { '\r', '\n' })[0];
            return first.Length == 0 ? "(빈 메모)" : first.Substring(0, Math.Min(first.Length, 60));
        }
    }

    [DataContract]
    public sealed class Notebook
    {
        [DataMember(IsRequired = true)] public int Version;
        [DataMember(IsRequired = true)] public List<Note> Notes;
    }

    // A separate store per data directory. No discovery or import of Microsoft data.
    public sealed class NoteStore : IDisposable
    {
        public const int MaxBytes = 16 * 1024 * 1024;
        public readonly string DirectoryPath;
        public string DataPath { get { return Path.Combine(DirectoryPath, "notes.json"); } }
        public string BackupPath { get { return DataPath + ".bak"; } }
        private FileStream lease;
        private List<Note> notes = new List<Note>();
        private bool disposed;

        public NoteStore(string directory)
        {
            DirectoryPath = Path.GetFullPath(directory);
            Directory.CreateDirectory(DirectoryPath);
            lease = new FileStream(Path.Combine(DirectoryPath, "app.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            try { notes = Read(DataPath); }
            catch { lease.Dispose(); throw; }
        }

        public List<Note> Snapshot()
        {
            EnsureOpen();
            return notes.Select(n => n.Copy()).ToList();
        }

        private static List<Note> Read(string path)
        {
            if (!File.Exists(path)) return new List<Note>();
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length > MaxBytes) throw new InvalidDataException("메모 파일이 16 MiB 제한을 초과했습니다.");
                Notebook book = (Notebook)Serializer().ReadObject(input);
                if (book == null || book.Version != 1 || book.Notes == null)
                    throw new InvalidDataException("지원하지 않는 메모 파일 형식입니다.");
                var ids = new HashSet<string>();
                foreach (Note note in book.Notes)
                {
                    Guid id;
                    if (note == null || !Guid.TryParse(note.Id, out id) || !ids.Add(note.Id) || note.Text == null ||
                        note.Width < 240 || note.Width > 5000 || note.Height < 200 || note.Height > 5000 ||
                        note.Color < 0 || note.Color > 3 || note.X < -100000 || note.X > 100000 ||
                        note.Y < -100000 || note.Y > 100000)
                        throw new InvalidDataException("메모 파일 검증에 실패했습니다. 원본 파일을 보존합니다.");
                }
                return book.Notes;
            }
        }

        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(Notebook));
        }

        public Note Create()
        {
            EnsureOpen();
            int offset = (notes.Count % 10) * 26;
            Note note = new Note { Id = Guid.NewGuid().ToString(), Text = "", X = 100 + offset,
                Y = 100 + offset, Width = 320, Height = 320, Color = 0, UpdatedUtc = DateTime.UtcNow.ToString("o") };
            List<Note> candidate = Snapshot();
            candidate.Add(note);
            Commit(candidate);
            return note.Copy();
        }

        public void Update(string id, string text, int x, int y, int width, int height, int color)
        {
            EnsureOpen();
            if (text == null || width < 240 || width > 5000 || height < 200 || height > 5000 ||
                color < 0 || color > 3 || x < -100000 || x > 100000 || y < -100000 || y > 100000)
                throw new ArgumentException("메모 값이 허용 범위를 벗어났습니다.");
            List<Note> candidate = Snapshot();
            Note note = candidate.Single(n => n.Id == id);
            note.Text = text; note.X = x; note.Y = y; note.Width = width; note.Height = height;
            note.Color = color; note.UpdatedUtc = DateTime.UtcNow.ToString("o");
            Commit(candidate);
        }

        public void Delete(string id)
        {
            List<Note> candidate = Snapshot();
            if (candidate.RemoveAll(n => n.Id == id) != 1) throw new ArgumentException("메모를 찾을 수 없습니다.");
            Commit(candidate);
        }

        private void Commit(List<Note> candidate)
        {
            string temp = Path.Combine(DirectoryPath, "notes-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Serializer().WriteObject(output, new Notebook { Version = 1, Notes = candidate });
                    if (output.Length > MaxBytes) throw new InvalidDataException("메모 저장 용량은 최대 16 MiB입니다.");
                    output.Flush(true);
                }
                // Rename/replace only after a complete, flushed file exists in the same directory.
                if (File.Exists(DataPath)) File.Replace(temp, DataPath, BackupPath, true);
                else File.Move(temp, DataPath);
                notes = candidate;
            }
            finally
            {
                // Cleanup failure must not report a committed transaction as failed.
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void EnsureOpen()
        {
            if (disposed) throw new ObjectDisposedException("NoteStore");
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lease.Dispose();
        }
    }
}
