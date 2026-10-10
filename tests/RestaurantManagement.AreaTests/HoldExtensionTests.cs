using Microsoft.Data.SqlClient;
using RestaurantManagement.DbTool;

internal static class HoldExtensionTests
{
    public static async Task Run(Action<bool,string> check)
    {
        var b = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set RM_CONNECTION_STRING for the test SQL server."));
        var name = "RestaurantHoldTest_"+Guid.NewGuid().ToString("N");
        b.InitialCatalog=name; var cs=b.ConnectionString;
        async Task<object?> Sql(string sql)
        {
            await using var cn=new SqlConnection(cs); await cn.OpenAsync();
            await using var cmd=new SqlCommand(sql,cn) {CommandTimeout=60};
            return await cmd.ExecuteScalarAsync();
        }
        async Task<int> Call(long id,string proc="usp_ExtendReservationHold",int actor=1)
        {
            try { await Sql($"EXEC dbo.{proc} @ReservationId={id},@ActorUserId={actor};");return 0; }
            catch(SqlException e) {return e.Number;}
        }
        int sequence=0;
        async Task<long> Booking(int minutes=20,string status="Confirmed")
        {
            var code="H"+(++sequence).ToString("D5");
            return Convert.ToInt64(await Sql($"""
                INSERT dbo.DiningTables(AreaId,Code,MaxCapacity,Status) SELECT TOP(1) Id,'{code}',4,'Reserved' FROM dbo.Areas;
                DECLARE @t int=SCOPE_IDENTITY(),@s datetime2(3)=DATEADD(minute,-{minutes},SYSUTCDATETIME());
                INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status)
                VALUES('{code}',N'Gia hạn thử','0912345678',2,@t,@s,DATEADD(minute,90,@s),'{status}');
                SELECT CONVERT(bigint,SCOPE_IDENTITY());
                """));
        }
        async Task Expire(long id) => await Sql($"UPDATE dbo.Reservations SET StartsAt=DATEADD(minute,-31,SYSUTCDATETIME()),HoldExtendedUntil=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id={id};");
        try
        {
            await DatabaseTool.Migrate(cs);
            await Sql("INSERT dbo.Users(RoleId,FullName,UserName,Phone) VALUES(1,N'Manager','no_show_test','0900000099'),(2,N'Waiter','waiter_hold_test','0900000098'),(3,N'Kitchen','kitchen_hold_test','0900000097');");
            var early=await Booking(14);
            check(await Call(early)==51065,"Cannot extend before warning threshold");
            var expired=await Booking(31);
            check(await Call(expired)==51065,"Cannot extend after appointment +30 minutes");
            check(Convert.ToInt32(await Sql($"SELECT ExtensionCount FROM dbo.Reservations WHERE Id={expired};"))==0,"Expired attempt does not consume extension");
            foreach(var status in new[]{"Pending","Rejected","Cancelled","NoShow","Arrived"})
                check(await Call(await Booking(status:status))==51064,"Reject extension for "+status);
            var held=await Booking();
            check(await Call(held,actor:3)==51001,"Kitchen cannot extend");
            check(await Call(held,actor:2)==0,"Waiter can extend late confirmed booking");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.Reservations WHERE Id={held} AND ExtensionCount=1 AND HoldExtendedUntil=DATEADD(minute,30,StartsAt) AND DATEDIFF(minute,StartsAt,EndsAt)=90;"))==1,"Deadline is appointment +30, booking interval remains 90 minutes");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.v_NoShowAlerts WHERE Id={held};"))==0,"Warning disappears during extension");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.v_ReservationHoldState WHERE Id={held} AND IsOverdue=0 AND CanExtend=0 AND HoldExtendedUntil IS NOT NULL;"))==1,"Active extension stays visible with deadline and disabled extension");
            check((string?)await Sql($"SELECT Status FROM dbo.DiningTables WHERE Id=(SELECT TableId FROM dbo.Reservations WHERE Id={held});")=="Reserved","Extension keeps table reserved");
            check(await Call(held)==51015,"Second extension rejected");
            check(await Call(held,"usp_RecordReservationNoShow")==51065,"Stale no-show action cannot release extended hold");
            check(await Call(held,"usp_MarkManagedReservationNoShow")==51065,"Legacy no-show entry point respects extension");
            foreach(var (delta,expected) in new[]{(-1,0),(0,1),(1,1)})
                check(Convert.ToInt32(await Sql($"DECLARE @s datetime2(3)='2026-10-10T11:00:00',@h datetime2(3)='2026-10-10T11:30:00'; SELECT dbo.fn_IsReservationHoldDue('Confirmed',NULL,@s,@h,DATEADD(millisecond,{delta},@h));"))==expected,$"Extended deadline boundary {delta}ms");
            await Expire(held);
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.v_NoShowAlerts WHERE Id={held};"))==1,"Warning returns after extension expires");
            check(await Call(held)==51015,"Cannot extend again even after expiry");
            var marked=await Task.WhenAll(Call(held,"usp_RecordReservationNoShow"),Call(held,"usp_RecordReservationNoShow"));
            check(marked.Count(x=>x==0)==1 && marked.Count(x=>x==51064)==1,"No-show after expiry succeeds exactly once under concurrency");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.ReservationNoShowHistory WHERE ReservationId={held};"))==1,"One history row after extended no-show");
            check((string?)await Sql($"SELECT Status FROM dbo.DiningTables WHERE Id=(SELECT TableId FROM dbo.Reservations WHERE Id={held});")=="Available","Extended no-show releases table");
            var race=await Booking();
            var racers=await Task.WhenAll(Call(race,actor:1),Call(race,actor:2));
            check(racers.Count(x=>x==0)==1 && racers.Count(x=>x==51015)==1,"Two employees: exactly one extension succeeds");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.ReservationEvents WHERE ReservationId={race} AND Reason=N'Gia hạn giữ bàn đến giờ hẹn cộng 30 phút';"))==1,"Concurrent extension records one event");
            var arrival=await Booking();
            check(await Call(arrival)==0,"Extend before guest arrival");
            await Sql($"INSERT dbo.Shifts(Name,BusinessDate,OpenedBy) VALUES(N'Test',CONVERT(date,SYSUTCDATETIME()),1); DECLARE @t int=(SELECT TableId FROM dbo.Reservations WHERE Id={arrival}); EXEC dbo.usp_OpenSession @t,2,1,{arrival};");
            await Expire(arrival);
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.v_ReservationHoldState WHERE Id={arrival};"))==0,"Arrived during extension never reappears in warning");
            check(await Call(arrival,"usp_RecordReservationNoShow")==51064,"Arrived customer protected after expiry");
            check((string?)await Sql($"SELECT Status FROM dbo.DiningTables WHERE Id=(SELECT TableId FROM dbo.Reservations WHERE Id={arrival});")=="Serving","Arrived table stays serving");
            var conflict=await Booking();
            var concurrent=await Task.WhenAll(Call(conflict),Call(conflict,"usp_RecordReservationNoShow"));
            check(concurrent.Count(x=>x==0)==1,"Extend vs no-show: exactly one operation succeeds");
            var state=(string?)await Sql($"SELECT Status FROM dbo.Reservations WHERE Id={conflict};");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.ReservationNoShowHistory WHERE ReservationId={conflict};"))==(state=="NoShow"?1:0),"Extend/no-show race has consistent history");
            var rollback=await Booking();
            await Sql("CREATE TRIGGER dbo.TestHoldFailure ON dbo.ReservationEvents AFTER INSERT AS THROW 51999,'Test failure',1;");
            check(await Call(rollback)==51999,"Injected event failure rejects extension");
            check(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM dbo.Reservations WHERE Id={rollback} AND ExtensionCount=0 AND HoldExtendedUntil IS NULL;"))==1,"Extension failure rolls back count and deadline together");
            await Sql("DROP TRIGGER dbo.TestHoldFailure;");
            await Sql("CREATE USER HoldReader WITHOUT LOGIN; ALTER ROLE restaurant_app ADD MEMBER HoldReader;");
            check(Convert.ToInt32(await Sql("EXECUTE AS USER='HoldReader'; SELECT COUNT(*) FROM dbo.v_ReservationHoldState; REVERT;"))>0,"Restricted app role can read hold state");
            if(Environment.GetEnvironmentVariable("HOLD_BROWSER_TEST")=="1")
            {
                await Sql("UPDATE dbo.Reservations SET Status='Cancelled' WHERE Status IN ('Pending','Confirmed');");
                var browserId=await Booking(29);
                await NoShowWebTests.Run(cs,browserId,check,"hold-extension-browser.cjs");
            }
        }
        finally
        {
            SqlConnection.ClearAllPools(); b.InitialCatalog="master";
            await using var cn=new SqlConnection(b.ConnectionString); await cn.OpenAsync();
            await using var cmd=new SqlCommand($"IF DB_ID('{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END",cn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
