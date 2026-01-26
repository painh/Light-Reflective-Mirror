using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LightReflectiveMirror
{
    partial class Program
    {
        public static string GitCommitTime { get; private set; } = "Unknown";

        public List<Room> GetRooms() => _relay.rooms;
        public int GetConnections() => _currentConnections.Count;
        public TimeSpan GetUptime() => DateTime.Now - _startupTime;
        public int GetPublicRoomCount() => _relay.rooms.Where(x => x.isPublic).Count();

        public static void WriteLogMessage(string message, ConsoleColor color = ConsoleColor.White, bool oneLine = false)
        {
            Console.ForegroundColor = color;
            if (oneLine)
                Console.Write(message);
            else
                Console.WriteLine(message);

            // 콘솔 출력과 함께 로그 저장소에도 저장
            LogLevel level = color switch
            {
                ConsoleColor.Red or ConsoleColor.DarkRed => LogLevel.Error,
                ConsoleColor.Yellow or ConsoleColor.DarkYellow => LogLevel.Warning,
                _ => LogLevel.Info
            };
            LogStore.Instance.Add(message.Trim(), level);
        }

        private static void GetPublicIP()
        {
            try
            {
                // easier to just ping an outside source to get our public ip
                publicIP = webClient.DownloadString("https://api.ipify.org/").Replace("\\r", "").Replace("\\n", "").Trim();
            }
            catch
            {
                WriteLogMessage("Failed to reach public IP endpoint! Using loopback address.", ConsoleColor.Yellow);
                publicIP = "127.0.0.1";
            }
        }

        private static void LoadGitCommitTime()
        {
            // 1. Docker 빌드 시 생성된 파일에서 먼저 읽기 시도
            try
            {
                var buildTimeFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "build_time.txt");
                if (File.Exists(buildTimeFile))
                {
                    string content = File.ReadAllText(buildTimeFile).Trim();
                    if (!string.IsNullOrEmpty(content) && content != "Unknown")
                    {
                        GitCommitTime = content;
                        return;
                    }
                }
            }
            catch { }

            // 2. git 명령어로 직접 읽기 (로컬 개발 환경)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "log -1 --format=%ci",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output) && DateTime.TryParse(output, out DateTime commitTime))
                    {
                        GitCommitTime = commitTime.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
            }
            catch
            {
                GitCommitTime = "Unknown";
            }
        }

        void WriteTitle()
        {
            LoadGitCommitTime();

            string t = $@"
                           w  c(..)o   (
  _       _____   __  __    \__(-)    __)
 | |     |  __ \ |  \/  |       /\   (
 | |     | |__) || \  / |      /(_)___)
 | |     |  _  / | |\/| |      w /|
 | |____ | | \ \ | |  | |       | \
 |______||_|  \_\|_|  |_|      m  m copyright monkesoft 2021

 Build: {GitCommitTime}
";

            string load = $"Chimp Event Listener Initializing... OK" +
                            "\nHarambe Memorial Initializing...     OK" +
                            "\nBananas Initializing...              OK\n";

            WriteLogMessage(t, ConsoleColor.Green);
            WriteLogMessage(load, ConsoleColor.Cyan);
        }
    }
}
