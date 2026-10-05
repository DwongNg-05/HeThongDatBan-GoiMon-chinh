using System.Data;
using System.Security.Cryptography;
using System.Text;
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
            var password = "VerificationOnly9!" + Guid.NewGuid().ToString("N");
            await DatabaseTool.Seed(connection, password);
            await Check(connection, "Seed", "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.MenuItems)=60 AND (SELECT COUNT(*) FROM dbo.DiningTables)=60 AND (SELECT COUNT(*) FROM dbo.Reservations)=20 THEN 1 ELSE 0 END");
            await LoginVerification.Run(connection, password);
            await EmailVerificationVerification.Run(connection, password);
            await VerifyAreas(connection);
            await VerifyTableReservationSchedule(connection);
            await VerifyManagedReservationConcurrencyAndSuggestions(connection);
            await VerifyManagedReservationRelease(connection);
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
            await Check(connection, "10 simultaneous checkout retries, exact VND totals and table status outbox", "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.Invoices)=1 AND (SELECT Total FROM dbo.Invoices)=45000 AND (SELECT COUNT(*) FROM dbo.InvoiceLines)=1 AND (SELECT ChangeAmount FROM dbo.Payments)=55000 AND (SELECT Status FROM dbo.DiningTables WHERE Id=1)='Cleaning' AND EXISTS(SELECT 1 FROM dbo.TableStatusChangeEvents WHERE TableId=1 AND PreviousStatus='Serving' AND Status='Cleaning') THEN 1 ELSE 0 END");
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
            await VerifyAreaLifecycle(connection);
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

    private static async Task VerifyAreas(string connection)
    {
        await Call(connection, "usp_CreateArea", ("ActorUserId", 1), ("Name", "Task1 sân"), ("SortOrder", 7));
        await Check(connection, "Area saved active", "SELECT CASE WHEN COUNT(*)=1 THEN 1 ELSE 0 END FROM dbo.Areas WHERE Name=N'Task1 sân' AND SortOrder=7 AND IsActive=1");
        await Reject(connection, "Duplicate case-insensitive", "EXEC dbo.usp_CreateArea 1,N'TASK1 SÂN',8", 51402);
        await Reject(connection, "Negative order", "EXEC dbo.usp_CreateArea 1,N'Bad',-1", 51403);
        await Reject(connection, "Missing order", "EXEC dbo.usp_CreateArea 1,N'Bad',NULL", 51403);
        await Reject(connection, "Blank name", "EXEC dbo.usp_CreateArea 1,N'   ',1", 51401);
        await Reject(connection, "No catalog permission", "EXEC dbo.usp_CreateArea 2,N'Bad',1", 51001);
        await Reject(connection, "Create normalizes whitespace", "EXEC dbo.usp_CreateArea 1,N'  Task1   sân  ',0", 51402);
        await Call(connection, "usp_CreateArea", ("ActorUserId", 1), ("Name", "Task1 san"), ("SortOrder", 0));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => {
            try { await Call(connection,"usp_CreateArea",("ActorUserId",1),("Name","Task1 race"),("SortOrder",1)); return true; }
            catch (SqlException ex) when (ex.Number == 51402) { return false; }
        }));
        if (results.Count(x => x) != 1) throw new Exception("Concurrent area creation failed");
        Console.WriteLine("PASS: Concurrent duplicate area creation");
        await DatabaseTool.Execute(connection, "UPDATE dbo.Areas SET IsActive=0 WHERE Name=N'Task1 san'");
        await Check(connection, "Management list includes inactive areas and is sorted", """
            DECLARE @list TABLE(Position int IDENTITY,Id int,Name nvarchar(80),SortOrder int,Notes nvarchar(500),IsActive bit);
            INSERT @list(Id,Name,SortOrder,Notes,IsActive) EXEC dbo.usp_ListAreas;
            SELECT CASE WHEN EXISTS(SELECT 1 FROM @list WHERE Name=N'Task1 sân' AND SortOrder=7)
             AND EXISTS(SELECT 1 FROM @list WHERE IsActive=0)
             AND NOT EXISTS(SELECT 1 FROM @list a JOIN @list b ON a.Position<b.Position WHERE a.SortOrder>b.SortOrder)
             AND (SELECT COUNT(*) FROM @list)=(SELECT COUNT(*) FROM dbo.Areas)
             THEN 1 ELSE 0 END;
            """);
        await Reject(connection, "Inactive name still reserved", "EXEC dbo.usp_CreateArea 1,N'Task1 san',0", 51402);
    }
    private static async Task VerifyAreaLifecycle(string connection)
    {
        await DatabaseTool.Execute(connection, "EXEC dbo.usp_CreateArea 1,N'Lifecycle sân',4;");
        const string areaId = "DECLARE @id int=(SELECT Id FROM dbo.Areas WHERE Name=N'Lifecycle sân'); ";
        await DatabaseTool.Execute(connection, areaId + "EXEC dbo.usp_UpdateArea 1,@id,N'Lifecycle sân',4,N'Gần cửa sổ';");
        await Check(connection, "Edit notes with unchanged name", "SELECT CASE WHEN Notes=N'Gần cửa sổ' AND SortOrder=4 THEN 1 ELSE 0 END FROM dbo.Areas WHERE Name=N'Lifecycle sân'");
        await DatabaseTool.Execute(connection, areaId + "EXEC dbo.usp_UpdateArea 1,@id,N'Lifecycle sân',0,N'Gần cửa sổ';");
        await Check(connection, "Edit sort order", "SELECT CASE WHEN SortOrder=0 THEN 1 ELSE 0 END FROM dbo.Areas WHERE Name=N'Lifecycle sân'");
        await Reject(connection, "Rename duplicate", areaId + "EXEC dbo.usp_UpdateArea 1,@id,N'Tầng một',0;", 51402);
        await Reject(connection, "Rename case and extra whitespace duplicate", areaId + "EXEC dbo.usp_UpdateArea 1,@id,N'  TẦNG    MỘT ',0;", 51402);
        await Reject(connection, "Rename tab duplicate", areaId + "DECLARE @n nvarchar(80)=N'Tầng'+NCHAR(9)+N'một'; EXEC dbo.usp_UpdateArea 1,@id,@n,0;", 51402);
        await Reject(connection, "Edit negative order", areaId + "EXEC dbo.usp_UpdateArea 1,@id,N'Lifecycle sân',-1;", 51403);
        await Reject(connection, "Edit missing area", "EXEC dbo.usp_UpdateArea 1,2147483647,N'Missing',0;", 51404);
        await Check(connection, "Rejected edits unchanged", "SELECT CASE WHEN SortOrder=0 AND Notes=N'Gần cửa sổ' THEN 1 ELSE 0 END FROM dbo.Areas WHERE Name=N'Lifecycle sân'");
        await DatabaseTool.Execute(connection, areaId + "DECLARE @before int=(SELECT COUNT(*) FROM dbo.Areas); EXEC dbo.usp_UpdateArea 1,@id,N'  Lifecycle   renamed ',2,N'Mới'; IF (SELECT COUNT(*) FROM dbo.Areas)<>@before OR NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE Id=@id AND Name=N'Lifecycle renamed') THROW 51900,'Edit created a new area or failed to normalize',1;");
        Console.WriteLine("PASS: Rename normalizes without creating a new row");
        await DatabaseTool.Execute(connection, """
            DECLARE @id int=(SELECT Id FROM dbo.Areas WHERE Name=N'Lifecycle renamed');
            INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,SortOrder) VALUES(@id,'LIFE01',1,4,0),(@id,'LIFE02',1,4,1);
            DECLARE @start datetime2(3)=DATEADD(hour,3,CONVERT(datetime2(3),CONVERT(date,DATEADD(day,20,SYSUTCDATETIME()))));
            EXEC dbo.usp_CreateReservation @CustomerName=N'Lifecycle 1',@Phone='0981111111',@GuestCount=2,@StartsAt=@start,@PreferredAreaId=@id;
            EXEC dbo.usp_CreateReservation @CustomerName=N'Lifecycle 2',@Phone='0982222222',@GuestCount=2,@StartsAt=@start,@PreferredAreaId=@id;
            """);
        await Reject(connection, "Area with tables cannot be deleted", "DECLARE @id int=(SELECT Id FROM dbo.Areas WHERE Name=N'Lifecycle renamed'); EXEC dbo.usp_DeleteArea 1,@id;", 51007);
        await DatabaseTool.Execute(connection, "DECLARE @id int=(SELECT Id FROM dbo.Areas WHERE Name=N'Lifecycle renamed'); EXEC dbo.usp_DeactivateArea 1,@id; EXEC dbo.usp_UpdateArea 1,@id,N'Lifecycle archived',2,N'Mới';");
        await Check(connection, "Deactivation keeps area and tables", "SELECT CASE WHEN a.IsActive=0 AND EXISTS(SELECT 1 FROM dbo.DiningTables t WHERE t.AreaId=a.Id) THEN 1 ELSE 0 END FROM dbo.Areas a WHERE a.Name=N'Lifecycle archived'");
        await Check(connection, "Multiple historical bookings keep original name after rename and deactivation", "SELECT CASE WHEN COUNT(*)=2 AND MIN(AreaNameSnapshot)=N'Lifecycle renamed' AND MAX(AreaNameSnapshot)=N'Lifecycle renamed' THEN 1 ELSE 0 END FROM dbo.Reservations WHERE CustomerName IN (N'Lifecycle 1',N'Lifecycle 2')");
        await Reject(connection, "Inactive area rejects new booking", "DECLARE @id int=(SELECT Id FROM dbo.Areas WHERE Name=N'Lifecycle archived'),@start datetime2(3)=DATEADD(day,20,SYSUTCDATETIME()); EXEC dbo.usp_CreateReservation @CustomerName=N'Bad',@Phone='0983333333',@GuestCount=2,@StartsAt=@start,@PreferredAreaId=@id;", 51407);
        await DatabaseTool.Execute(connection, "EXEC dbo.usp_CreateArea 1,N'Lifecycle empty',0; DECLARE @id int=(SELECT Id FROM dbo.Areas WHERE Name=N'Lifecycle empty'); EXEC dbo.usp_DeactivateArea 1,@id;");
        await Check(connection, "Deactivate without tables", "SELECT CASE WHEN IsActive=0 THEN 1 ELSE 0 END FROM dbo.Areas WHERE Name=N'Lifecycle empty'");
        await Reject(connection, "Inactive canonical name remains reserved", "EXEC dbo.usp_CreateArea 1,N'  LIFECYCLE   EMPTY ',0;", 51402);
        await Reject(connection, "Missing area cannot be deleted", "EXEC dbo.usp_DeleteArea 1,2147483647;", 51404);
    }

    private static async Task BookingConcurrency(string connection)
    {
        await DatabaseTool.Execute(connection, "DECLARE @n int=1,@start datetime2(3)=DATEADD(day,14,CONVERT(datetime2(3),CONVERT(date,SYSUTCDATETIME()))); WHILE @n<=50 BEGIN INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,StartsAt,EndsAt) VALUES(CONCAT('T',RIGHT(CONCAT('00000',@n),5)),N'Test','0999999999',2,@start,DATEADD(minute,90,@start)); SET @n+=1; END;");
        var results = await Task.WhenAll(Enumerable.Range(21, 50).Select(async id =>
        {
            try { await Call(connection, "usp_ConfirmReservation", ("ReservationId", (long)id), ("TableId", 25), ("ActorUserId", 2)); return true; }
            catch (SqlException ex) when (ex.Number == 51009) { return false; }
        }));
        if (results.Count(x => x) != 1) throw new InvalidOperationException("Concurrent booking invariant failed.");
        Console.WriteLine("PASS: 50 concurrent bookings, exactly one confirmed.");
        await Reject(connection, "Direct SQL overlap guard", "UPDATE dbo.Reservations SET Status='Confirmed',TableId=25 WHERE Id BETWEEN 21 AND 70;", 51060);
    }

    private static async Task VerifyTableReservationSchedule(string connection)
    {
        const string firstStart = "2030-01-01T19:00:00";
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_CreateManagedTableReservation @TableId=45,@StartsAt='{firstStart}',@CustomerName=N'Lịch bàn',@Phone='0981000001',@InitialStatus='Pending',@ActorUserId=1;");
        await Reject(connection, "Managed table reservation rejects full overlap", $"EXEC dbo.usp_CreateManagedTableReservation @TableId=45,@StartsAt='{firstStart}',@CustomerName=N'Trùng toàn phần',@Phone='0981000002',@InitialStatus='Pending',@ActorUserId=1;", 51060);
        await Reject(connection, "Managed table reservation rejects overlap at start", "EXEC dbo.usp_CreateManagedTableReservation @TableId=45,@StartsAt='2030-01-01T20:00:00',@CustomerName=N'Giao đầu',@Phone='0981000003',@InitialStatus='Confirmed',@ActorUserId=1;", 51060);
        await Reject(connection, "Managed table reservation rejects cleanup interval", "EXEC dbo.usp_CreateManagedTableReservation @TableId=45,@StartsAt='2030-01-01T20:40:00',@CustomerName=N'Dọn bàn',@Phone='0981000004',@InitialStatus='Pending',@ActorUserId=1;", 51060);
        await DatabaseTool.Execute(connection, "EXEC dbo.usp_CreateManagedTableReservation @TableId=45,@StartsAt='2030-01-01T20:45:00',@CustomerName=N'Đúng mốc',@Phone='0981000005',@InitialStatus='Confirmed',@ActorUserId=1;");
        await DatabaseTool.Execute(connection, $"EXEC dbo.usp_CreateManagedTableReservation @TableId=46,@StartsAt='{firstStart}',@CustomerName=N'Bàn khác',@Phone='0981000006',@InitialStatus='Pending',@ActorUserId=1; EXEC dbo.usp_CreateManagedTableReservation @TableId=45,@StartsAt='2030-01-02T19:00:00',@CustomerName=N'Ngày khác',@Phone='0981000007',@InitialStatus='Pending',@ActorUserId=1;");
        await DatabaseTool.Execute(connection, "EXEC dbo.usp_CreateManagedTableReservation @TableId=47,@StartsAt='2030-01-01T19:00:00',@CustomerName=N'Đã xác nhận',@Phone='0981000008',@InitialStatus='Confirmed',@ActorUserId=1;");
        await Reject(connection, "Confirmed reservation blocks pending reservation", "EXEC dbo.usp_CreateManagedTableReservation @TableId=47,@StartsAt='2030-01-01T19:00:00',@CustomerName=N'Đơn chờ',@Phone='0981000009',@InitialStatus='Pending',@ActorUserId=1;", 51060);
        await DatabaseTool.Execute(connection, "INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status) VALUES('S23D01',N'Ghi trực tiếp','0981000010',1,48,'2030-01-01T19:00:00','2030-01-01T20:30:00','Pending');");
        await Reject(connection, "Direct SQL insert cannot bypass table hold", "INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status) VALUES('S23D02',N'Trùng trực tiếp','0981000011',1,48,'2030-01-01T20:00:00','2030-01-01T21:30:00','Confirmed');", 51060);
        await DatabaseTool.Execute(connection, "INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status) VALUES('S23C01',N'Đã huỷ','0981000012',1,49,'2030-01-01T19:00:00','2030-01-01T20:30:00','Cancelled'),('S23N01',N'Không tới','0981000013',1,49,'2030-01-01T19:00:00','2030-01-01T20:30:00','NoShow'); EXEC dbo.usp_CreateManagedTableReservation @TableId=49,@StartsAt='2030-01-01T19:00:00',@CustomerName=N'Được nhận',@Phone='0981000014',@InitialStatus='Pending',@ActorUserId=1;");
        await Check(connection, "Managed schedule stores table, status, duration and cleanup boundary", "SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.Reservations WHERE TableId=45 AND StartsAt>='2030-01-01T19:00:00' AND Status IN ('Pending','Confirmed'))=3 AND EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=49 AND CustomerName=N'Được nhận' AND Status='Pending') THEN 1 ELSE 0 END;");
    }

    private static async Task VerifyManagedReservationConcurrencyAndSuggestions(string connection)
    {
        const string start = "2030-02-04T19:00:00";
        var race = await Task.WhenAll(Enumerable.Range(0, 10).Select(async index =>
        {
            try
            {
                await Call(connection, "usp_CreateManagedTableReservation",
                    ("TableId", 53), ("StartsAt", DateTime.Parse(start)), ("CustomerName", "Race " + index),
                    ("Phone", "09720000" + index.ToString("00")), ("InitialStatus", "Pending"), ("ActorUserId", 1));
                return true;
            }
            catch (SqlException ex) when (ex.Number == 51060) { return false; }
        }));
        if (race.Count(result => result) != 1) throw new InvalidOperationException("Concurrent same-table reservation invariant failed.");
        Console.WriteLine("PASS: 10 simultaneous managed reservations, exactly one succeeds.");

        await Task.WhenAll(
            Call(connection, "usp_CreateManagedTableReservation", ("TableId", 54), ("StartsAt", DateTime.Parse(start)), ("CustomerName", "Table 54"), ("Phone", "0972000101"), ("InitialStatus", "Pending"), ("ActorUserId", 1)),
            Call(connection, "usp_CreateManagedTableReservation", ("TableId", 55), ("StartsAt", DateTime.Parse(start)), ("CustomerName", "Table 55"), ("Phone", "0972000102"), ("InitialStatus", "Confirmed"), ("ActorUserId", 1)));
        Console.WriteLine("PASS: Concurrent reservations on different tables both succeed.");

        var partialRace = await Task.WhenAll(new[] { "2030-02-05T19:00:00", "2030-02-05T19:30:00" }.Select(async time =>
        {
            try
            {
                await Call(connection, "usp_CreateManagedTableReservation", ("TableId", 56), ("StartsAt", DateTime.Parse(time)),
                    ("CustomerName", "Partial " + time), ("Phone", time.EndsWith("00") ? "0972000103" : "0972000104"),
                    ("InitialStatus", "Pending"), ("ActorUserId", 1));
                return true;
            }
            catch (SqlException ex) when (ex.Number == 51060) { return false; }
        }));
        if (partialRace.Count(result => result) != 1) throw new InvalidOperationException("Concurrent partial-overlap invariant failed.");
        Console.WriteLine("PASS: Concurrent partial overlap, exactly one succeeds.");

        await DatabaseTool.Execute(connection, "EXEC dbo.usp_CreateManagedTableReservation @TableId=52,@StartsAt='2030-02-06T19:00:00',@CustomerName=N'Gợi ý',@Phone='0972000105',@InitialStatus='Pending',@ActorUserId=1;");
        var suggestions = await ReadSuggestions(connection, 52, new DateTime(2030, 2, 6), new TimeSpan(19, 0, 0));
        if (!suggestions.SequenceEqual(["20:45", "17:00", "21:00"]))
            throw new InvalidOperationException("Suggestions were not ordered by nearest valid start time.");
        Console.WriteLine("PASS: Suggestions respect cleanup, closing time and nearest-first order.");

        var fullDayTimes = new[] { "08:00", "09:45", "11:30", "13:15", "15:00", "16:45", "18:30", "20:15" };
        for (var index = 0; index < fullDayTimes.Length; index++)
            await DatabaseTool.Execute(connection, $"EXEC dbo.usp_CreateManagedTableReservation @TableId=57,@StartsAt='2030-02-07T{fullDayTimes[index]}:00',@CustomerName=N'Kín ngày',@Phone='09720002{index:00}',@InitialStatus='Pending',@ActorUserId=1;");
        if ((await ReadSuggestions(connection, 57, new DateTime(2030, 2, 7), new TimeSpan(19, 0, 0))).Count != 0)
            throw new InvalidOperationException("A full-day table returned an unavailable suggestion.");
        Console.WriteLine("PASS: Full-day table returns no suggestions.");
    }

    private static async Task<List<string>> ReadSuggestions(string connection, int tableId, DateTime date, TimeSpan desiredStart)
    {
        var slots = new List<string>();
        await using var cn = new SqlConnection(connection);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("dbo.usp_GetManagedTableReservationSuggestions", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TableId", tableId);
        cmd.Parameters.AddWithValue("@ReservationDate", date.Date);
        cmd.Parameters.AddWithValue("@DesiredStart", desiredStart);
        cmd.Parameters.AddWithValue("@MaxSuggestions", 3);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) slots.Add(reader.GetString(0));
        return slots;
    }

    private static async Task VerifyManagedReservationRelease(string connection)
    {
        await DatabaseTool.Execute(connection, "EXEC dbo.usp_CreateManagedTableReservation @TableId=58,@StartsAt='2030-03-01T19:00:00',@CustomerName=N'Huỷ chờ',@Phone='0972000301',@InitialStatus='Pending',@ActorUserId=1; DECLARE @id bigint=(SELECT Id FROM dbo.Reservations WHERE CustomerName=N'Huỷ chờ'); EXEC dbo.usp_CancelManagedTableReservation @ReservationId=@id,@ActorUserId=1; EXEC dbo.usp_CreateManagedTableReservation @TableId=58,@StartsAt='2030-03-01T19:00:00',@CustomerName=N'Đặt lại',@Phone='0972000302',@InitialStatus='Pending',@ActorUserId=1;");
        await Check(connection, "Cancelled pending reservation frees the table and preserves history", "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Reservations WHERE CustomerName=N'Huỷ chờ' AND Status='Cancelled') AND EXISTS(SELECT 1 FROM dbo.ReservationEvents e JOIN dbo.Reservations r ON r.Id=e.ReservationId WHERE r.CustomerName=N'Huỷ chờ' AND e.ToStatus='Cancelled') AND EXISTS(SELECT 1 FROM dbo.Reservations WHERE CustomerName=N'Đặt lại' AND Status='Pending') THEN 1 ELSE 0 END;");
        await Reject(connection, "Cancelling twice is rejected clearly", "DECLARE @id bigint=(SELECT Id FROM dbo.Reservations WHERE CustomerName=N'Huỷ chờ'); EXEC dbo.usp_CancelManagedTableReservation @ReservationId=@id,@ActorUserId=1;", 51064);

        await DatabaseTool.Execute(connection, "EXEC dbo.usp_CreateManagedTableReservation @TableId=59,@StartsAt='2030-03-01T19:00:00',@CustomerName=N'Huỷ xác nhận',@Phone='0972000303',@InitialStatus='Confirmed',@ActorUserId=1; DECLARE @id bigint=(SELECT Id FROM dbo.Reservations WHERE CustomerName=N'Huỷ xác nhận'); EXEC dbo.usp_CancelManagedTableReservation @ReservationId=@id,@ActorUserId=1; EXEC dbo.usp_CreateManagedTableReservation @TableId=59,@StartsAt='2030-03-01T19:00:00',@CustomerName=N'Đặt lại xác nhận',@Phone='0972000304',@InitialStatus='Confirmed',@ActorUserId=1;");
        await Check(connection, "Cancelled confirmed reservation frees the table", "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Reservations WHERE CustomerName=N'Huỷ xác nhận' AND Status='Cancelled') AND EXISTS(SELECT 1 FROM dbo.Reservations WHERE CustomerName=N'Đặt lại xác nhận' AND Status='Confirmed') THEN 1 ELSE 0 END;");

        await DatabaseTool.Execute(connection, "DECLARE @start datetime2(3)=DATEADD(minute,-60,DATEADD(minute,DATEDIFF(minute,0,SYSUTCDATETIME()),0)); EXEC dbo.usp_CreateManagedTableReservation @TableId=60,@StartsAt=@start,@CustomerName=N'Không tới',@Phone='0972000305',@InitialStatus='Confirmed',@ActorUserId=1; DECLARE @id bigint=(SELECT Id FROM dbo.Reservations WHERE CustomerName=N'Không tới'); EXEC dbo.usp_MarkManagedReservationNoShow @ReservationId=@id,@ActorUserId=1; EXEC dbo.usp_CreateManagedTableReservation @TableId=60,@StartsAt=@start,@CustomerName=N'Đặt sau không tới',@Phone='0972000306',@InitialStatus='Pending',@ActorUserId=1;");
        await Check(connection, "No-show after appointment frees the table immediately", "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Reservations WHERE CustomerName=N'Không tới' AND Status='NoShow' AND NoShowAt IS NOT NULL) AND EXISTS(SELECT 1 FROM dbo.Reservations WHERE CustomerName=N'Đặt sau không tới' AND Status='Pending') THEN 1 ELSE 0 END;");
        await DatabaseTool.Execute(connection, "DECLARE @start datetime2(3)=DATEADD(day,10,DATEADD(minute,DATEDIFF(minute,0,SYSUTCDATETIME()),0)); EXEC dbo.usp_CreateManagedTableReservation @TableId=44,@StartsAt=@start,@CustomerName=N'Chưa đến giờ',@Phone='0972000307',@InitialStatus='Pending',@ActorUserId=1;");
        await Reject(connection, "No-show before appointment is rejected", "DECLARE @id bigint=(SELECT Id FROM dbo.Reservations WHERE CustomerName=N'Chưa đến giờ'); EXEC dbo.usp_MarkManagedReservationNoShow @ReservationId=@id,@ActorUserId=1;", 51065);
        await Reject(connection, "No-show after cancellation is rejected", "DECLARE @id bigint=(SELECT Id FROM dbo.Reservations WHERE CustomerName=N'Huỷ chờ'); EXEC dbo.usp_MarkManagedReservationNoShow @ReservationId=@id,@ActorUserId=1;", 51064);
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

        var publicToken = CreatePublicQrToken();
        await Call(connection, "usp_RotateTableQr",
            ("TableId", 4), ("TokenHash", SHA256.HashData(Encoding.UTF8.GetBytes(publicToken))),
            ("ActorUserId", 1), ("PublicToken", publicToken));
        await Check(connection, "Public QR token stored with matching hash",
            $"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.TableQrCodes WHERE TableId=4 AND PublicToken='{publicToken}' AND RevokedAt IS NULL AND TokenHash=HASHBYTES('SHA2_256',CONVERT(varbinary(64),'{publicToken}'))) THEN 1 ELSE 0 END");

        var replacementToken = CreatePublicQrToken();
        await Call(connection, "usp_RotateTableQr",
            ("TableId", 4), ("TokenHash", SHA256.HashData(Encoding.UTF8.GetBytes(replacementToken))),
            ("ActorUserId", 1), ("PublicToken", replacementToken));
        await Check(connection, "Public QR rotation revokes old token",
            $"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.TableQrCodes WHERE TableId=4 AND PublicToken='{publicToken}' AND RevokedAt IS NOT NULL) AND EXISTS(SELECT 1 FROM dbo.TableQrCodes WHERE TableId=4 AND PublicToken='{replacementToken}' AND RevokedAt IS NULL) THEN 1 ELSE 0 END");
    }

    private static string CreatePublicQrToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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
