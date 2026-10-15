using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.Web.Services;

public static class DevelopmentLocalDb
{
    public static string Resolve(string connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        const string prefix = @"(localdb)\";
        if (!connection.DataSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return connectionString;
        var instance = connection.DataSource[prefix.Length..];
        if (instance.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || instance is "." or "..") return connectionString;

        // The LocalDB launcher can fail under Visual Studio even while SQL is running.
        // Read the current instance's own log and verify its pipe before using it.
        var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Microsoft SQL Server Local DB", "Instances", instance, "error.log");
        string? logText = null;
        try
        {
            using var file = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file);
            logText = reader.ReadToEnd();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (TryPipe(logText, connection, out var resolved)) return resolved;
        if (TryPipe(Run("info", instance), connection, out resolved)) return resolved;
        Run("start", instance);
        if (TryPipe(Run("info", instance), connection, out resolved)) return resolved;
        // Let SqlClient report the actual database error; helper failure must not crash startup.
        return connectionString;
    }
    private static bool TryPipe(string? text, SqlConnectionStringBuilder original, out string resolved)
    {
        resolved = original.ConnectionString;
        if (text is null) return false;
        var matches = Regex.Matches(text, @"\\\\\.\\pipe\\LOCALDB#[A-Za-z0-9]+\\tsql\\query", RegexOptions.IgnoreCase);
        foreach (Match match in matches.Reverse())
        {
            var candidate = new SqlConnectionStringBuilder(original.ConnectionString) { DataSource="np:"+match.Value, ConnectTimeout=3 };
            try
            {
                using var connection = new SqlConnection(candidate.ConnectionString);
                connection.Open();
                candidate.ConnectTimeout=original.ConnectTimeout;
                resolved=candidate.ConnectionString;
                return true;
            }
            catch (SqlException) { }
        }
        return false;
    }
    private static string? Run(string action, string instance)
    {
        try
        {
            var start = new ProcessStartInfo("sqllocaldb") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
            start.ArgumentList.Add(action); start.ArgumentList.Add(instance);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { process.Kill(); return null; }
            Task.WaitAll(output,error);
            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (Win32Exception) { return null; }
    }
}
