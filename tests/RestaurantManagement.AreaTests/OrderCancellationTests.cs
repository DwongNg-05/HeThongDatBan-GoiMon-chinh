using Microsoft.Data.SqlClient;
using RestaurantManagement.DbTool;
using RestaurantManagement.Web.Services;

internal static class OrderCancellationTests
{
    internal static async Task Run(Action<bool,string> check)
    {
        var builder=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("RM_CONNECTION_STRING") ?? "Server=.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True");
        var database="S305_Test_"+Guid.NewGuid().ToString("N"); builder.InitialCatalog=database;
        var connection=builder.ConnectionString;
        async Task Exec(string sql) => await DatabaseTool.Execute(connection,sql);
        async Task<long> Scalar(string sql) { await using var cn=new SqlConnection(connection); await cn.OpenAsync(); return Convert.ToInt64(await new SqlCommand(sql,cn).ExecuteScalarAsync()); }
        async Task Reject(Func<Task> action,int number,string label) { try { await action(); throw new Exception("Expected rejection: "+label); } catch(SqlException ex) { check(ex.Number==number,label); } }
        try
        {
            await DatabaseTool.Migrate(connection);
            // Fresh test DB only: signed/EXECUTE AS procedures need a resolvable owner.
            await Exec($"ALTER AUTHORIZATION ON DATABASE::[{database}] TO sa;");
            await DatabaseTool.Seed(connection,"S305TestPassword9!");
            await Exec("UPDATE dbo.Users SET MustChangePassword=0;");
            await Exec("EXEC dbo.usp_OpenShift @Name=N'Test S305',@OpeningCash=100000,@ActorUserId=4; EXEC dbo.usp_OpenSession @TableId=1,@GuestCount=2,@ActorUserId=2;");
            var store=new OrderWorkflowStore(connection);
            async Task<long> NewItem() {
                await Exec($"EXEC dbo.usp_SubmitOrder @SessionId=1,@RequestId='{Guid.NewGuid()}',@ItemsJson=N'[{{\"MenuItemId\":1,\"Quantity\":3}}]',@ActorUserId=2;");
                return await Scalar("SELECT MAX(Id) FROM dbo.OrderItems");
            }
            foreach(var reason in new[]{"ChangedMind","Mistake","SoldOut"})
            {
                var id=await NewItem(); var before=(await store.Read(2,false,default)).Sessions.Single().Subtotal;
                check(await store.Cancel(id,reason,2,default),"S3-05 cancel Pending: "+reason);
                var after=await store.Read(2,false,default);
                check(after.Sessions.Single().Subtotal==before-75000 && after.Items.Single(i=>i.Id==id) is { Status:"Cancelled",Quantity:3,ChargeWhenCancelled:false },"S3-05 full quantity cancelled, subtotal reduced once");
                check(!(await store.Read(3,true,default)).Items.Any(i=>i.Id==id),"S3-05 cancelled line absent from kitchen immediately");
                check(await Scalar($"SELECT COUNT(*) FROM dbo.OrderItemEvents WHERE OrderItemId={id} AND ToStatus='Cancelled' AND ActorUserId=2 AND Reason='{reason}' AND OccurredAt IS NOT NULL")==1,"S3-05 audit actor, item, reason and time");
                check(!await store.Cancel(id,reason,2,default),"S3-05 retry idempotent");
                check((await store.Read(2,false,default)).Sessions.Single().Subtotal==after.Sessions.Single().Subtotal,"S3-05 retry does not reduce subtotal twice");
            }
            var invalid=await NewItem();
            await Reject(()=>Exec($"EXEC dbo.usp_CancelPendingOrderItem {invalid},NULL,2"),51501,"S3-05 SQL rejects missing reason");
            await Reject(()=>store.Cancel(invalid,"ManagerOverride",2,default),51501,"S3-05 rejects unsupported reason");
            await Reject(()=>store.Cancel(invalid,"Mistake",3,default),51001,"S3-05 kitchen cannot cancel");
            await Reject(()=>store.Cancel(invalid,"Mistake",4,default),51001,"S3-05 cashier cannot cancel");
            await store.Start(invalid,3,default);
            await Reject(()=>store.Cancel(invalid,"Mistake",2,default),51503,"S3-05 stale dialog after kitchen start rejected");
            await Reject(()=>store.Cancel(invalid,"Mistake",1,default),51503,"S3-05 staff endpoint does not allow manager to cancel preparing");
            var duplicate=await NewItem();
            var retries=await Task.WhenAll(Enumerable.Range(0,5).Select(_=>store.Cancel(duplicate,"Mistake",2,default)));
            check(retries.Count(changed=>changed)==1 && await Scalar($"SELECT COUNT(*) FROM dbo.OrderItemEvents WHERE OrderItemId={duplicate} AND ToStatus='Cancelled'")==1,"S3-05 concurrent duplicate creates one cancellation event");
            for(var attempt=0;attempt<5;attempt++)
            {
                var id=await NewItem();
                async Task<bool> TryCancel(){try{return await store.Cancel(id,"SoldOut",2,default);}catch(SqlException ex) when(ex.Number==51503){return false;}}
                async Task<bool> TryStart(){try{await store.Start(id,3,default);return true;}catch(SqlException ex) when(ex.Number==51030){return false;}}
                var results=await Task.WhenAll(TryCancel(),TryStart());
                check(results.Count(success=>success)==1,"S3-05 cancellation vs kitchen start has exactly one winner");
            }
            var httpItem=await NewItem();
            await OrderCancellationHttpTests.Run(connection,httpItem,check);
            var closed=await NewItem(); await Exec("UPDATE dbo.DiningSessions SET Status='AwaitingPayment' WHERE Id=1;");
            await Reject(()=>store.Cancel(closed,"Mistake",2,default),51504,"S3-05 rejects awaiting-payment session");
        }
        finally
        {
            SqlConnection.ClearAllPools(); builder.InitialCatalog="master";
            await using var cn=new SqlConnection(builder.ConnectionString);await cn.OpenAsync();
            await new SqlCommand($"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END",cn).ExecuteNonQueryAsync();
        }
    }
}
