using System;
using System.IO;
using System.Text;

namespace HIS.Client.UrsaShell.Services;

public static class ClientDiagnosticsLog
{
    private static readonly object SyncRoot = new();
    private static readonly string LogFilePath = ResolveLogFilePath();

    public static string FilePath => LogFilePath;

    public static void Info(string category, string message, Exception? exception = null)
        => Write("INFO", category, message, exception);

    public static void Warn(string category, string message, Exception? exception = null)
        => Write("WARN", category, message, exception);

    public static void Error(string category, string message, Exception? exception = null)
        => Write("ERROR", category, message, exception);

    private static string ResolveLogFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        var directory = Path.Combine(localAppData, "GHIS", "Logs");
        try
        {
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "client.log");
        }
        catch
        {
            return Path.Combine(AppContext.BaseDirectory, "client.log");
        }
    }

    private static void Write(string level, string category, string message, Exception? exception)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var text = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(" [").Append(level).Append(']')
                .Append(" [PID:").Append(Environment.ProcessId)
                .Append(" T:").Append(Environment.CurrentManagedThreadId).Append(']')
                .Append(" [").Append(category).Append("] ")
                .Append(message)
                .AppendLine();

            if (exception != null)
            {
                text.Append("EXCEPTION: ").Append(exception).AppendLine();
            }

            lock (SyncRoot)
            {
                File.AppendAllText(LogFilePath, text.ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never take down the client.
        }
    }
}
