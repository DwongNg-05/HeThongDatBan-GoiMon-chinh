using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using RestaurantManagement.DbTool;

internal static class NoShowWebTests
{
    internal static async Task Run(string connection, long earlyId, Action<bool,string> check)
    {
        var password = "NoShowTest-" + Guid.NewGuid().ToString("N");
        await using (var cn = new SqlConnection(connection))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("UPDATE dbo.Users SET PasswordHash=@hash,MustChangePassword=0 WHERE Id=1;",cn);
            cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password));
            await cmd.ExecuteNonQueryAsync();
        }
        var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start = new ProcessStartInfo("dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            WorkingDirectory=Path.Combine(DatabaseTool.Root,"src","RestaurantManagement.Web") };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"RestaurantManagement.Web.dll"));
        start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["RM_CONNECTION_STRING"] = connection;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["Email__RetryPollSeconds"] = "0";
        start.Environment["EmailVerification__Enabled"] = "false";
        // Never send mail from this isolated verification app.
        start.Environment["Smtp__Username"] = "";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress=new Uri($"http://127.0.0.1:{port}") };
            var ready = false;
            for(var i=0;i<60;i++)
            {
                try { using var r = await client.GetAsync("/Account/Login"); if(r.IsSuccessStatusCode) {ready=true;break;} }
                catch(HttpRequestException) { }
                await Task.Delay(250);
            }
            check(ready,"Test web starts");
            using var anonymous = await client.GetAsync("/NoShow/Alerts");
            check(anonymous.StatusCode == HttpStatusCode.Redirect,"Anonymous users cannot access no-show data");
            await using(var cn = new SqlConnection(connection))
            {
                await cn.OpenAsync();
                await using var cmd = new SqlCommand("UPDATE dbo.Reservations SET StartsAt=DATEADD(second,-870,SYSUTCDATETIME()),EndsAt=DATEADD(minute,75,SYSUTCDATETIME()) WHERE Id=@id;",cn);
                cmd.Parameters.AddWithValue("@id",earlyId); await cmd.ExecuteNonQueryAsync();
            }
            var node = new ProcessStartInfo("node") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=DatabaseTool.Root };
            node.ArgumentList.Add(Path.Combine(DatabaseTool.Root,"tests","no-show-browser.cjs"));
            node.Environment["NOSHOW_TEST_URL"] = client.BaseAddress!.ToString();
            node.Environment["NOSHOW_TEST_PASSWORD"] = password;
            using var browser = Process.Start(node)!;
            var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            try { await browser.WaitForExitAsync(timeout.Token); }
            finally { if(!browser.HasExited)browser.Kill(true); }
            Console.WriteLine(await stdout);
            if(browser.ExitCode!=0) throw new Exception("Browser verification failed: "+await stderr);
            check(true,"Real browser: both screens auto-alert, mark, release, remove warning and phone history");
        }
        finally
        {
            if(!process.HasExited)process.Kill(true);
            await process.WaitForExitAsync(); await Task.WhenAll(output,errors);
        }
    }
}
