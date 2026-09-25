using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace RestaurantManagement.DbTool;

internal static class Verification
{
    internal static async Task Run(string baseConnection)
    {
        var builder = new SqlConnectionStringBuilder(baseConnection);
        // Only this exact, freshly generated database may be deleted by this command.
        var name = "RestaurantManagement_Test_" + Guid.NewGuid().ToString("N");
        builder.InitialCatalog = name;
        var connection = builder.ConnectionString;
        try
        {
            await DatabaseTool.Migrate(connection);
            await DatabaseTool.Migrate(connection);
            await DatabaseTool.Seed(connection, "VerificationOnly9!" + Guid.NewGuid().ToString("N"));
            await Check(connection, "Seed", "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.MenuItems)=60 AND (SELECT COUNT(*) FROM dbo.DiningTables)=25 AND (SELECT COUNT(*) FROM dbo.Reservations)=20 THEN 1 ELSE 0 END");
            await BookingConcurrency(connection);
            await DatabaseTool.Execute(connection, "EXEC dbo.usp_OpenShift @Name=N'Test',@OpeningCash=100000,@ActorUserId=4; EXEC dbo.usp_OpenSession @TableId=1,@GuestCount=2,@ActorUserId=2;");
            const string items = """[{"MenuItemId":1,"Quantity":2,"Notes":"ít cay"},{"MenuItemId":1,"Quantity":1,"Notes":"không hành"}]""";
            var request = Guid.NewGuid();
            await Call(connection, "usp_SubmitOrder", ("SessionId", 1L), ("RequestId", request), ("ItemsJson", items), ("ActorUserId", 2));
            await Call(connection, "usp_SubmitOrder", ("SessionId", 1L), ("RequestId", request), ("ItemsJson", items), ("ActorUserId", 2));
            await Check(connection, "Order retry and separate notes", "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.OrderBatches)=1 AND (SELECT COUNT(*) FROM dbo.OrderItems)=2 THEN 1 ELSE 0 END");
            await DatabaseTool.Execute(connection, "EXEC dbo.usp_UpdateMenuPrice @MenuItemId=1,@Price=99999,@ActorUserId=1;");
            await Check(connection, "Price snapshot", "SELECT CASE WHEN MIN(UnitPrice)=25000 AND MAX(UnitPrice)=25000 THEN 1 ELSE 0 END FROM dbo.OrderItems");
            await Reject(connection, "No skipped kitchen states", "EXEC dbo.usp_TransitionOrderItem @OrderItemId=1,@ToStatus='Ready',@ActorUserId=3;", 51030);
            await Reject(connection, "Waiter cannot change kitchen states", "EXEC dbo.usp_TransitionOrderItem @OrderItemId=1,@ToStatus='Preparing',@ActorUserId=2;", 51001);
            await DatabaseTool.Execute(connection, "EXEC dbo.usp_TransitionOrderItem @OrderItemId=1,@ToStatus='Preparing',@ActorUserId=3;");
            await Reject(connection, "Waiter cannot cancel prepared food", "EXEC dbo.usp_CancelOrderItem @OrderItemId=1,@Reason='ChangedMind',@ActorUserId=2;", 51001);
            await DatabaseTool.Execute(connection, "EXEC dbo.usp_TransitionOrderItem @OrderItemId=1,@ToStatus='Ready',@ActorUserId=3; EXEC dbo.usp_TransitionOrderItem @OrderItemId=1,@ToStatus='Served',@ActorUserId=2; EXEC dbo.usp_TransitionOrderItem @OrderItemId=1,@ToStatus='Served',@ActorUserId=2; EXEC dbo.usp_CancelOrderItem @OrderItemId=2,@Reason='Mistake',@ActorUserId=2;");
            await Check(connection, "Exactly one served event", "SELECT CASE WHEN COUNT(*)=1 THEN 1 ELSE 0 END FROM dbo.OrderItemEvents WHERE OrderItemId=1 AND ToStatus='Served'");
            await DatabaseTool.Execute(connection, "EXEC dbo.usp_SetMenuAvailability @MenuItemId=2,@IsSoldOut=1,@ActorUserId=3;");
            await Reject(connection, "Sold-out item rejected", """EXEC dbo.usp_SubmitOrder @SessionId=1,@RequestId='01010101-0101-0101-0101-010101010101',@ItemsJson=N'[{"MenuItemId":2,"Quantity":1}]',@ActorUserId=2;""", 51028);
            await DatabaseTool.Execute(connection, "EXEC dbo.usp_SetPaymentState @SessionId=1,@Awaiting=1,@ActorUserId=2;");
            await Reject(connection, "No new order during payment", """EXEC dbo.usp_SubmitOrder @SessionId=1,@RequestId='02020202-0202-0202-0202-020202020202',@ItemsJson=N'[{"MenuItemId":3,"Quantity":1}]',@ActorUserId=2;""", 51025);
            await Reject(connection, "No early shift close", "EXEC dbo.usp_CloseShift @ShiftId=1,@CountedCash=100000,@ActorUserId=4;", 51050);
            await Reject(connection, "Discount capped at 50%", "EXEC dbo.usp_Checkout @SessionId=1,@RequestId='03030303-0303-0303-0303-030303030303',@Method='Cash',@ActorUserId=4,@DiscountType='Percent',@DiscountValue=51,@DiscountReason=N'Test',@CashReceived=100000;", 51044);
            var payment = Guid.NewGuid();
            await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Call(connection, "usp_Checkout", ("SessionId", 1L), ("RequestId", payment), ("Method", "Cash"), ("ActorUserId", 4), ("DiscountType", "Percent"), ("DiscountValue", 10m), ("DiscountReason", "Test"), ("CashReceived", 100000m))));
            await Check(connection, "10 simultaneous checkout retries, exact VND totals", "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.Invoices)=1 AND (SELECT Total FROM dbo.Invoices)=45000 AND (SELECT COUNT(*) FROM dbo.InvoiceLines)=1 AND (SELECT ChangeAmount FROM dbo.Payments)=55000 AND (SELECT Status FROM dbo.DiningTables WHERE Id=1)='Cleaning' THEN 1 ELSE 0 END");
            await Reject(connection, "Immutable invoice", "UPDATE dbo.Invoices SET Subtotal=40000 WHERE Id=1;", 51105);
            await Reject(connection, "Immutable payment", "UPDATE dbo.Payments SET Amount=1 WHERE Id=1;", 51103);
            await DatabaseTool.Execute(connection, "EXEC dbo.usp_CleanTable @TableId=1,@ActorUserId=2;");
            await Reject(connection, "Cash mismatch requires reason", "EXEC dbo.usp_CloseShift @ShiftId=1,@CountedCash=0,@ActorUserId=4;", 51051);
            await DatabaseTool.Execute(connection, "EXEC dbo.usp_CloseShift @ShiftId=1,@CountedCash=145000,@ActorUserId=4;");
            await Check(connection, "Cash reconciliation", "SELECT CASE WHEN CashDifference=0 AND Revenue=45000 AND InvoiceCount=1 THEN 1 ELSE 0 END FROM dbo.Shifts WHERE Id=1");
            await Reject(connection, "Closed shift protects invoice", "EXEC dbo.usp_VoidInvoice @InvoiceId=1,@Reason=N'Test',@ActorUserId=1;", 51056);
            await DatabaseTool.Execute(connection, "EXEC dbo.usp_ReopenShift @ShiftId=1,@Reason=N'Test',@ActorUserId=1; EXEC dbo.usp_VoidInvoice @InvoiceId=1,@Reason=N'Test',@ActorUserId=1;");
            await Check(connection, "Void retains records, excludes revenue", "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.vw_DailyRevenue)=0 AND (SELECT COUNT(*) FROM dbo.InvoiceLines)=1 THEN 1 ELSE 0 END");
            await MergeAndQr(connection);
            await DatabaseTool.Execute(connection, "EXEC dbo.usp_RunMaintenance; EXEC dbo.usp_RunMaintenance;");
            Console.WriteLine("PASS: all SQL Server integration checks.");
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

