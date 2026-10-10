using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Services;
using RestaurantManagement.Web.Models.Reservations;

internal static class NoShowWarningTests
{
    public static async Task Run(Action<bool,string> check)
    {
        var b = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("RM_CONNECTION_STRING")!);
        var name = "RestaurantWarningTest_" + Guid.NewGuid().ToString("N");
        b.InitialCatalog = name; var cs = b.ConnectionString;
        async Task<object?> Sql(string sql)
        {
            await using var cn = new SqlConnection(cs); await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql,cn) { CommandTimeout=60 }; return await cmd.ExecuteScalarAsync();
        }
        async Task<int> Create(int acknowledged, string phone="0912345678")
        {
            try { await Sql($"EXEC dbo.usp_CreateManagedTableReservation @TableId=1,@StartsAt='2030-01-01T12:00:00',@CustomerName=N'Test',@Phone='{phone}',@InitialStatus='Pending',@ActorUserId=1,@AcknowledgedNoShowCount={acknowledged};"); return 0; }
            catch(SqlException e) { return e.Number; }
        }
        try
        {
            await DatabaseTool.Migrate(cs);
            await Sql("INSERT dbo.Users(RoleId,FullName,UserName,Phone) VALUES(1,N'Manager','no_show_test','0900000099'); INSERT dbo.DiningTables(AreaId,Code,MaxCapacity) SELECT TOP(1) Id,'WARN1',4 FROM dbo.Areas;");
            foreach(var count in new[]{0,2,3,5})
            {
                await Sql("DELETE dbo.ReservationNoShowHistory;");
                for(int i=1;i<=count;i++) await Sql($"INSERT dbo.ReservationNoShowHistory(ReservationId,ReservationCode,NormalizedPhone,AppointmentAt,NoShowAt) VALUES({-i},'W{i}','0912345678',SYSUTCDATETIME(),SYSUTCDATETIME());");
                check(Convert.ToInt32(await Sql("SELECT dbo.fn_RecentNoShowCount('0912345678',SYSUTCDATETIME());"))==count,$"Count {count} recent no-shows");
                check(await Create(-1)==(count>=3?51066:0),$"Creation enforces threshold for {count}");
                await Sql("UPDATE dbo.Reservations SET Status='Cancelled';");
            }
            foreach(var phone in new[]{"0912345678","+84 912 345 678","0084-912-345-678","(0912).345.678"})
            {
                check(ReservationPhoneNormalizer.Normalize(phone)=="0912345678","C# phone normalization: "+phone);
                check(Convert.ToInt32(await Sql($"SELECT dbo.fn_RecentNoShowCount('{phone}',SYSUTCDATETIME());"))==5,"SQL equivalent phone count: "+phone);
                check(new ReservationCreateViewModel{Phone=phone}.Phone=="0912345678","Public creation normalizes input");
                check(new ManagedTableReservationCreateViewModel{Phone=phone}.Phone=="0912345678","Staff creation normalizes input");
            }
            check(await Create(4)==51066,"Stale acknowledgement rejected when history increases");
            check(Convert.ToInt32(await Sql("SELECT COUNT(*) FROM dbo.Reservations WHERE Status='Pending';"))==0,"Rejected creation leaves no booking");
            check(await Create(5)==0,"Acknowledged warning allows creation");
            await Sql("UPDATE dbo.Reservations SET Status='Cancelled';");
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
            var warning=new NoShowBookingWarning(config,new EphemeralDataProtectionProvider());
            var model=new NoShowWarningInput(); await warning.Refresh(model,"+84 912345678");
            check(model.NoShowCount==5 && model.NoShowWarningToken is not null,"Lookup issues protected receipt with count");
            check(warning.AcknowledgedCount(model,"0912345678")==-1,"Unchecked receipt is not acknowledgement");
            model.NoShowAcknowledged=true;
            check(warning.AcknowledgedCount(model,"0912345678")==5,"Valid receipt accepts same normalized phone");
            check(warning.AcknowledgedCount(model,"0900000000")==-1,"Receipt cannot be reused for another phone");
            model.NoShowWarningToken="forged";
            check(warning.AcknowledgedCount(model,"0912345678")==-1,"Forged receipt rejected");
            await Sql("DELETE dbo.ReservationNoShowHistory;");
            foreach(var (offset,expected) in new[]{(-1,0),(0,1),(1,1)})
            {
                await Sql($"DELETE dbo.ReservationNoShowHistory; DECLARE @n datetime2(3)='2026-10-10T12:00:00'; INSERT dbo.ReservationNoShowHistory(ReservationId,ReservationCode,NormalizedPhone,AppointmentAt,NoShowAt) VALUES(-1,'BOUND','0912345678',@n,DATEADD(millisecond,{offset},DATEADD(day,-90,@n)));");
                check(Convert.ToInt32(await Sql("SELECT dbo.fn_RecentNoShowCount('0912345678','2026-10-10T12:00:00');"))==expected,$"90-day boundary {offset} ms");
            }
            await Sql("UPDATE dbo.ReservationNoShowHistory SET NoShowAt='2026-10-10T12:00:00';");
            check(Convert.ToInt32(await Sql("SELECT dbo.fn_RecentNoShowCount('0912345678','2026-10-10T12:00:00');"))==1,"Upper bound inclusive");
            check(Convert.ToInt32(await Sql("SELECT dbo.fn_RecentNoShowCount('0912345678','2026-10-10T11:59:59.999');"))==0,"Future events excluded");
            await Sql("DELETE dbo.ReservationNoShowHistory; INSERT dbo.ReservationNoShowHistory(ReservationId,ReservationCode,NormalizedPhone,AppointmentAt,NoShowAt) SELECT -v.n,CONCAT('WEB',v.n),'0912345678',SYSUTCDATETIME(),SYSUTCDATETIME() FROM (VALUES(1),(2),(3))v(n);");
            if(Environment.GetEnvironmentVariable("WARNING_BROWSER_TEST")=="1")
            {
                var id=Convert.ToInt64(await Sql("DECLARE @s datetime2(3)=DATEADD(minute,-20,SYSUTCDATETIME()); INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status) VALUES('RACE01',N'Race','0912345678',1,1,@s,DATEADD(minute,90,@s),'Confirmed'); SELECT SCOPE_IDENTITY();"));
                await NoShowWebTests.Run(cs,id,check,"no-show-warning-browser.cjs");
            }
        }
        finally
        {
            SqlConnection.ClearAllPools(); b.InitialCatalog="master";
            await using var cn=new SqlConnection(b.ConnectionString); await cn.OpenAsync();
            await using var cmd=new SqlCommand($"IF DB_ID('{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END",cn); await cmd.ExecuteNonQueryAsync();
        }
    }
}
