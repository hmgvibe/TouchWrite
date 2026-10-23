using System;
using System.IO;

namespace TouchWrite
{
    /// <summary>Small append-only log at %APPDATA%\TouchWrite\log.txt, for diagnosing problems after the fact.</summary>
    internal static class Log
    {
        private static readonly object Gate = new();
        private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TouchWrite");
        public static readonly string FilePath = Path.Combine(Dir, "log.txt");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > 512 * 1024)
                    {
                        string old = FilePath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
                }
            }
            catch { }
        }

        public static void Error(string what, Exception ex) => Write($"ERROR {what}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }
}
