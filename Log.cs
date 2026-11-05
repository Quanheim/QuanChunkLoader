using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;

namespace QuanChunkLoader
{
    /// <summary>
    /// The mod's log (docs/design.md, "Logging"). Lines go to the BepInEx log, with a local timestamp and UTC offset
    /// so they can be matched against players' Player.log files, and optionally to a daily file.
    /// </summary>
    internal static class Log
    {
        internal static ManualLogSource Source;

        private static string _fileDay;
        private static StreamWriter _file;

        private static string Folder => Path.Combine(Paths.BepInExRootPath, "QuanChunkLoader", "logs");

        internal static bool DebugOn => Settings.Detail.Value >= LogDetail.Debug;
        internal static bool TraceOn => Settings.Detail.Value >= LogDetail.Trace;

        internal static void Info(string message) => Write(LogLevel.Info, message);
        internal static void Warning(string message) => Write(LogLevel.Warning, message);
        internal static void Error(string message) => Write(LogLevel.Error, message);

        internal static void Debug(string message)
        {
            if (DebugOn)
            {
                Write(LogLevel.Info, "[debug] " + message);
            }
        }

        internal static void Trace(string message)
        {
            if (TraceOn)
            {
                Write(LogLevel.Info, "[trace] " + message);
            }
        }

        private static void Write(LogLevel level, string message)
        {
            var now = DateTime.Now;
            var line = now.ToString("HH:mm:ss zzz") + " " + message;
            Source.Log(level, line);
            if (Settings.DailyFile != null && Settings.DailyFile.Value)
            {
                WriteFile(now, level, line);
            }
        }

        private static void WriteFile(DateTime now, LogLevel level, string line)
        {
            try
            {
                var day = now.ToString("yyyy-MM-dd");
                if (_file == null || day != _fileDay)
                {
                    _file?.Dispose();
                    Directory.CreateDirectory(Folder);
                    _file = new StreamWriter(Path.Combine(Folder, $"QuanChunkLoader-{day}.log"), true);
                    _fileDay = day;
                    PruneOldFiles(now);
                }
                _file.WriteLine(now.ToString("yyyy-MM-dd ") + (level == LogLevel.Info ? "" : $"[{level}] ") + line);
                if (level != LogLevel.Info)
                {
                    _file.Flush(); // warnings and errors reach the disk at once
                }
            }
            catch (Exception e)
            {
                // A log file that can't be written mustn't stop the mod; say so once and stop trying.
                Settings.DailyFile.Value = false;
                Source.LogWarning($"Couldn't write the daily log file, so it's switched off: {e.Message}");
            }
        }

        /// <summary>
        /// Writes buffered lines to the daily file. Called about once a second by the server loop, so a burst of
        /// Debug or Trace lines isn't a disk write per line.
        /// </summary>
        internal static void Flush()
        {
            try
            {
                _file?.Flush();
            }
            catch (Exception)
            {
                // Reported by the next write, which switches the file off.
            }
        }

        private static void PruneOldFiles(DateTime now)
        {
            var cutoff = now.Date.AddDays(-Settings.DaysToKeep.Value);
            foreach (var path in Directory.GetFiles(Folder, "QuanChunkLoader-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(path).Substring("QuanChunkLoader-".Length);
                if (DateTime.TryParse(name, out var date) && date < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
    }
}
