using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CodexQuotaPet
{
    internal static class CodexLocator
    {
        public static string FindExecutable(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return Path.GetFullPath(configuredPath);

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string managedRoot = Path.Combine(local, "OpenAI", "Codex", "bin");
            if (Directory.Exists(managedRoot))
            {
                try
                {
                    FileInfo best = Directory.GetFiles(managedRoot, "codex.exe", SearchOption.AllDirectories)
                        .Select(path => new FileInfo(path))
                        .OrderByDescending(file => file.LastWriteTimeUtc)
                        .FirstOrDefault();
                    if (best != null) return best.FullName;
                }
                catch { }
            }

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string part in pathValue.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(part.Trim().Trim('"'), "codex.exe");
                    if (File.Exists(candidate) && candidate.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0)
                        return candidate;
                }
                catch { }
            }

            try
            {
                foreach (Process process in Process.GetProcessesByName("codex"))
                {
                    try
                    {
                        string candidate = process.MainModule.FileName;
                        if (File.Exists(candidate) && candidate.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0)
                            return candidate;
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            catch { }

            return null;
        }

        public static string FindCodexHome(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath))
                return Path.GetFullPath(configuredPath);
            string fromEnvironment = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
                return Path.GetFullPath(fromEnvironment);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }
    }
}
