using System;
using System.IO;

namespace CodexQuotaPet
{
    internal static class DiagnosticLog
    {
        private static readonly object Sync = new object();

        public static void Write(string message)
        {
            try
            {
                string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(directory);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + CodexAppServerClient.Sanitize(message) + Environment.NewLine;
                lock (Sync) File.AppendAllText(Path.Combine(directory, "codex-quota-pet.log"), line);
            }
            catch { }
        }
    }
}
