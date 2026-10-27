using System;
using System.IO;
using System.Windows.Forms;

namespace WithRea.StickyMemo
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WithREA", "StickyMemoPoC");
                if (args.Length != 0)
                {
                    if (args.Length != 2 || args[0] != "--data-dir" || String.IsNullOrWhiteSpace(args[1]))
                        throw new ArgumentException("사용법: StickyMemo.exe [--data-dir <새 메모 전용 폴더>]");
                    directory = args[1];
                }
                using (var store = new NoteStore(directory))
                using (var window = new NotebookWindow(store)) Application.Run(window);
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("앱을 시작할 수 없습니다.\n같은 데이터 폴더의 앱이 열려 있는지 확인하세요.\n" +
                    "손상된 notes.json은 덮어쓰지 않습니다. 앱을 종료한 뒤 파일과 .bak을 복사해 보관하세요.\n\n" + error.Message,
                    "Sticky Memo PoC", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
