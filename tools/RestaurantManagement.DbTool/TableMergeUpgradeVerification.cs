using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

internal static class TableMergeUpgradeVerification
{
    internal static async Task Run(string baseConnection)
    {
        var builder=new SqlConnectionStringBuilder(baseConnection);
        var name="RestaurantManagement_Test_Upgrade_"+Guid.NewGuid().ToString("N");
        builder.InitialCatalog=name;
        var connection=builder.ConnectionString;
        try
        {
            // Reproduce the old schema and real existing order data before QuickNotes existed.
            await DatabaseTool.Migrate(connection,"046_S301QrVersionAndCleaning.sql");
            const string password="UpgradeVerification9!";
            await DatabaseTool.Seed(connection,password);
            await DatabaseTool.Execute(connection,"""
                UPDATE dbo.Users SET MustChangePassword=0;
                EXEC dbo.usp_OpenShift @Name=N'Upgrade test',@OpeningCash=0,@ActorUserId=4;
                INSERT dbo.DiningTables(AreaId,Code,MaxCapacity) VALUES(1,'UPGRADE-A',4),(1,'UPGRADE-B',4);
                DECLARE @a int=(SELECT Id FROM dbo.DiningTables WHERE Code='UPGRADE-A'),
                        @b int=(SELECT Id FROM dbo.DiningTables WHERE Code='UPGRADE-B');
                EXEC dbo.usp_OpenSession @TableId=@a,@GuestCount=2,@ActorUserId=2;
                EXEC dbo.usp_OpenSession @TableId=@b,@GuestCount=2,@ActorUserId=2;
                DECLARE @sa bigint=(SELECT SessionId FROM dbo.SessionTables WHERE TableId=@a AND ReleasedAt IS NULL),
                        @sb bigint=(SELECT SessionId FROM dbo.SessionTables WHERE TableId=@b AND ReleasedAt IS NULL);
                EXEC dbo.usp_SubmitOrder @SessionId=@sa,@RequestId='10000000-0000-0000-0000-000000000001',@ItemsJson=N'[{"MenuItemId":1,"Quantity":2,"Notes":"old main"}]',@ActorUserId=2;
                EXEC dbo.usp_SubmitOrder @SessionId=@sb,@RequestId='10000000-0000-0000-0000-000000000002',@ItemsJson=N'[{"MenuItemId":1,"Quantity":3,"Notes":"old child"}]',@ActorUserId=2;
                EXEC dbo.usp_MergeSessions @MainSessionId=@sa,@ChildSessionId=@sb,@ActorUserId=2;
                """);
            Assert(await Scalar(connection,"SELECT CASE WHEN COL_LENGTH('dbo.OrderItems','QuickNotes') IS NULL THEN 1 ELSE 0 END")==1,"Upgrade reproduces missing QuickNotes from the screenshot");
            var total=await Scalar(connection,"SELECT SUM(LineTotal) FROM dbo.OrderItems");
            var idSum=await Scalar(connection,"SELECT SUM(Id) FROM dbo.OrderItems");
            await using(var oldWeb=await Web.Start(connection,new() { ["Email__RetryPollSeconds"]="0" }))
            {
                await Login(oldWeb.Client,"kitchen",password);
                using var blocked=await oldWeb.Client.GetAsync("/Kitchen");
                var explanation=System.Net.WebUtility.HtmlDecode(await blocked.Content.ReadAsStringAsync());
                Assert(blocked.StatusCode==System.Net.HttpStatusCode.ServiceUnavailable
                    && explanation.Contains("Cần cập nhật cơ sở dữ liệu") && explanation.Contains("047_S303QuickNotes.sql"),
                    "Old schema shows an actionable upgrade message instead of throwing an invalid-column exception");
            }
            await DatabaseTool.Migrate(connection);
            await DatabaseTool.Migrate(connection);
            await Check(connection,"SELECT CASE WHEN COUNT(*)=2 AND COUNT(DISTINCT GroupLabel)=1 THEN 1 ELSE 0 END FROM dbo.vw_ActiveTableGroups",
                "Task 2 upgrade: an existing merged group receives one persistent label for both tables");

            await Check(connection,$"SELECT CASE WHEN COUNT(*)=2 AND SUM(Id)={idSum} AND SUM(LineTotal)={total} AND SUM(Quantity)=5 AND COUNT(DISTINCT OriginalTableId)=2 AND COUNT(QuickNotes)=0 THEN 1 ELSE 0 END FROM dbo.OrderItems","Upgrade retains existing line IDs, prices, totals, quantities and origins; new notes are nullable");
            await Check(connection,"SELECT CASE WHEN COUNT(DISTINCT SessionId)=1 AND COUNT(DISTINCT OriginSessionId)=2 AND COUNT(DISTINCT BatchNumber)=2 THEN 1 ELSE 0 END FROM dbo.OrderBatches","Existing merged batches move to one root without losing the source or colliding numbers");
            await DatabaseTool.Execute(connection,"""
                DECLARE @child bigint=(SELECT OriginSessionId FROM dbo.OrderBatches WHERE RequestId='10000000-0000-0000-0000-000000000002');
                EXEC dbo.usp_SubmitOrder @SessionId=@child,@RequestId='10000000-0000-0000-0000-000000000002',@ItemsJson=N'[{"MenuItemId":1,"Quantity":3}]',@ActorUserId=2;
                """);
            Assert(await Scalar(connection,"SELECT COUNT(*) FROM dbo.OrderItems")==2,"An old request still retries without duplicate items after schema upgrade");
            await using var web=await Web.Start(connection,new() { ["Email__RetryPollSeconds"]="0" });
            await Login(web.Client,"kitchen",password);
            var kitchen=await Html(web.Client,"/Kitchen");
            Assert(kitchen.Contains("old main") && kitchen.Contains("old child"),"Kitchen loads after upgrade and retains both old notes");
            using var waiterHandler=new HttpClientHandler { AllowAutoRedirect=false,CookieContainer=new System.Net.CookieContainer() };
            using var waiter=new HttpClient(waiterHandler) { BaseAddress=web.Client.BaseAddress };
            await Login(waiter,"waiter",password);
            foreach(var code in new[]{"UPGRADE-A","UPGRADE-B"})
            {
                var page=await Html(waiter,"/Ordering?tableCode="+code);
                Assert(page.Contains("UPGRADE-A</td>") && page.Contains("UPGRADE-B</td>"),"Either old table opens the complete upgraded common order");
            }
            Console.WriteLine("PASS: schema-upgrade and existing-order acceptance checks.");
        }
        finally
        {
            SqlConnection.ClearAllPools(); builder.InitialCatalog="master";
            await using var cn=new SqlConnection(builder.ConnectionString); await cn.OpenAsync();
            await using var cmd=new SqlCommand($"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END",cn);
            cmd.Parameters.AddWithValue("@name",name); await cmd.ExecuteNonQueryAsync();
        }
    }
}
