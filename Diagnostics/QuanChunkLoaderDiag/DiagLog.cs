using System;
using System.IO;
using System.Text;
using BepInEx;

namespace QuanChunkLoaderDiag
{
    /// <summary>One file per local day under BepInEx/QuanChunkLoaderDiag/, capped per day so it can't fill a disk.</summary>
    internal static class DiagLog
    {
        private const long MaxBytesPerDay = 20L * 1024 * 1024;

        private static readonly string Dir = Path.Combine(Paths.BepInExRootPath, "QuanChunkLoaderDiag", "v3");
        private static string _currentDay;
        private static long _bytesToday;
        private static bool _capped;

        public static void Write(string category, string message)
        {
            try
            {
                var now = DateTime.Now;
                var day = now.ToString("yyyy-MM-dd");
                if (day != _currentDay)
                {
                    Directory.CreateDirectory(Dir);
                    _currentDay = day;
                    var path = PathFor(day);
                    _bytesToday = File.Exists(path) ? new FileInfo(path).Length : 0;
                    _capped = false;
                }
                if (_capped)
                {
                    return;
                }

                var line = $"{now:yyyy-MM-dd HH:mm:ss zzz} [{category}] {message}{Environment.NewLine}";
                if (_bytesToday + line.Length > MaxBytesPerDay)
                {
                    _capped = true;
                    line = $"{now:yyyy-MM-dd HH:mm:ss zzz} [LOG] Daily cap of {MaxBytesPerDay / (1024 * 1024)} MB reached; nothing more is logged today.{Environment.NewLine}";
                }
                File.AppendAllText(PathFor(day), line, Encoding.UTF8);
                _bytesToday += line.Length;
            }
            catch
            {
                // Logging must never take the server down.
            }
        }

        private static string PathFor(string day) => Path.Combine(Dir, $"diag-{day}.log");
    }
}