    private static async Task BookingConcurrency(string connection)
    {
        await DatabaseTool.Execute(connection, "DECLARE @n int=1,@start datetime2(3)=DATEADD(day,14,CONVERT(datetime2(3),CONVERT(date,SYSUTCDATETIME()))); WHILE @n<=50 BEGIN INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt) VALUES(CONCAT('T',RIGHT(CONCAT('00000',@n),5)),N'Test','0999999999',2,25,@start,DATEADD(minute,90,@start)); SET @n+=1; END;");
        var results = await Task.WhenAll(Enumerable.Range(21, 50).Select(async id =>
        {
            try { await Call(connection, "usp_ConfirmReservation", ("ReservationId", (long)id), ("TableId", 25), ("ActorUserId", 2)); return true; }
            catch (SqlException ex) when (ex.Number == 51009) { return false; }
        }));
        if (results.Count(x => x) != 1) throw new InvalidOperationException("Concurrent booking invariant failed.");
        Console.WriteLine("PASS: 50 concurrent bookings, exactly one confirmed.");
        await Reject(connection, "Direct SQL overlap guard", "UPDATE dbo.Reservations SET Status='Confirmed' WHERE Id BETWEEN 21 AND 70;", 51100);
    }

    private static async Task MergeAndQr(string connection)
    {
        await DatabaseTool.Execute(connection, "EXEC dbo.usp_OpenSession @TableId=2,@GuestCount=2,@ActorUserId=2; EXEC dbo.usp_OpenSession @TableId=3,@GuestCount=2,@ActorUserId=2; EXEC dbo.usp_MergeSessions @MainSessionId=2,@ChildSessionId=3,@ActorUserId=2; EXEC dbo.usp_UnmergeSession @ChildSessionId=3,@ActorUserId=2;");
        await Check(connection, "Merge/unmerge preserves sessions", "SELECT CASE WHEN (SELECT BillingSessionId FROM dbo.DiningSessions WHERE Id=3) IS NULL AND EXISTS(SELECT 1 FROM dbo.SessionMerges WHERE UndoneAt IS NOT NULL) THEN 1 ELSE 0 END");
        var qr = RandomNumberGenerator.GetBytes(32);
        var guest = RandomNumberGenerator.GetBytes(32);
        await Call(connection, "usp_RotateTableQr", ("TableId", 2), ("TokenHash", qr), ("ActorUserId", 1));
        await Call(connection, "usp_OpenGuestSession", ("QrTokenHash", qr), ("GuestTokenHash", guest));
        await Call(connection, "usp_SubmitOrder", ("SessionId", 2L), ("RequestId", Guid.NewGuid()), ("ItemsJson", """[{"MenuItemId":3,"Quantity":1}]"""), ("GuestTokenHash", guest));
        await Call(connection, "usp_RotateTableQr", ("TableId", 2), ("TokenHash", RandomNumberGenerator.GetBytes(32)), ("ActorUserId", 1));
        await Check(connection, "QR rotation revokes sessions", "SELECT CASE WHEN COUNT(*)=0 THEN 1 ELSE 0 END FROM dbo.GuestSessions WHERE RevokedAt IS NULL");
    }

    private static async Task Check(string connection, string label, string sql)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1) throw new InvalidOperationException("FAILED: " + label);
        Console.WriteLine("PASS: " + label);
    }
    private static async Task Reject(string connection, string label, string sql, int error)
    {
        try { await DatabaseTool.Execute(connection, sql); }
        catch (SqlException ex) when (ex.Number == error) { Console.WriteLine("PASS: " + label); return; }
        throw new InvalidOperationException("Expected rejection missing: " + label);
    }
    private static async Task Call(string connection, string procedure, params (string Key, object Value)[] parameters)
    {
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo." + procedure, cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
        foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue("@" + key, value);
        await cmd.ExecuteNonQueryAsync();
    }
}

