using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Controllers;

internal static class AreaHttpTests
{
    internal static async Task Run(bool openingHoursOnly = false)
    {
        var baseConnection = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set RM_CONNECTION_STRING to the test SQL Server instance.");
        var database = "RestaurantManagement_HttpTest_" + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = database };
        var connection = builder.ConnectionString;
        Process? web = null;
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        try
        {
            await DatabaseTool.Migrate(connection);
            await DatabaseTool.Seed(connection, "TestOnly9!" + Guid.NewGuid().ToString("N"));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.Combine(DatabaseTool.Root, "src", "RestaurantManagement.Web"),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(typeof(AreasController).Assembly.Location);
            start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
            start.Environment["ConnectionStrings__DefaultConnection"] = connection;
            start.Environment["RM_CONNECTION_STRING"] = connection;
            start.Environment["OpeningHours__ActorUserId"] = "1";
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["AreaManagement__ActorUserId"] = "1";
            start.Environment["EmailVerification__Enabled"] = "false";
            web = new Process { StartInfo = start };
            web.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
            web.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
            web.Start(); web.BeginOutputReadLine(); web.BeginErrorReadLine();
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
                { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(15) };
            var ready = false;
            for (var i=0;i<60;i++)
            {
                if (web.HasExited) break;
                try { if ((await client.GetAsync("/Areas")).IsSuccessStatusCode) { ready = true; break; } }
                catch (HttpRequestException) { }
                await Task.Delay(250);
            }
            if (!ready) throw new Exception("Web did not start: " + string.Join(Environment.NewLine, output.TakeLast(25)));
            if (openingHoursOnly)
            {
                await OpeningHoursTests.RunHttp(client, connection);
                await SpecialHolidayTests.Run(client, connection);
                await BookingScheduleTests.Run(client, connection);
                await VietnamTimeTests.Run(client, connection);
                await ManagementFlowTests.Run(client, connection);
                return;
            }
            async Task<string> Get(string path)
            {
                using var response = await client.GetAsync(path);
                response.EnsureSuccessStatusCode();
                return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            }
            async Task<HttpResponseMessage> Post(string formPath, string action, Dictionary<string,string> values)
            {
                var html = await Get(formPath);
                var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
                if (token.Length==0) throw new Exception("Missing anti-forgery token: " + formPath);
                values["__RequestVerificationToken"] = token;
                return await client.PostAsync(action, new FormUrlEncodedContent(values));
            }
            async Task<int> Scalar(string sql)
            {
                await using var cn = new SqlConnection(connection); await cn.OpenAsync();
                await using var command = new SqlCommand(sql, cn);
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }
            void Check(bool ok,string label)
            {
                if (!ok) throw new Exception("FAIL HTTP: " + label);
                Console.WriteLine("PASS HTTP: " + label);
            }
            Dictionary<string,string> Edit(string name,string order,string notes="") => new()
                { ["Id"]="1", ["Name"]=name, ["SortOrder"]=order, ["Notes"]=notes };
            var html = await Get("/Areas/Edit/1");
            Check(html.Contains("Tầng một") && html.Contains("name=\"Notes\""), "Edit form contains current values and notes");
            var before = await Scalar("SELECT COUNT(*) FROM dbo.Areas");
            using (var result = await Post("/Areas/Edit/1", "/Areas/Edit", Edit("Tầng một","1","Gần cửa sổ")))
                Check(result.StatusCode==HttpStatusCode.Redirect, "Save notes with same name");
            html = await Get("/Areas");
            Check(html.Contains("Gần cửa sổ") && html.Contains("Cập nhật khu vực thành công"), "List refresh shows saved notes and success");
            using (var result = await Post("/Areas/Edit/1", "/Areas/Edit", Edit("Tầng một","8","Gần cửa sổ")))
                Check(result.StatusCode==HttpStatusCode.Redirect && await Scalar("SELECT SortOrder FROM dbo.Areas WHERE Id=1")==8, "Save sort order");
            html = await Get("/Areas");
            Check(html.IndexOf("data-area-id=\"1\"")>html.IndexOf("data-area-id=\"3\""), "List uses new sort order");
            using (var result = await Post("/Areas/Edit/1", "/Areas/Edit", Edit("  Tầng   1 ","8","Gần cửa sổ")))
                Check(result.StatusCode==HttpStatusCode.Redirect && await Scalar("SELECT COUNT(*) FROM dbo.Areas WHERE Id=1 AND Name=N'Tầng 1'")==1, "Rename normalizes and retains ID");
            Check(await Scalar("SELECT COUNT(*) FROM dbo.Areas")==before, "Editing does not create another area");
            foreach (var name in new[] { "Tầng hai", "  TẦNG   HAI  ", "Tầng\thai" })
            {
                using var result = await Post("/Areas/Edit/1", "/Areas/Edit", Edit(name,"9","Giữ nội dung"));
                html = WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync());
                Check(result.StatusCode==HttpStatusCode.OK && html.Contains("Tên khu vực đã tồn tại") && html.Contains("Giữ nội dung"), "Reject duplicate edit: " + name);
            }
            using (var result = await Post("/Areas/Create", "/Areas/Create", new() { ["Name"]=" TẦNG   HAI ", ["SortOrder"]="0" }))
                Check(WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync()).Contains("Tên khu vực đã tồn tại"), "Create and edit share normalized uniqueness");
            using (var result = await Post("/Areas/Edit/1", "/Areas/Edit", Edit("Tầng 1","-1")))
                Check(result.StatusCode==HttpStatusCode.OK && await Scalar("SELECT SortOrder FROM dbo.Areas WHERE Id=1")==8, "Invalid order does not update");
            Check((await client.GetAsync("/Areas/Edit/2147483647")).StatusCode==HttpStatusCode.NotFound, "Missing edit returns 404");
            using (var result = await client.PostAsync("/Areas/Deactivate/1", new FormUrlEncodedContent(new Dictionary<string,string>())))
                Check(result.StatusCode==HttpStatusCode.BadRequest, "State changes require anti-forgery token");
            html = await Get("/Areas/Deactivate/1");
            Check(html.Contains("Xác nhận ngừng sử dụng") && html.Contains("Hủy"), "Deactivation confirmation shown");
            var cancelPath = Regex.Match(html,"href=\"([^\"]+)\"[^>]*>Hủy</a>").Groups[1].Value;
            Check(cancelPath.Length>0, "Cancel is a navigation link");
            await Get(cancelPath);
            Check(await Scalar("SELECT CONVERT(int,IsActive) FROM dbo.Areas WHERE Id=1")==1, "Cancel leaves status unchanged");
            using (var result = await Post("/Areas", "/Areas/Delete/1", new()))
                Check(result.StatusCode==HttpStatusCode.Redirect, "Delete request handled");
            html = await Get("/Areas");
            Check(html.Contains("không thể xóa") && html.Contains("ngừng sử dụng") && await Scalar("SELECT COUNT(*) FROM dbo.Areas WHERE Id=1")==1, "Area with tables cannot be deleted; message suggests deactivation");
            html = await Get("/Reservations/Create");
            Check(Regex.IsMatch(html,"<option[^>]*value=\"1\""), "Active area available for booking");
            var bookingTime = DateTime.UtcNow.AddHours(7).Date.AddDays(25).AddHours(10).ToString("yyyy-MM-ddTHH:mm");
            Dictionary<string,string> Booking(string phone) => new()
                { ["CustomerName"]="HTTP Test", ["Phone"]=phone, ["GuestCount"]="2", ["PreferredAreaId"]="1", ["StartsAt"]=bookingTime };
            using (var result = await Post("/Reservations/Create", "/Reservations/Create", Booking("0985555555")))
                Check(result.StatusCode==HttpStatusCode.Redirect, "Booking active area succeeds");
            var bookingId=await Scalar("SELECT CONVERT(int,MAX(Id)) FROM dbo.Reservations");
            var staleForm = await Get("/Reservations/Create");
            var staleToken = Regex.Match(staleForm, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var countBeforeDeactivation = await Scalar("SELECT COUNT(*) FROM dbo.Reservations");
            var pendingBeforeDeactivation = await Scalar("SELECT COUNT(*) FROM dbo.Reservations WHERE Status='Pending'");
            using (var result = await Post("/Areas/Deactivate/1", "/Areas/Deactivate/1", new()))
                Check(result.StatusCode==HttpStatusCode.Redirect, "Confirm deactivation");
            html = await Get("/Areas");
            var row = Regex.Match(html,"<section[^>]*data-area-id=\"1\"[^>]*>([\\s\\S]*?)</section>").Value; // Khu vực & bàn: mỗi khu vực là một thẻ
            Check(row.Contains("Ngừng sử dụng") && !row.Contains("href=\"/Areas/Deactivate/1\""), "Inactive area remains visible with status");
            Check(await Scalar("SELECT COUNT(*) FROM dbo.DiningTables WHERE AreaId=1")==10, "Deactivation preserves tables");
            html=await Get("/Reservations/Create");
            Check(!Regex.IsMatch(html,"<option[^>]*value=\"1\""), "Inactive area absent from new booking dropdown");
            Check(await Scalar("SELECT COUNT(*) FROM dbo.Reservations")==countBeforeDeactivation
                && await Scalar("SELECT COUNT(*) FROM dbo.Reservations WHERE Status='Pending'")==pendingBeforeDeactivation,
                "Deactivation neither deletes nor cancels existing reservations");
            var staleBooking = Booking("0987777777");
            staleBooking["__RequestVerificationToken"] = staleToken;
            using (var result = await client.PostAsync("/Reservations/Create", new FormUrlEncodedContent(staleBooking)))
            {
                var returnedForm = WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync());
                Check(result.StatusCode==HttpStatusCode.OK && returnedForm.Contains("không còn hoạt động")
                    && !Regex.IsMatch(returnedForm,"<option[^>]*value=\"1\""),
                    "Form opened before deactivation is rejected and dropdown refreshed");
            }
            Check(await Scalar("SELECT COUNT(*) FROM dbo.Reservations")==countBeforeDeactivation, "Stale form creates no reservation");
            var bookingCount=await Scalar("SELECT COUNT(*) FROM dbo.Reservations");
            using (var result = await Post("/Reservations/Create", "/Reservations/Create", Booking("0986666666")))
                Check(WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync()).Contains("không còn hoạt động"), "Forged inactive area selection rejected");
            Check(await Scalar("SELECT COUNT(*) FROM dbo.Reservations")==bookingCount, "Rejected booking creates no record");
            foreach(var id in Enumerable.Range(1,20))
                Check((await Get($"/Reservations/Details/{id}")).Contains("Tầng một"), "Historical booking retains original area name: " + id);
            Check((await Get($"/Reservations/Details/{bookingId}")).Contains("Tầng 1"), "New booking retains name at booking time");
            Check(Regex.Matches(await Get("/Reservations"), "Tầng một").Count==20, "All twenty historical names remain in list");
            Check((await client.GetAsync("/Reservations/Details/2147483647")).StatusCode==HttpStatusCode.NotFound, "Missing reservation returns 404");
            using (var result=await Post("/Areas/Create","/Areas/Create",new() { ["Name"]="HTTP empty",["SortOrder"]="0" }))
                Check(result.StatusCode==HttpStatusCode.Redirect,"Create empty area");
            var empty=await Scalar("SELECT Id FROM dbo.Areas WHERE Name=N'HTTP empty'");
            var emptyAreaBooking = Booking("0984444444");
            emptyAreaBooking["PreferredAreaId"] = empty.ToString();
            using (var result=await Post("/Reservations/Create","/Reservations/Create",emptyAreaBooking))
                Check(result.StatusCode==HttpStatusCode.Redirect, "Active area without tables accepts pending booking");
            Check(await Scalar($"SELECT COUNT(*) FROM dbo.Reservations WHERE PreferredAreaId={empty} AND Status='Pending' AND TableId IS NULL AND AreaNameSnapshot=N'HTTP empty'")==1,
                "Empty-area request is pending, unassigned and preserves area name");
            using (var result=await Post($"/Areas/Deactivate/{empty}",$"/Areas/Deactivate/{empty}",new()))
                Check(result.StatusCode==HttpStatusCode.Redirect && await Scalar($"SELECT CONVERT(int,IsActive) FROM dbo.Areas WHERE Id={empty}")==0,"Deactivate area without tables");
            using (var result=await Post("/Reservations/Create","/Reservations/Create",emptyAreaBooking))
                Check(WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync()).Contains("không còn hoạt động"), "Empty area still rejects bookings after deactivation");
            Check((await Get("/Areas")).Contains("Sử dụng lại"), "Inactive area exposes reactivate action");
            using (var result=await client.PostAsync($"/Areas/Reactivate/{empty}", new FormUrlEncodedContent(new Dictionary<string,string>())))
                Check(result.StatusCode==HttpStatusCode.BadRequest, "Reactivation requires anti-forgery");
            using (var result=await Post("/Areas", $"/Areas/Reactivate/{empty}", new()))
                Check(result.StatusCode==HttpStatusCode.Redirect && await Scalar($"SELECT CONVERT(int,IsActive) FROM dbo.Areas WHERE Id={empty}")==1, "Reactivate existing area");
            Check(Regex.IsMatch(await Get("/Reservations/Create"),$"<option[^>]*value=\"{empty}\""), "Reactivated area returns to booking dropdown");
            Check(await Scalar($"SELECT COUNT(*) FROM dbo.Reservations WHERE PreferredAreaId={empty} AND AreaNameSnapshot=N'HTTP empty'")==1, "Reactivation preserves booking history");
            using (var result=await Post("/Reservations/Create","/Reservations/Create",emptyAreaBooking))
                Check(result.StatusCode==HttpStatusCode.Redirect, "Reactivated area accepts new bookings");
            using (var result=await Post("/Areas", "/Areas/Reactivate/2147483647", new()))
                Check(result.StatusCode==HttpStatusCode.NotFound, "Missing reactivation target returns 404");
            await OpeningHoursTests.RunHttp(client, connection);
            await DatabaseTool.Execute(connection, "UPDATE dbo.Users SET IsActive=0 WHERE Id=1;");
            using (var result=await Post("/Areas", "/Areas/Delete/2", new()))
                Check(result.StatusCode==HttpStatusCode.Redirect, "Denied delete redirects to readable error");
            Check((await Get("/Areas")).Contains("không có quyền quản lý khu vực"), "Permission error shown in management page");
            using (var result=await Post("/Areas/Deactivate/2", "/Areas/Deactivate/2", new()))
                Check(result.StatusCode==HttpStatusCode.Redirect, "Denied deactivation redirects to readable error");
            Check((await Get("/Areas")).Contains("không có quyền quản lý khu vực") && await Scalar("SELECT CONVERT(int,IsActive) FROM dbo.Areas WHERE Id=2")==1, "Denied state change preserves data");
            using (var result=await Post("/Areas", "/Areas/Reactivate/1", new()))
                Check(result.StatusCode==HttpStatusCode.Redirect && await Scalar("SELECT CONVERT(int,IsActive) FROM dbo.Areas WHERE Id=1")==0, "Unauthorized reactivation cannot change state");
            Console.WriteLine("PASS: all HTTP area lifecycle checks.");
        }
        finally
        {
            if (web is not null) { if (!web.HasExited) { web.Kill(entireProcessTree:true); await web.WaitForExitAsync(); } web.Dispose(); }
            SqlConnection.ClearAllPools();
            builder.InitialCatalog="master";
            await using var master=new SqlConnection(builder.ConnectionString); await master.OpenAsync();
            // Only the random database created by this test can be removed.
            await using var drop=new SqlCommand($"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END",master);
            drop.Parameters.AddWithValue("@name",database); await drop.ExecuteNonQueryAsync();
        }
    }
}




