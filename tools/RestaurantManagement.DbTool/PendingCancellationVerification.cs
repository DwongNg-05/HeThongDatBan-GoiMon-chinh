using System.Data;
using System.Diagnostics;
using System.Net;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

internal static class PendingCancellationVerification
{
    internal static async Task Run(string baseConnection)
    {
        var builder = new SqlConnectionStringBuilder(baseConnection);
        var name = "RestaurantManagement_S305_Test_" + Guid.NewGuid().ToString("N");
        builder.InitialCatalog = name;
        var connection = builder.ConnectionString;
        try
        {
            await DatabaseTool.Migrate(connection);
            // Test database only: avoid resolving an unavailable Windows database owner.
            await DatabaseTool.Execute(connection, $"ALTER AUTHORIZATION ON DATABASE::[{name}] TO sa;");
            const string password = "S305TestOnly9!";
            await DatabaseTool.Seed(connection, password);
            await ShiftCancellationVerification.Empty(connection);
            await DatabaseTool.Execute(connection, """
                UPDATE dbo.Users SET MustChangePassword=0,IsActive=1,FailedLoginCount=0,LockedUntil=NULL;
                DECLARE @cashier int=(SELECT Id FROM dbo.Users WHERE UserName='cashier');
                DECLARE @waiter int=(SELECT Id FROM dbo.Users WHERE UserName='waiter');
                EXEC dbo.usp_OpenShift @Name=N'S305 test',@OpeningCash=0,@ActorUserId=@cashier;
                DECLARE @table int=(SELECT TOP(1) Id FROM dbo.DiningTables WHERE Status='Available' AND IsActive=1 AND MaxCapacity>=2 ORDER BY Id);
                EXEC dbo.usp_OpenSession @TableId=@table,@GuestCount=2,@ActorUserId=@waiter;
                """);
            var session = await Scalar(connection, "SELECT MAX(Id) FROM dbo.DiningSessions");
            var waiterId = (int)await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName='waiter'");
            var kitchenId = (int)await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName='kitchen'");
            var managerId = (int)await Scalar(connection, "SELECT Id FROM dbo.Users WHERE UserName='manager'");
            var dish = await Scalar(connection, "SELECT TOP(1) Id FROM dbo.MenuItems WHERE IsActive=1 AND IsSoldOut=0 AND IsTemporarilyOut=0 ORDER BY Id");
            await using var web = await Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
            await Login(web.Client, "waiter", password);
            using var initial = await web.Client.GetAsync("/Ordering/Sent");
            var html = await initial.Content.ReadAsStringAsync();
            if (!initial.IsSuccessStatusCode) throw new Exception($"Sent page: {initial.StatusCode}, location={initial.Headers.Location}, detail={html[..Math.Min(900,html.Length)]}");
            Assert(html.Contains("name=\"reason\" required") && html.Contains("data-cancel-submit disabled"), "Reason is required and confirmation initially disabled");
            var token = Token(html);
            using var kitchenHandler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var kitchen = new HttpClient(kitchenHandler) { BaseAddress = web.Client.BaseAddress };
            await Login(kitchen, "kitchen", password);

            async Task<long> Add(int quantity = 3)
            {
                await DatabaseTool.Execute(connection, $$"""
                    DECLARE @request uniqueidentifier=NEWID();
                    EXEC dbo.usp_SubmitOrder @SessionId={{session}},@RequestId=@request,
                     @ItemsJson=N'[{"MenuItemId":{{dish}},"Quantity":{{quantity}},"Notes":"S305"}]',@ActorUserId={{waiterId}};
                    """);
                return await Scalar(connection, "SELECT MAX(Id) FROM dbo.OrderItems WHERE Status='Pending'");
            }
            async Task<HttpResponseMessage> Cancel(HttpClient client, long id, int qty, string reason, Guid request, string? antiForgery = null)
                => await client.PostAsync($"/Ordering/Sent/{id}/Cancel", Form(("quantity", qty.ToString()),
                    ("reason", reason), ("requestId", request.ToString()), ("__RequestVerificationToken", antiForgery ?? token)));

            foreach (var reason in new[] { "ChangedMind", "Mistake", "SoldOut" })
            {
                var id = await Add(); var request = Guid.NewGuid();
                using var response = await Cancel(web.Client, id, 3, reason, request);
                Assert(response.StatusCode == HttpStatusCode.OK, $"Whole-line cancellation with {reason}");
                Assert(await Scalar(connection, $"SELECT Quantity FROM dbo.OrderItems WHERE Id={id}") == 3, "Cancelled line preserves its entire quantity");
                Assert(await Scalar(connection, $"SELECT COUNT(*) FROM dbo.PendingOrderCancellations c JOIN dbo.OrderItems i ON i.Id=c.CancelledOrderItemId WHERE c.RequestId='{request}' AND c.OrderItemId={id} AND c.Quantity=3 AND c.ActorUserId={waiterId} AND c.Reason='{reason}' AND c.OccurredAt IS NOT NULL AND i.Status='Cancelled' AND i.ChargeWhenCancelled=0") == 1, "Cancellation, quantity and full audit recorded");
                var subtotal = await Scalar(connection, $"SELECT Subtotal FROM dbo.vw_SessionTotals WHERE SessionId={session}");
                using var repeated = await Cancel(web.Client, id, 3, reason, request);
                Assert(repeated.StatusCode == HttpStatusCode.OK && await Scalar(connection, $"SELECT Subtotal FROM dbo.vw_SessionTotals WHERE SessionId={session}") == subtotal, "Same request returns success without another deduction");
                using var mismatched = await Cancel(web.Client, id, 2, reason, request);
                Assert(mismatched.StatusCode == HttpStatusCode.Conflict, "A request cannot be reused for different quantities");

            }
            Assert(await Scalar(connection, $"SELECT Subtotal FROM dbo.vw_SessionTotals WHERE SessionId={session}") == 0, "All cancelled portions are excluded from subtotal");
            var pending = await Add();
            foreach (var (qty, reason) in new[] { (3, ""), (3, "ManagerOverride"), (0, "Mistake"), (4, "Mistake"), (1, "Mistake") })
            {
                using var bad = await Cancel(web.Client, pending, qty, reason, Guid.NewGuid());
                Assert(bad.StatusCode == HttpStatusCode.BadRequest, "Direct invalid quantity/reason is rejected");
            }
            using (var csrf = await Cancel(web.Client, pending, 1, "Mistake", Guid.NewGuid(), "invalid"))
                Assert(csrf.StatusCode == HttpStatusCode.BadRequest, "Missing/invalid CSRF token is rejected");
            using (var forbidden = await Cancel(kitchen, pending, 1, "Mistake", Guid.NewGuid(), Token(await Html(kitchen, "/Kitchen"))))
                Assert(forbidden.StatusCode == HttpStatusCode.Forbidden, "Kitchen cannot cancel by posting directly");
            using (var anonymous = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = web.Client.BaseAddress })
            using (var denied = await Cancel(anonymous, pending, 1, "Mistake", Guid.NewGuid()))
                Assert(denied.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized, "Anonymous request is denied");

            // Deterministic race: kitchen holds the common lock after starting cooking.
            await using (var cn = new SqlConnection(connection))
            {
                await cn.OpenAsync(); await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
                await using var advance = new SqlCommand($"EXEC dbo.usp_TransitionOrderItem @OrderItemId={pending},@ToStatus='Preparing',@ActorUserId={kitchenId};", cn, tx);
                await advance.ExecuteNonQueryAsync();
                var cancel = Cancel(web.Client, pending, 1, "ChangedMind", Guid.NewGuid());
                await Task.Delay(250); Assert(!cancel.IsCompleted, "Cancellation waits for concurrent kitchen transaction");
                await tx.CommitAsync(); using var conflict = await cancel;
                Assert(conflict.StatusCode == HttpStatusCode.Conflict, "Kitchen wins race: cancellation rejected with latest status");
            }
            Assert(await Scalar(connection, $"SELECT Quantity FROM dbo.OrderItems WHERE Id={pending}") == 3, "Failed cancellation changes no quantity");
            using (var preparing = await Cancel(web.Client, pending, 3, "Mistake", Guid.NewGuid()))
                Assert(preparing.StatusCode == HttpStatusCode.Conflict, "Employee cannot cancel a preparing item directly");
            var snapshot = await web.Client.GetStringAsync("/Ordering/Sent/Snapshot");
            Assert(snapshot.Contains("Preparing") && snapshot.Contains("subtotal"), "Snapshot returns latest state and subtotal after conflict");

            var duplicate = await Add(); var duplicateRequest = Guid.NewGuid();
            var results = await Task.WhenAll(Cancel(web.Client, duplicate, 3, "Mistake", duplicateRequest), Cancel(web.Client, duplicate, 3, "Mistake", duplicateRequest));
            foreach (var result in results) { Assert(result.IsSuccessStatusCode, "Simultaneous duplicate request succeeds idempotently"); result.Dispose(); }
            Assert(await Scalar(connection, $"SELECT Quantity FROM dbo.OrderItems WHERE Id={duplicate}") == 3 && await Scalar(connection, $"SELECT COUNT(*) FROM dbo.PendingOrderCancellations WHERE RequestId='{duplicateRequest}'") == 1, "Concurrent duplicates deduct once and audit once");
            var remove = await Add(1);
            var watch = Stopwatch.StartNew();
            using (var removed = await Cancel(web.Client, remove, 1, "SoldOut", Guid.NewGuid())) Assert(removed.IsSuccessStatusCode, "Full pending cancellation succeeds");
            var kitchenHtml = await Html(kitchen, "/Kitchen"); watch.Stop();
            Assert(!kitchenHtml.Contains($"data-order-item=\"{remove}\"") && watch.Elapsed < TimeSpan.FromSeconds(5), "Cancelled line disappears from kitchen response within five seconds");
            Assert(kitchenHtml.Contains("kitchen-order-poll.js"), "Kitchen loads automatic queue refresh");
            var price = await Scalar(connection, $"SELECT UnitPrice FROM dbo.OrderItems WHERE Id={pending}");
            Assert(await Scalar(connection, $"SELECT Subtotal FROM dbo.vw_SessionTotals WHERE SessionId={session}") == price * 3, "Subtotal equals the remaining preparing line only");

            // Existing manager rule remains billable after preparation.
            await DatabaseTool.Execute(connection, $"EXEC dbo.usp_CancelOrderItem @OrderItemId={pending},@Reason='ManagerOverride',@ActorUserId={managerId};");
            Assert(await Scalar(connection, $"SELECT ChargeWhenCancelled FROM dbo.OrderItems WHERE Id={pending}") == 1 && await Scalar(connection, $"SELECT Subtotal FROM dbo.vw_SessionTotals WHERE SessionId={session}") == price * 3, "Existing manager cancellation after preparation is still charged");
            await ShiftCancellationVerification.Run(connection, web.Client, session, managerId, waiterId, password);
            await DatabaseTool.Execute(connection, $"UPDATE dbo.Users SET IsActive=0 WHERE Id={waiterId};");
            try
            {
                await DatabaseTool.Execute(connection, $"EXEC dbo.usp_CancelPendingOrderItem @OrderItemId={duplicate},@Quantity=1,@Reason='Mistake',@ActorUserId={waiterId},@RequestId='{Guid.NewGuid()}';");
                throw new Exception("Inactive actor was allowed");
            }
            catch (SqlException ex) when (ex.Number == 51001) { Assert(true, "Database denies an inactive actor"); }
            Console.WriteLine("PASS: S3-05 Task 1 HTTP, SQL, whole-line cancellation, races and idempotency.");
            var browserReady = Environment.GetEnvironmentVariable("RM_S305_BROWSER_READY_FILE");
            if (!string.IsNullOrWhiteSpace(browserReady))
            {
                await DatabaseTool.Execute(connection, $"UPDATE dbo.Users SET IsActive=1 WHERE Id={waiterId};");
                var browserPending = await Add(3);
                var browserPreparing = await Add(1);
                await DatabaseTool.Execute(connection, $"EXEC dbo.usp_TransitionOrderItem @OrderItemId={browserPreparing},@ToStatus='Preparing',@ActorUserId={kitchenId};");
                await File.WriteAllTextAsync(browserReady, System.Text.Json.JsonSerializer.Serialize(new { url=web.Client.BaseAddress, pending=browserPending, preparing=browserPreparing }));
                var deadline = DateTime.UtcNow.AddMinutes(10);
                while (!File.Exists(browserReady + ".done") && DateTime.UtcNow < deadline) await Task.Delay(1000);
            }
        }
        finally
        {
            SqlConnection.ClearAllPools(); builder.InitialCatalog = "master";
            await using var cn = new SqlConnection(builder.ConnectionString); await cn.OpenAsync();
            await using var drop = new SqlCommand($"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", cn);
            drop.Parameters.AddWithValue("@name", name); await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> Scalar(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn); return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
