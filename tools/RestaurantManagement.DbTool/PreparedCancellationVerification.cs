using System.Data;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

internal static class PreparedCancellationVerification
{
    internal static async Task Run(string baseConnection)
    {
        var builder = new SqlConnectionStringBuilder(baseConnection);
        var name = "RestaurantManagement_S305_Test_" + Guid.NewGuid().ToString("N");
        builder.InitialCatalog = name; var connection = builder.ConnectionString;
        try
        {
            await DatabaseTool.Migrate(connection);
            await DatabaseTool.Execute(connection, $"ALTER AUTHORIZATION ON DATABASE::[{name}] TO sa;");
            const string password = "S305TestOnly9!";
            await DatabaseTool.Seed(connection, password);
            await DatabaseTool.Execute(connection, """
                UPDATE dbo.Users SET MustChangePassword=0,IsActive=1,FailedLoginCount=0,LockedUntil=NULL;
                EXEC dbo.usp_OpenShift @Name=N'S305 charged test',@OpeningCash=0,@ActorUserId=4;
                DECLARE @t int=(SELECT TOP(1) Id FROM dbo.DiningTables WHERE Status='Available' AND IsActive=1 AND MaxCapacity>=2 ORDER BY Id);
                EXEC dbo.usp_OpenSession @TableId=@t,@GuestCount=2,@ActorUserId=2;
                """);
            var session = await Scalar(connection, "SELECT MAX(Id) FROM dbo.DiningSessions");
            var dish = await Scalar(connection, "SELECT TOP(1) Id FROM dbo.MenuItems WHERE IsActive=1 AND IsSoldOut=0 AND IsTemporarilyOut=0 ORDER BY Id");
            await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
            await Login(web.Client, "manager", password);
            using var waiter = new HttpClient(new HttpClientHandler { AllowAutoRedirect=false, CookieContainer=new CookieContainer() }) { BaseAddress=web.Client.BaseAddress };
            using var kitchen = new HttpClient(new HttpClientHandler { AllowAutoRedirect=false, CookieContainer=new CookieContainer() }) { BaseAddress=web.Client.BaseAddress };
            using var cashier = new HttpClient(new HttpClientHandler { AllowAutoRedirect=false, CookieContainer=new CookieContainer() }) { BaseAddress=web.Client.BaseAddress };
            await Login(waiter,"waiter",password); await Login(kitchen,"kitchen",password); await Login(cashier,"cashier",password);
            var managerHtml = await Html(web.Client, "/Ordering/Sent"); var token = Token(managerHtml);
            Assert(managerHtml.Contains("data-can-cancel-prepared=\"true\"") && managerHtml.Contains("name=\"confirmCharged\""),
                "Task 3: manager receives charged cancellation controls");
            Assert((await Html(waiter, "/Ordering/Sent")).Contains("data-can-cancel-prepared=\"false\""),
                "Task 3: waiter receives no prepared cancellation capability");

            async Task<long> Add(string status, long? targetSession=null, int qty=2)
            {
                await DatabaseTool.Execute(connection, $$"""
                    DECLARE @r uniqueidentifier=NEWID();
                    EXEC dbo.usp_SubmitOrder @SessionId={{targetSession??session}},@RequestId=@r,
                     @ItemsJson=N'[{"MenuItemId":{{dish}},"Quantity":{{qty}},"Notes":"S305 charged"}]',@ActorUserId=2;
                    """);
                var id = await Scalar(connection,"SELECT MAX(Id) FROM dbo.OrderItems");
                if(status!="Pending")
                    await DatabaseTool.Execute(connection,$"EXEC dbo.usp_TransitionOrderItem @OrderItemId={id},@ToStatus='Preparing',@ActorUserId=3;");
                if(status is "Ready" or "Served")
                    await DatabaseTool.Execute(connection,$"EXEC dbo.usp_TransitionOrderItem @OrderItemId={id},@ToStatus='Ready',@ActorUserId=3;");
                if(status=="Served")
                    await DatabaseTool.Execute(connection,$"EXEC dbo.usp_TransitionOrderItem @OrderItemId={id},@ToStatus='Served',@ActorUserId=2;");
                return id;
            }
            async Task<HttpResponseMessage> Cancel(HttpClient client,long id,string expected,string reason,Guid request,
                bool confirmed=true,int qty=2,string? csrf=null)
                => await client.PostAsync($"/Ordering/Sent/{id}/CancelPrepared",Form(("quantity",qty.ToString()),
                    ("reason",reason),("expectedStatus",expected),("requestId",request.ToString()),("confirmCharged",confirmed.ToString()),
                    ("__RequestVerificationToken",csrf??token)));
            var successful = new List<long>();
            foreach(var state in new[]{"Preparing","Ready"})
            foreach(var reason in new[]{"ChangedMind","Mistake","SoldOut"})
            {
                var id=await Add(state); var before=await Scalar(connection,$"SELECT Subtotal FROM dbo.vw_SessionTotals WHERE SessionId={session}");
                var request=Guid.NewGuid(); var watch=Stopwatch.StartNew();
                using(var response=await Cancel(web.Client,id,state,reason,request))
                    Assert(response.StatusCode==HttpStatusCode.OK,$"Task 3: manager cancels {state} with {reason}");
                var kitchenPage=await Html(kitchen,"/Kitchen");watch.Stop();
                Assert(watch.Elapsed<TimeSpan.FromSeconds(5) && kitchenPage.Contains($"data-stopped-order=\"{id}\"")
                    && !kitchenPage.Contains($"data-order-item=\"{id}\"") && kitchenPage.Contains("Dừng món"),
                    "Task 3: kitchen sees stop notice and removed queue item within five seconds");
                Assert(await Scalar(connection,$"SELECT Subtotal FROM dbo.vw_SessionTotals WHERE SessionId={session}")==before,
                    "Task 3: charged cancellation preserves subtotal");
                Assert(await Scalar(connection,$"SELECT COUNT(*) FROM dbo.OrderItems WHERE Id={id} AND Status='Cancelled' AND ChargeWhenCancelled=1 AND Quantity=2 AND CancelReason='{reason}' AND CancelledBy=1 AND CancelledAt IS NOT NULL")==1,
                    "Task 3: cancelled line retains full quantity, charge flag, reason, actor and time");
                using(var again=await Cancel(web.Client,id,state,reason,request))
                    Assert(again.IsSuccessStatusCode,"Task 3: retry succeeds idempotently");
                using(var mismatch=await Cancel(web.Client,id,state,reason=="Mistake"?"SoldOut":"Mistake",request))
                    Assert(mismatch.StatusCode==HttpStatusCode.Conflict,"Task 3: request cannot be reused with a different reason");
                Assert(await Scalar(connection,$"SELECT COUNT(*) FROM dbo.OrderItemEvents WHERE OrderItemId={id} AND ToStatus='Cancelled'")==1,
                    "Task 3: retries create one cancellation event");
                successful.Add(id);
            }

            var pending=await Add("Pending"); var served=await Add("Served"); var preparing=await Add("Preparing");
            foreach(var (id,state,reason,confirmed,qty) in new[]{
                (pending,"Preparing","Mistake",true,2),(served,"Ready","Mistake",true,2),
                (preparing,"Preparing","",true,2),(preparing,"Preparing","ManagerOverride",true,2),
                (preparing,"Preparing","Mistake",false,2),(preparing,"Preparing","Mistake",true,1)})
            {
                using var denied=await Cancel(web.Client,id,state,reason,Guid.NewGuid(),confirmed,qty);
                Assert(denied.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
                    "Task 3: invalid state, reason, partial quantity or missing charged confirmation is rejected");
            }
            foreach(var client in new[]{waiter,kitchen,cashier})
            {
                using var denied=await Cancel(client,preparing,"Preparing","Mistake",Guid.NewGuid());
                Assert(denied.StatusCode==HttpStatusCode.Forbidden,"Task 3: non-manager cannot post charged cancellation");
            }
            using(var csrf=await Cancel(web.Client,preparing,"Preparing","Mistake",Guid.NewGuid(),csrf:"invalid"))
                Assert(csrf.StatusCode==HttpStatusCode.BadRequest,"Task 3: invalid CSRF is rejected");

            // The kitchen wins the shared transaction lock and changes the state while confirmation is open.
            await using(var cn=new SqlConnection(connection))
            {
                await cn.OpenAsync();await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
                await using var advance=new SqlCommand($"EXEC dbo.usp_TransitionOrderItem @OrderItemId={preparing},@ToStatus='Ready',@ActorUserId=3;",cn,tx);
                await advance.ExecuteNonQueryAsync();
                var inFlight=Cancel(web.Client,preparing,"Preparing","Mistake",Guid.NewGuid());
                await Task.Delay(200);Assert(!inFlight.IsCompleted,"Task 3: cancellation waits for concurrent kitchen change");
                await tx.CommitAsync();using var conflict=await inFlight;
                Assert(conflict.StatusCode==HttpStatusCode.Conflict,"Task 3: stale Preparing confirmation is rejected after Ready");
            }
            using(var snapshot=JsonDocument.Parse(await web.Client.GetStringAsync("/Ordering/Sent/Snapshot")))
                Assert(snapshot.RootElement.EnumerateArray().Single(x=>x.GetProperty("id").GetInt64()==preparing).GetProperty("status").GetString()=="Ready",
                    "Task 3: conflict snapshot exposes latest state");
            var duplicateRequest=Guid.NewGuid();
            var duplicates=await Task.WhenAll(Cancel(web.Client,preparing,"Ready","Mistake",duplicateRequest),
                Cancel(web.Client,preparing,"Ready","Mistake",duplicateRequest));
            foreach(var r in duplicates){Assert(r.IsSuccessStatusCode,"Task 3: concurrent duplicate confirmation succeeds");r.Dispose();}
            Assert(await Scalar(connection,$"SELECT COUNT(*) FROM dbo.PreparedOrderCancellations WHERE OrderItemId={preparing}")==1,
                "Task 3: concurrent duplicate creates one audit entry");
            using(var freshDuplicate=await Cancel(web.Client,preparing,"Ready","Mistake",Guid.NewGuid()))
                Assert(freshDuplicate.StatusCode==HttpStatusCode.Conflict,"Task 3: already cancelled line cannot be cancelled again");
            successful.Add(preparing);
            var becameServed=await Add("Ready");
            await DatabaseTool.Execute(connection,$"EXEC dbo.usp_TransitionOrderItem @OrderItemId={becameServed},@ToStatus='Served',@ActorUserId=2;");
            using(var stale=await Cancel(web.Client,becameServed,"Ready","SoldOut",Guid.NewGuid()))
                Assert(stale.StatusCode==HttpStatusCode.Conflict,"Task 3: Ready-to-Served change prevents cancellation");

            foreach(var actor in new[]{2,3,4})
                await RejectSql(connection,$"EXEC dbo.usp_CancelPreparedOrderItem @OrderItemId={served},@Quantity=2,@Reason='Mistake',@ActorUserId={actor},@RequestId='{Guid.NewGuid()}',@ExpectedStatus='Ready',@ConfirmCharged=1;",51001);
            await DatabaseTool.Execute(connection,"UPDATE dbo.Users SET IsActive=0 WHERE Id=1;");
            await RejectSql(connection,$"EXEC dbo.usp_CancelPreparedOrderItem @OrderItemId={served},@Quantity=2,@Reason='Mistake',@ActorUserId=1,@RequestId='{Guid.NewGuid()}',@ExpectedStatus='Ready',@ConfirmCharged=1;",51001);
            await DatabaseTool.Execute(connection,"UPDATE dbo.Users SET IsActive=1 WHERE Id=1;");
            await RejectSql(connection,$"EXEC dbo.usp_CancelPreparedOrderItem @OrderItemId={served},@Quantity=2,@Reason=NULL,@ActorUserId=1,@RequestId='{Guid.NewGuid()}',@ExpectedStatus='Ready',@ConfirmCharged=1;",51032);

            // A separate bill has ONLY a charged cancelled dish: checkout must still be available.
            await DatabaseTool.Execute(connection,"""
                DECLARE @t int=(SELECT TOP(1) Id FROM dbo.DiningTables WHERE Status='Available' AND IsActive=1 AND MaxCapacity>=2 ORDER BY Id);
                EXEC dbo.usp_OpenSession @TableId=@t,@GuestCount=2,@ActorUserId=2;
                """);
            var billSession=await Scalar(connection,"SELECT MAX(Id) FROM dbo.DiningSessions");
            var billedItem=await Add("Preparing",billSession);
            var originalTotal=await Scalar(connection,$"SELECT Subtotal FROM dbo.vw_SessionTotals WHERE SessionId={billSession}");
            using(var response=await Cancel(web.Client,billedItem,"Preparing","ChangedMind",Guid.NewGuid()))
                Assert(response.IsSuccessStatusCode,"Task 3: cancellation on bill-only session succeeds");
            var payPage=await Html(cashier,"/Cashier");
            var payRow=Regex.Match(payPage,$"<tr data-session-id=\"{billSession}\">.*?</tr>",RegexOptions.Singleline).Value;
            Assert(payRow.Contains("/Cashier/Checkout") && payRow.Contains("Thanh toán"),"Task 3: only charged cancelled dishes still allow checkout");
            var paymentRequest=Guid.NewGuid();
            using(var paid=await cashier.PostAsync("/Cashier/Checkout",Form(("sessionId",billSession.ToString()),
                ("requestId",paymentRequest.ToString()),("method","Cash"),("cashReceived",originalTotal.ToString()),("__RequestVerificationToken",Token(payPage)))))
                Assert(paid.StatusCode==HttpStatusCode.Redirect,"Task 3: cashier checks out charged cancelled dish");
            var invoice=await Scalar(connection,$"SELECT Id FROM dbo.Invoices WHERE SessionId={billSession}");
            Assert(invoice>0 && await Scalar(connection,$"SELECT Total FROM dbo.Invoices WHERE Id={invoice}")==originalTotal
                && await Scalar(connection,$"SELECT COUNT(*) FROM dbo.InvoiceLines WHERE InvoiceId={invoice} AND OrderItemId={billedItem} AND IsChargedCancellation=1 AND LineTotal={originalTotal}")==1,
                "Task 3: invoice preserves charged cancelled line and its full value");
            var invoicePage=await Html(cashier,$"/Cashier/Invoices/{invoice}");
            Assert(invoicePage.Contains("Đã huỷ có tính tiền") && invoicePage.Contains($"data-invoice-order-item=\"{billedItem}\""),
                "Task 3: invoice detail labels charged cancellation");
            Assert((await Html(web.Client,$"/Cashier/Invoices/{invoice}")).Contains("Đã huỷ có tính tiền"),"Task 3: manager can view labelled invoice");
            foreach(var client in new[]{waiter,kitchen})
            {
                using var forbidden=await client.GetAsync($"/Cashier/Invoices/{invoice}");
                Assert(forbidden.StatusCode==HttpStatusCode.Forbidden,"Task 3: invoice access remains cashier/manager only");
            }
            Assert((await Html(kitchen,"/Kitchen")).Contains($"data-stopped-order=\"{billedItem}\""),
                "Task 3: stop notice remains until shift closes, even after checkout");
            using var report=JsonDocument.Parse(await web.Client.GetStringAsync("/ShiftReports/Cancellations?shiftId=1"));
            var entry=report.RootElement.GetProperty("entries").EnumerateArray().Single(x=>x.GetProperty("orderItemId").GetInt64()==billedItem);
            Assert(entry.GetProperty("chargeWhenCancelled").GetBoolean() && entry.GetProperty("chargedAmount").GetDecimal()==originalTotal
                && entry.GetProperty("reason").GetString()=="Khách đổi ý" && entry.GetProperty("actorUserId").GetInt32()==1,
                "Task 3: shift report includes complete charged cancellation after checkout");
            Console.WriteLine("PASS: S3-05 Task 3 charged cancellation, permissions, races, kitchen stop, invoice and report.");
        }
        finally
        {
            SqlConnection.ClearAllPools();builder.InitialCatalog="master";
            await using var cn=new SqlConnection(builder.ConnectionString);await cn.OpenAsync();
            await using var cmd=new SqlCommand($"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END",cn);
            cmd.Parameters.AddWithValue("@name",name);await cmd.ExecuteNonQueryAsync();
        }
    }
    private static async Task<long> Scalar(string connection,string sql)
    {
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        await using var cmd=new SqlCommand(sql,cn);var result=await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }
    private static async Task RejectSql(string connection,string sql,int error)
    {
        try { await DatabaseTool.Execute(connection,sql);throw new Exception("Invalid direct SQL cancellation accepted"); }
        catch(SqlException ex) when(ex.Number==error){Assert(true,"Task 3: invalid direct SQL request denied");}
    }
}
