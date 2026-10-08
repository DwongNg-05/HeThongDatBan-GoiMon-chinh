using System.Data;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static class KitchenLineVerification
{
    internal static async Task RunIsolated(string baseConnection)
    {
        var builder = new SqlConnectionStringBuilder(baseConnection);
        var name = "RestaurantManagement_Test_" + Guid.NewGuid().ToString("N");
        builder.InitialCatalog = name;
        var connection = builder.ConnectionString;
        const string password = "Kitchen-Test-9x!";
        try
        {
            await DatabaseTool.Migrate(connection);
            await DatabaseTool.Seed(connection, password);
            await DatabaseTool.Execute(connection, """
                UPDATE dbo.Users SET MustChangePassword=0;
                EXEC dbo.usp_OpenShift @Name=N'Kitchen test',@OpeningCash=0,@ActorUserId=4;
                EXEC dbo.usp_OpenSession @TableId=1,@GuestCount=2,@ActorUserId=2;
                DECLARE @request uniqueidentifier=NEWID();
                EXEC dbo.usp_SubmitOrder @SessionId=1,@RequestId=@request,
                  @ItemsJson=N'[{"MenuItemId":1,"Quantity":2},{"MenuItemId":1,"Quantity":1,"Notes":"Không hành"}]',@ActorUserId=2;
                """);
            await Run(connection);
            await using var web = await BookingConfirmationVerification.Web.Start(connection, new() { ["Email__RetryPollSeconds"] = "0" });
            await BookingConfirmationVerification.Login(web.Client, "kitchen", password);
            var page = await BookingConfirmationVerification.Html(web.Client, "/Kitchen");
            var json = await web.Client.GetStringAsync("/Kitchen/Snapshot");
            using var snapshot = System.Text.Json.JsonDocument.Parse(json);
            var first = snapshot.RootElement.EnumerateArray().First(i => i.GetProperty("id").GetInt64() == 1);
            var version = first.GetProperty("version").GetString()!;
            foreach (var (from, to) in new[] { ("Pending", "Preparing"), ("Preparing", "Ready") })
            {
                using var response = await web.Client.PostAsync("/Kitchen/Transition", BookingConfirmationVerification.Form(
                    ("id", "1"), ("from", from), ("to", to), ("version", version),
                    ("__RequestVerificationToken", BookingConfirmationVerification.Token(page))));
                if (!response.IsSuccessStatusCode) throw new Exception("Kitchen HTTP transition failed: " + await response.Content.ReadAsStringAsync());
                using var next = System.Text.Json.JsonDocument.Parse(await web.Client.GetStringAsync("/Kitchen/Snapshot"));
                var updated = next.RootElement.EnumerateArray().First(i => i.GetProperty("id").GetInt64() == 1);
                version = updated.GetProperty("version").GetString()!;
                if (to == "Preparing" && (updated.GetProperty("actualCookingMilliseconds").ValueKind != System.Text.Json.JsonValueKind.Null
                    || updated.GetProperty("elapsedCookingMilliseconds").GetDouble() < 0))
                    throw new Exception("Preparing HTTP snapshot must have elapsed time but no actual duration.");
                if (to == "Ready" && (updated.GetProperty("actualCookingMilliseconds").GetDouble() <= 0
                    || updated.GetProperty("elapsedCookingMilliseconds").ValueKind != System.Text.Json.JsonValueKind.Null))
                    throw new Exception("Ready HTTP snapshot must have a fixed positive actual duration.");
            }
            using var waiter = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = web.Client.BaseAddress };
            await BookingConfirmationVerification.Login(waiter, "waiter", password);
            var readyPage = await BookingConfirmationVerification.Html(waiter, "/Kitchen/Ready");
            using var ready = System.Text.Json.JsonDocument.Parse(await waiter.GetStringAsync("/Kitchen/Snapshot"));
            if (!readyPage.Contains("kitchen-ready") || ready.RootElement.GetArrayLength() != 1 || ready.RootElement[0].GetProperty("id").GetInt64() != 1)
                throw new Exception("Waiter ready snapshot must contain exactly the finished dish.");
            using var denied = await waiter.PostAsync("/Kitchen/Transition", BookingConfirmationVerification.Form(("id", "1"),
                ("from", "Ready"), ("to", "Preparing"), ("version", version), ("__RequestVerificationToken", BookingConfirmationVerification.Token(readyPage))));
            if (denied.StatusCode != System.Net.HttpStatusCode.Forbidden) throw new Exception("Waiter must not transition kitchen dishes.");
            Console.WriteLine("PASS: real web sequential transitions, waiter ready snapshot exactly once, waiter write forbidden.");
            await KitchenBatchVerification.Run(connection,web.Client,waiter,BookingConfirmationVerification.Token(page),BookingConfirmationVerification.Token(readyPage));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            builder.InitialCatalog = "master";
            await using var cn = new SqlConnection(builder.ConnectionString);
            await cn.OpenAsync();
            await using var drop = new SqlCommand($"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", cn);
            drop.Parameters.AddWithValue("@name", name);
            await drop.ExecuteNonQueryAsync();
        }
    }
    internal static async Task Run(string connection)
    {
        async Task<byte[]> Version()
        {
            await using var cn = new SqlConnection(connection);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT RowVersion FROM dbo.OrderItems WHERE Id=1", cn);
            return (byte[])(await cmd.ExecuteScalarAsync())!;
        }
        async Task<int> Move(string from, string to, byte[] version, int actor = 3)
        {
            await using var cn = new SqlConnection(connection);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.usp_KitchenLineTransition", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@OrderItemId", 1L);
            cmd.Parameters.AddWithValue("@FromStatus", from);
            cmd.Parameters.AddWithValue("@ToStatus", to);
            cmd.Parameters.Add("@ExpectedVersion", SqlDbType.Binary, 8).Value = version;
            cmd.Parameters.AddWithValue("@ActorUserId", actor);
            try { await cmd.ExecuteNonQueryAsync(); return 0; }
            catch (SqlException ex) when (ex.Number is 51029 or 51030 or 51001) { return ex.Number; }
        }
        void Require(bool valid, string label)
        {
            if (!valid) throw new Exception("FAIL: " + label);
            Console.WriteLine("PASS: " + label);
        }
        var pending = await Version();
        Require(await Move("Pending", "Ready", pending) == 51030, "Kitchen rejects skipped state");
        Require(await Move("Pending", "Preparing", pending, 2) == 51001, "Waiter cannot transition kitchen line");
        var attempts = await Task.WhenAll(Move("Pending", "Preparing", pending), Move("Pending", "Preparing", pending));
        Require(attempts.Count(code => code == 0) == 1 && attempts.Count(code => code == 51029) == 1, "Two devices: exactly one start succeeds");
        var preparing = await Version();
        Require(await Move("Preparing", "Pending", preparing) == 51030, "Kitchen rejects backward state");
        attempts = await Task.WhenAll(Move("Preparing", "Ready", preparing), Move("Preparing", "Ready", preparing));
        Require(attempts.Count(code => code == 0) == 1 && attempts.Count(code => code == 51029) == 1, "Two devices: exactly one finish succeeds");
        Require(await Move("Ready", "Preparing", await Version()) == 51030, "Finished dish cannot move backward");
        await using var check = new SqlConnection(connection);
        await check.OpenAsync();
        await using var verify = new SqlCommand("""
            SELECT CASE WHEN
             (SELECT COUNT(*) FROM dbo.vw_KitchenQueue WHERE OrderItemId=1 AND Status='Ready')=1
             AND NOT EXISTS(SELECT 1 FROM dbo.vw_KitchenQueue WHERE OrderItemId=1 AND Status IN ('Pending','Preparing'))
             AND EXISTS(SELECT 1 FROM dbo.OrderItems WHERE Id=2 AND Status='Pending')
             AND EXISTS(SELECT 1 FROM dbo.OrderItems WHERE Id=1 AND PreparingAt IS NOT NULL AND ReadyAt>PreparingAt)
             AND (SELECT COUNT(*) FROM dbo.OrderItemEvents WHERE OrderItemId=1 AND FromStatus IS NOT NULL)=2
             AND EXISTS(SELECT 1 FROM dbo.OrderItemEvents e JOIN dbo.OrderItems i ON i.Id=e.OrderItemId
               WHERE i.Id=1 AND e.FromStatus='Pending' AND e.ToStatus='Preparing' AND e.OccurredAt=i.PreparingAt)
             AND EXISTS(SELECT 1 FROM dbo.OrderItemEvents e JOIN dbo.OrderItems i ON i.Id=e.OrderItemId
               WHERE i.Id=1 AND e.FromStatus='Preparing' AND e.ToStatus='Ready' AND e.OccurredAt=i.ReadyAt)
             THEN 1 ELSE 0 END;
            """, check);
        Require(Convert.ToInt32(await verify.ExecuteScalarAsync()) == 1, "Ready once; sibling unchanged; exactly two ordered server timestamps, no history for rejected or competing requests");
        // Restore only this disposable verification fixture for the existing business tests.
        await DatabaseTool.Execute(connection, "DELETE dbo.OrderItemEvents WHERE OrderItemId=1 AND FromStatus IS NOT NULL; UPDATE dbo.OrderItems SET Status='Pending',PreparingAt=NULL,ReadyAt=NULL WHERE Id=1;");
    }
}
