using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantManagement.Web.Services;

internal static class TableDetailsSqlTests
{
    public static async Task Run(string connectionString, IConfiguration configuration)
    {
        await Execute(connectionString, """
            CREATE TABLE dbo.Roles(Id int PRIMARY KEY,Code varchar(20));
            CREATE TABLE dbo.Users(Id int PRIMARY KEY,RoleId int,IsActive bit);
            INSERT dbo.Roles VALUES(1,'Waiter'),(2,'Kitchen');
            INSERT dbo.Users VALUES(1,1,1),(2,2,1),(3,1,0);
            CREATE TABLE dbo.Reservations(Id bigint PRIMARY KEY,TableId int,CustomerName nvarchar(100),Phone varchar(10),GuestCount int,StartsAt datetime2(3),Status varchar(20));
            CREATE TABLE dbo.DiningSessions(Id bigint PRIMARY KEY,ReservationId bigint NULL,BillingSessionId bigint NULL,GuestCount int,OpenedAt datetime2(3),Status varchar(20));
            CREATE TABLE dbo.SessionTables(SessionId bigint,TableId int,ReleasedAt datetime2(3) NULL);
            CREATE TABLE dbo.OrderBatches(Id bigint PRIMARY KEY,SessionId bigint);
            CREATE TABLE dbo.OrderItems(BatchId bigint,Quantity int,UnitPrice decimal(18,0),LineTotal AS (Quantity*UnitPrice),Status varchar(20),ChargeWhenCancelled bit);
            INSERT dbo.DiningTables VALUES(3,1,'A03',4,'Reserved',SYSUTCDATETIME(),3,1),(4,1,'A04',4,'Cleaning',SYSUTCDATETIME(),4,1);
            UPDATE dbo.DiningTables SET Status='Serving' WHERE Id=1;
            UPDATE dbo.DiningTables SET Status='Available' WHERE Id=2;
            INSERT dbo.Reservations VALUES
             (1,1,N'Khách đang ngồi','0900000001',3,DATEADD(hour,-1,SYSUTCDATETIME()),'Arrived'),
             (2,1,N'Lượt gần nhất','0900000002',4,DATEADD(hour,1,SYSUTCDATETIME()),'Confirmed'),
             (3,1,N'Lượt tiếp theo','0900000003',2,DATEADD(hour,3,SYSUTCDATETIME()),'Confirmed'),
             (4,1,N'Lượt đã qua','0900000004',2,DATEADD(hour,-3,SYSUTCDATETIME()),'Confirmed'),
             (5,3,N'Khách đặt trước','0900000005',2,DATEADD(hour,1,SYSUTCDATETIME()),'Confirmed');
            INSERT dbo.DiningSessions VALUES(1,1,NULL,3,DATEADD(minute,-20,SYSUTCDATETIME()),'Open'),(4,NULL,NULL,2,DATEADD(hour,-1,SYSUTCDATETIME()),'Closed');
            INSERT dbo.SessionTables VALUES(1,1,NULL),(4,4,NULL);
            """);
        await Execute(connectionString, """
            CREATE VIEW dbo.vw_SessionTotals AS
            SELECT s.Id AS SessionId,COALESCE(s.BillingSessionId,s.Id) AS BillingSessionId,
            COALESCE(SUM(CASE WHEN i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1 THEN i.LineTotal ELSE 0 END),0) AS Subtotal
            FROM dbo.DiningSessions s LEFT JOIN dbo.OrderBatches b ON b.SessionId=s.Id LEFT JOIN dbo.OrderItems i ON i.BatchId=b.Id
            GROUP BY s.Id,s.BillingSessionId;
            """);
        var service = new TableDetailsService(configuration, new DemoTableCatalog(), new TestHostEnvironment(), NullLogger<TableDetailsService>.Instance);
        var serving = await service.GetAsync("A01", default);
        Require(serving is { HasActiveSession: true, CurrentGuestName: "Khách đang ngồi", CurrentGuestPhone: "0900000001", CurrentGuestCount: 3, ServiceStartedAtUtc: not null, CurrentSubtotal: 0 }, "Serving details include guest, start and zero subtotal before ordering");
        Require(serving is { UpcomingReservationCount: 2, UpcomingReservation.CustomerName: "Lượt gần nhất" }, "Nearest future confirmed reservation selected; past booking excluded");
        Require(await service.GetAsync("B01", default) is { CurrentSubtotal: null, HasActiveSession: false, UpcomingReservation: null }, "Available table without reservations has no guest or subtotal");
        Require(await service.GetAsync("A03", default) is { Status: "Reserved", UpcomingReservation.CustomerName: "Khách đặt trước", CurrentGuestName: null }, "Reserved table displays upcoming reservation");
        Require(await service.GetAsync("A04", default) is { Status: "Cleaning", CurrentSubtotal: null, ServiceStartedAtUtc: null, CurrentGuestName: null }, "Cleaning table does not expose a finished session");
        await Execute(connectionString, """
            INSERT dbo.OrderBatches VALUES(1,1);
            INSERT dbo.OrderItems VALUES(1,2,50000,'Pending',0),(1,1,45000,'Served',0),(1,1,99000,'Cancelled',0),(1,1,10000,'Cancelled',1);
            """);
        Require(await service.GetAsync("A01", default) is { CurrentSubtotal: 155000 }, "Subtotal equals ordered lines and charged cancellations, excluding free cancellations");
        await Execute(connectionString, """
            INSERT dbo.DiningSessions VALUES(2,NULL,1,2,SYSUTCDATETIME(),'Open');
            INSERT dbo.SessionTables VALUES(2,2,NULL);
            UPDATE dbo.DiningTables SET Status='Serving' WHERE Id=2;
            INSERT dbo.OrderBatches VALUES(2,2);
            INSERT dbo.OrderItems VALUES(2,1,30000,'Ready',0);
            """);
        Require(await service.GetAsync("A01", default) is { CurrentSubtotal: 185000 } && await service.GetAsync("B01", default) is { CurrentSubtotal: 185000 }, "Merged tables share billing-group subtotal without double counting");
        await Execute(connectionString, "UPDATE dbo.DiningTables SET Status='Cleaning' WHERE Id=1;");
        Require(await service.GetAsync("A01", default) is { Status: "Cleaning", CurrentGuestName: null, CurrentSubtotal: null, UpcomingReservationCount: 2 }, "State change removes current guest and subtotal while retaining future bookings");
        Require(await service.GetAsync("X99", default) is null, "Unknown SQL table returns no details");
        Require(await service.GetForUserAsync("A01", 1, default) is not null, "Current active waiter can read private details");
        foreach (var userId in new[] { 2,3,99 })
        {
            try { await service.GetForUserAsync("A01", userId, default); throw new Exception("Unauthorized user received private details"); }
            catch (SqlException exception) when (exception.Number == 51001) { Console.WriteLine("PASS: Private details rejected for user " + userId); }
        }
        await Execute(connectionString,"UPDATE dbo.Users SET RoleId=2 WHERE Id=1;");
        try { await service.GetForUserAsync("A01", 1, default); throw new Exception("Old role claim was trusted"); }
        catch (SqlException exception) when (exception.Number == 51001) { Console.WriteLine("PASS: Revoked waiter role cannot read private details"); }
    }
    private static async Task Execute(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await new SqlCommand(sql, connection).ExecuteNonQueryAsync();
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
