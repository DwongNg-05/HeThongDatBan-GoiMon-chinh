using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Services;

internal static class ReservationConfirmationTests
{
    static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS confirmation: " + label); }
    internal static async Task Run()
    {
        var connection = Environment.GetEnvironmentVariable("RM_CONNECTION_STRING") ?? throw new Exception("Set RM_CONNECTION_STRING");
        var database = "RestaurantManagement_ConfirmationTest_" + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = database };
        connection = builder.ConnectionString;
        var password = "TestOnly9!" + Guid.NewGuid().ToString("N");
        async Task<object?> Sql(string sql)
        {
            await using var cn = new SqlConnection(connection); await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn); return await cmd.ExecuteScalarAsync();
        }
        try
        {
            await DatabaseTool.Migrate(connection);
            await DatabaseTool.Seed(connection, password);
            await Sql("UPDATE dbo.Users SET MustChangePassword=0; UPDATE dbo.DiningTables SET IsActive=0; INSERT dbo.Areas(Name,SortOrder) VALUES(N'Confirmation test',99); DECLARE @a int=SCOPE_IDENTITY(); INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,SortOrder) VALUES(@a,'T2',1,2,1),(@a,'T4',1,4,2),(@a,'T6',1,6,3);");
            var t2=Convert.ToInt32(await Sql("SELECT Id FROM dbo.DiningTables WHERE Code='T2'"));
            var t4=Convert.ToInt32(await Sql("SELECT Id FROM dbo.DiningTables WHERE Code='T4'"));
            var t6=Convert.ToInt32(await Sql("SELECT Id FROM dbo.DiningTables WHERE Code='T6'"));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"]=connection }).Build();
            var service = new ReservationConfirmationService(config);
            var seq=0;
            async Task<long> Booking(int offset=0, int guests=4, bool email=true)
            {
                var code="T"+(++seq).ToString("D5");
                return Convert.ToInt64(await Sql($"DECLARE @s datetime2(3)=DATEADD(minute,{offset},DATEADD(hour,11,CONVERT(datetime2(3),CONVERT(date,DATEADD(day,3,SYSUTCDATETIME()))))); INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,Email,StartsAt,EndsAt) VALUES('{code}',N'Test guest','0901234567',{guests},{(email?"'guest@example.test'":"NULL")},@s,DATEADD(minute,90,@s)); SELECT CONVERT(bigint,SCOPE_IDENTITY());"));
            }
            async Task<int> Confirm(long id,int table,int actor=2) { try { await service.Confirm(actor,id,table); return 0; } catch(SqlException ex) { return ex.Number; } }
            var id=await Booking();
            Check((await service.Pending(2)).Any(r=>r.Id==id),"waiter sees pending booking");
            var detail=(await service.Details(2,id))!;
            Check(detail.Tables.Select(t=>t.Id).Order().SequenceEqual(new[]{t4,t6}.Order()),"all free sufficiently large tables, including exact capacity; small and inactive excluded");
            Check(await Confirm(id,t2)==51008,"too-small table rejected at confirmation");
            Check(await Confirm(id,t4)==0,"pending confirmed");
            Check(await Confirm(id,t6)==51502,"reconfirmation and table change rejected");
            Check(!(await service.Pending(2)).Any(r=>r.Id==id),"confirmed removed from pending list");
            var overlapping=await Booking(30);
            Check(!(await service.Details(2,overlapping))!.Tables.Any(t=>t.Id==t4),"overlapping reservation excluded");
            foreach(var offset in new[]{-90,90})
            {
                var adjacent=await Booking(offset);
                Check((await service.Details(2,adjacent))!.Tables.Any(t=>t.Id==t4),"adjacent interval allowed: "+offset);
            }
            var many=await Booking(0,20);
            Check((await service.Details(2,many))!.Tables.Count==0,"no adequate table returns empty suggestions");
            var r1=await Booking(240); var r2=await Booking(240);
            var race=await Task.WhenAll(Confirm(r1,t6,1),Confirm(r2,t6,2));
            Check(race.Count(n=>n==0)==1 && race.Count(n=>n==51009)==1,"two employees same table/time: exactly one succeeds");
            var r3=await Booking(480);
            var same=await Task.WhenAll(Confirm(r3,t4,1),Confirm(r3,t6,2));
            Check(same.Count(n=>n==0)==1 && same.Count(n=>n==51502)==1,"same reservation concurrent confirmation: exactly one succeeds");
            var slots=await service.Slots(2,detail.StartsAt.Date);
            Check(slots.Any(s=>s.Id==id && s.TableCode=="T4"),"reserved table visible in correct daily interval");
            Check(await Confirm(await Booking(720),t4,3)==51001,"kitchen cannot confirm");
            var noEmail=await Booking(900,email:false);
            Check(await Confirm(noEmail,t4)==0 && (await service.Details(2,noEmail))!.EmailStatus=="Failed","missing email does not roll back confirmation");
            var sender=new TestSender();
            var dispatcher=new ConfirmationEmailDispatcher(config,sender);
            Check(await dispatcher.DispatchOne(default),"claimed confirmation email");
            Check(sender.Payload?.TableCode=="T4" && SmtpConfirmationEmailSender.Body(sender.Payload).Contains(detail.StartsAt.ToString("dd/MM/yyyy HH:mm")),"notification contains table and local appointment time");
            Check((await service.Details(2,id))!.EmailStatus=="Sent","successful sending recorded");
            sender.Fail=true;
            Check(await dispatcher.DispatchOne(default),"failed delivery processed");
            Check(Convert.ToInt32(await Sql("SELECT COUNT(*) FROM dbo.EmailOutbox o JOIN dbo.Reservations r ON r.Id=o.ReservationId WHERE o.MessageType='BookingConfirmed' AND o.AttemptCount=1 AND o.LastError IS NOT NULL AND r.Status='Confirmed'"))==1,"delivery failure recorded while confirmation remains valid");
            await Sql("UPDATE dbo.EmailOutbox SET NextAttemptAt=DATEADD(day,1,SYSUTCDATETIME()) WHERE Status='Pending' AND LastError IS NULL;");
            for(var i=0;i<3;i++) { await Sql("UPDATE dbo.EmailOutbox SET NextAttemptAt=DATEADD(second,-1,SYSUTCDATETIME()) WHERE Status='Pending' AND LastError IS NOT NULL;"); await dispatcher.DispatchOne(default); }
            Check(Convert.ToInt32(await Sql("SELECT COUNT(*) FROM dbo.EmailOutbox WHERE AttemptCount=4 AND Status='Failed'"))==1,"delivery retries stop after four failures");
            await Http(connection,password,await Booking(1200),t4,Sql);
            await ReservationRejectionTests.Run(service,Sql,t4,id);
            await ReservationTableChangeTests.Run(service,Sql);
            await Smtp();
            await DatabaseTool.SeedConfirmationDemo(connection);
            await DatabaseTool.SeedConfirmationDemo(connection);
            Check(Convert.ToInt32(await Sql("SELECT COUNT(*) FROM dbo.Reservations WHERE Code='CF0001'"))==1,"demo seed is repeatable without duplicate bookings");
        }
        finally
        {
            SqlConnection.ClearAllPools(); builder.InitialCatalog="master";
            await using var cn=new SqlConnection(builder.ConnectionString); await cn.OpenAsync();
            await using var cmd=new SqlCommand($"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END",cn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
    sealed class TestSender : IConfirmationEmailSender
    {
        public bool Fail; public ConfirmationEmailPayload? Payload;
        public Task Send(string recipient,string subject,ConfirmationEmailPayload payload,CancellationToken ct)
        { if(Fail) throw new SmtpException("Simulated delivery failure"); Payload=payload; return Task.CompletedTask; }
    }
    static async Task Http(string connection,string password,long id,int table,Func<string,Task<object?>> sql)
    {
        var listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); var port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start=new ProcessStartInfo("dotnet") { WorkingDirectory=Path.Combine(DatabaseTool.Root,"src","RestaurantManagement.Web"),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add(typeof(ReservationConfirmationService).Assembly.Location); start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["RM_CONNECTION_STRING"]=connection; start.Environment["ASPNETCORE_ENVIRONMENT"]="Development"; start.Environment["ConfirmationEmail__Enabled"]="false";
        using var web=new Process { StartInfo=start }; var logs=new System.Collections.Concurrent.ConcurrentQueue<string>();
        web.OutputDataReceived+=(_,e)=>{if(e.Data!=null)logs.Enqueue(e.Data);}; web.ErrorDataReceived+=(_,e)=>{if(e.Data!=null)logs.Enqueue(e.Data);};
        web.Start(); web.BeginOutputReadLine(); web.BeginErrorReadLine();
        try
        {
            using var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(15) };
            var ready=false;
            for(var i=0;i<80 && !web.HasExited;i++) { try { if((await client.GetAsync("/Account/Login")).IsSuccessStatusCode) {ready=true;break;} } catch(HttpRequestException) {} await Task.Delay(250); }
            if(!ready) throw new Exception(string.Join('\n',logs.TakeLast(15)));
            Check((await client.GetAsync("/ReservationConfirmations")).StatusCode==HttpStatusCode.Redirect,"anonymous list access requires login");
            async Task<string> Get(string path)=>WebUtility.HtmlDecode(await client.GetStringAsync(path));
            async Task<HttpResponseMessage> Post(string form,string action,Dictionary<string,string> fields)
            {
                var html=await Get(form); fields["__RequestVerificationToken"]=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
                return await client.PostAsync(action,new FormUrlEncodedContent(fields));
            }
            var login=await Post("/Account/Login","/Account/Login",new() { ["Identifier"]="waiter",["Password"]=password });
            Check(login.StatusCode==HttpStatusCode.Redirect,"waiter login succeeds");
            Check((await Get("/ReservationConfirmations")).Contains("Xem và chọn bàn"),"pending page renders");
            var path=$"/ReservationConfirmations/Details/{id}";
            Check((await Get(path)).Contains("Bàn T4"),"suggestion screen renders real table");
            Check((await client.PostAsync($"/ReservationConfirmations/Confirm/{id}",new FormUrlEncodedContent(new Dictionary<string,string> { ["selectedTableId"]=table.ToString() }))).StatusCode==HttpStatusCode.BadRequest,"confirmation requires CSRF token");
            var missing=await Post(path,$"/ReservationConfirmations/Confirm/{id}",new());
            Check(WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()).Contains("Vui lòng chọn một bàn"),"missing selection shows error");
            var confirm=await Post(path,$"/ReservationConfirmations/Confirm/{id}",new() { ["selectedTableId"]=table.ToString() });
            Check(confirm.StatusCode==HttpStatusCode.Redirect,"HTTP confirmation redirects after success");
            Check((await Get(path)).Contains("Đã xác nhận"),"updated confirmed status shown");
            Check((await Get("/ReservationConfirmations/Schedule?day="+DateTime.UtcNow.AddDays(4).ToString("yyyy-MM-dd"))).Contains("Lịch bàn đã đặt trước"),"schedule page renders");
            var repeated=await Post(path,$"/ReservationConfirmations/Confirm/{id}",new() { ["selectedTableId"]=table.ToString() });
            Check(WebUtility.HtmlDecode(await repeated.Content.ReadAsStringAsync()).Contains("Không thể xác nhận lại"),"HTTP reconfirm shows clear error");
            var staleId=Convert.ToInt64(await sql($"INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,StartsAt,EndsAt) SELECT 'STALE1',N'Stale test','0901234567',4,StartsAt,EndsAt FROM dbo.Reservations WHERE Id={id}; SELECT CONVERT(bigint,SCOPE_IDENTITY());"));
            var stalePath=$"/ReservationConfirmations/Details/{staleId}";
            var stale=await Post(stalePath,$"/ReservationConfirmations/Confirm/{staleId}",new() { ["selectedTableId"]=table.ToString() });
            var staleHtml=WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync());
            Check(staleHtml.Contains("Bàn vừa được giữ") && !staleHtml.Contains($"id=\"table-{table}\""),"conflict shows clear error and refreshed suggestions without occupied table");
            await sql($"UPDATE dbo.Reservations SET GuestCount=20 WHERE Id={staleId}");
            Check((await Get(stalePath)).Contains("Không có bàn trống đủ chỗ"),"empty suggestions screen explains no suitable tables");
            await ReservationRejectionTests.Http(client,sql);
            await ReservationTableChangeTests.Http(client,sql,id,table);
        }
        finally { if(!web.HasExited) {web.Kill(true);await web.WaitForExitAsync();} }
    }
    static async Task Smtp()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
        try
        {
            var port=((IPEndPoint)listener.LocalEndpoint).Port;
            var receive=Task.Run(async () =>
            {
                using var socket=await listener.AcceptTcpClientAsync(timeout.Token);
                using var stream=socket.GetStream(); using var reader=new StreamReader(stream);
                using var writer=new StreamWriter(stream) { AutoFlush=true,NewLine="\r\n" };
                await writer.WriteLineAsync("220 localhost test");
                var recipient=false; var body=false;
                while(await reader.ReadLineAsync(timeout.Token) is string line)
                {
                    if(line.StartsWith("RCPT TO:",StringComparison.OrdinalIgnoreCase)) recipient=line.Contains("guest@example.test");
                    if(line=="DATA")
                    {
                        await writer.WriteLineAsync("354 Send message");
                        while(await reader.ReadLineAsync(timeout.Token) is string data && data!=".") body |= data.Length>0;
                        await writer.WriteLineAsync("250 Accepted");
                    }
                    else if(line=="QUIT") {await writer.WriteLineAsync("221 Bye");break;}
                    else await writer.WriteLineAsync("250 OK");
                }
                return recipient && body;
            });
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["ConfirmationEmail:Host"]="127.0.0.1",["ConfirmationEmail:Port"]=port.ToString(),
                ["ConfirmationEmail:From"]="restaurant@example.test",["ConfirmationEmail:EnableSsl"]="false" }).Build();
            await new SmtpConfirmationEmailSender(config).Send("guest@example.test","Confirmation",new("ABC123","Demo",4,DateTime.UtcNow,DateTime.UtcNow.AddMinutes(90),"5"),timeout.Token);
            Check(await receive,"SMTP adapter delivers message to local test server without external email");
        }
        finally { listener.Stop(); }
    }
}
