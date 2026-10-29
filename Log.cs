using System.IO;
using System.Runtime.CompilerServices;

namespace MusicIsland
{
    /// <summary>
    /// Plain-text log at %AppData%\MusicIsland\log.txt, rotated at 1 MB.
    /// </summary>
    public static class Log
    {
        const long MaxSize = 1024 * 1024;

        static readonly object sync = new();

        public static string FilePath => Path.Combine(AppSettings.Folder, "log.txt");

        public static void Info(string message, [CallerMemberName] string caller = "") =>
            Write("INFO ", caller, message);

        public static void Error(string message, Exception? ex = null, [CallerMemberName] string caller = "") =>
            Write("ERROR", caller, ex == null ? message : message + ": " + ex);

        static void Write(string level, string caller, string message)
        {
            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(AppSettings.Folder);

                    var file = new FileInfo(FilePath);

                    if (file.Exists && file.Length > MaxSize)
                        File.Move(FilePath, Path.Combine(AppSettings.Folder, "log.old.txt"), true);

                    File.AppendAllText(FilePath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} [{caller}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never take the app down
            }
        }
    }
}
