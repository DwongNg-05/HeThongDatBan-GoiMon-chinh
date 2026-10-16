using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Services;

internal static class ReportLiveHttpTests
{
    internal static async Task RunAsync()
    {
        var original = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING") ?? throw new InvalidOperationException("Set RM_CONNECTION_STRING for disposable SQL/browser tests.");
        var name = "RestaurantManagement_Test_" + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder(original) { InitialCatalog = name };
        var connection = builder.ConnectionString;
        var password = "ReportTest9!" + Guid.NewGuid().ToString("N");
        Process? web = null;
        try
        {
            await DatabaseTool.Migrate(connection);
            // EXECUTE AS OWNER triggers need a resolvable owner on renamed Windows hosts.
            // This changes only the exact disposable database created above.
            await DatabaseTool.Execute(connection, """
                DECLARE @owner sysname=(SELECT name FROM sys.server_principals WHERE sid=0x01);
                IF @owner IS NOT NULL
                BEGIN
                    DECLARE @sql nvarchar(max)=N'ALTER AUTHORIZATION ON DATABASE::'+QUOTENAME(DB_NAME())+N' TO '+QUOTENAME(@owner);
                    EXEC sys.sp_executesql @sql;
                END;
                """);
            await DatabaseTool.Seed(connection, password);
            await DatabaseTool.Execute(connection, """
                UPDATE dbo.Users SET MustChangePassword=0;
                UPDATE dbo.OpeningHours SET IsClosed=0,OpensAt='00:00',ClosesAt='23:59:59';
                EXEC dbo.usp_OpenShift @Name=N'Report live test',@OpeningCash=0,@ActorUserId=4;
                """);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var baseUrl = $"http://127.0.0.1:{port}";
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.Combine(DatabaseTool.Root, "src", "RestaurantManagement.Web"),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(typeof(DailyOverviewStore).Assembly.Location);
            start.ArgumentList.Add("--urls"); start.ArgumentList.Add(baseUrl);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["RM_CONNECTION_STRING"] = connection;
            start.Environment["Email__RetryPollSeconds"] = "0";
            start.Environment["EmailVerification__Enabled"] = "false";
            web = new Process { StartInfo = start };
            var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
            web.OutputDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
            web.ErrorDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
            web.Start(); web.BeginOutputReadLine(); web.BeginErrorReadLine();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var ready = false;
            for (var i = 0; i < 50 && !web.HasExited; i++)
            {
                try { using var response = await http.GetAsync(baseUrl + "/Account/Login"); if (response.IsSuccessStatusCode) { ready = true; break; } }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(200);
            }
            if (!ready) throw new Exception("Report web did not start: " + string.Join(Environment.NewLine, logs.TakeLast(12)));
            var nodeStart = new ProcessStartInfo("node")
            {
                WorkingDirectory = DatabaseTool.Root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            nodeStart.ArgumentList.Add("tests/report-live.browser.cjs");
            nodeStart.Environment["RM_REPORT_TEST_URL"] = baseUrl;
            nodeStart.Environment["RM_REPORT_TEST_DB"] = name;
            nodeStart.Environment["RM_REPORT_TEST_SERVER"] = builder.DataSource;
            nodeStart.Environment["RM_REPORT_TEST_PASSWORD"] = password;
            using var node = Process.Start(nodeStart) ?? throw new Exception("Cannot start browser tests.");
            node.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
            node.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
            node.BeginOutputReadLine(); node.BeginErrorReadLine();
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try { await node.WaitForExitAsync(limit.Token); }
            catch (OperationCanceledException) { node.Kill(entireProcessTree: true); throw; }
            if (node.ExitCode != 0) throw new Exception("Live report browser checks failed.");
        }
        finally
        {
            if (web is { HasExited: false }) { web.Kill(entireProcessTree: true); await web.WaitForExitAsync(); }
            web?.Dispose();
            SqlConnection.ClearAllPools();
            // Only the exact freshly generated database is ever removed.
            builder.InitialCatalog = "master";
            await using var master = new SqlConnection(builder.ConnectionString);
            await master.OpenAsync();
            await using var drop = new SqlCommand($"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", master);
            drop.Parameters.AddWithValue("@name", name);
            await drop.ExecuteNonQueryAsync();
            Console.WriteLine("Disposable report database removed: " + name);
        }
    }
}
