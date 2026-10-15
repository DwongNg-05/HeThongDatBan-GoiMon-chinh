using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

internal static class TableUnmergeVerification
{
    private sealed record Pair(long A,long B,long Main,long Child);
    internal static async Task Run(string baseConnection)
    {
        var builder=new SqlConnectionStringBuilder(baseConnection);
        var name="RestaurantManagement_Test_Unmerge_"+Guid.NewGuid().ToString("N");
        builder.InitialCatalog=name; var connection=builder.ConnectionString;
        try
        {
            await DatabaseTool.Migrate(connection); await DatabaseTool.Migrate(connection);
            const string password="UnmergeVerification9!";
            await DatabaseTool.Seed(connection,password);
            await DatabaseTool.Execute(connection,"UPDATE dbo.Users SET MustChangePassword=0; EXEC dbo.usp_OpenShift @Name=N'Unmerge test',@OpeningCash=0,@ActorUserId=4;");
            async Task<string> Text(string sql)
            {
                await using var cn=new SqlConnection(connection); await cn.OpenAsync();
                await using var cmd=new SqlCommand(sql,cn); return Convert.ToString(await cmd.ExecuteScalarAsync())!;
            }
            async Task Refuse(string sql,int number,string label)
            {
                try { await DatabaseTool.Execute(connection,sql); throw new Exception("Expected refusal: "+label); }
                catch(SqlException ex) when(ex.Number==number) { Assert(true,label); }
            }
            async Task Submit(long session,int qty,Guid? request=null) => await DatabaseTool.Execute(connection,$$"""
                EXEC dbo.usp_SubmitOrder @SessionId={{session}},@RequestId='{{request ?? Guid.NewGuid()}}',@ItemsJson=N'[{"MenuItemId":1,"Quantity":{{qty}},"Notes":"preserve note","QuickNotes":"ít cay"}]',@ActorUserId=2;
                """);
            async Task<Pair> Setup(string tag,bool mixed=false)
            {
                await DatabaseTool.Execute(connection,$"INSERT dbo.DiningTables(AreaId,Code,MaxCapacity) VALUES(1,'U3-{tag}-A',4),(1,'U3-{tag}-B',4);");
                var a=await Scalar(connection,$"SELECT Id FROM dbo.DiningTables WHERE Code='U3-{tag}-A'");
                var b=await Scalar(connection,$"SELECT Id FROM dbo.DiningTables WHERE Code='U3-{tag}-B'");
                await DatabaseTool.Execute(connection,$"EXEC dbo.usp_OpenSession @TableId={a},@GuestCount=2,@ActorUserId=2; EXEC dbo.usp_OpenSession @TableId={b},@GuestCount=2,@ActorUserId=2; EXEC dbo.usp_SetTableAdjacency @TableId={a},@AdjacentTableId={b},@IsAdjacent=1,@ActorUserId=1;");
                var main=await Scalar(connection,$"SELECT SessionId FROM dbo.SessionTables WHERE TableId={a} AND ReleasedAt IS NULL");
                var child=await Scalar(connection,$"SELECT SessionId FROM dbo.SessionTables WHERE TableId={b} AND ReleasedAt IS NULL");
                await Submit(main,2); await Submit(child,3);
                await DatabaseTool.Execute(connection,$"EXEC dbo.usp_MergeSessions @MainSessionId={main},@ChildSessionId={child},@ActorUserId=2;");
                await Submit(main,1); await Submit(child,4);
                if(mixed)
                    await DatabaseTool.Execute(connection,$"""
                        DECLARE @batch bigint=(SELECT TOP(1) Id FROM dbo.OrderBatches WHERE SessionId={main} AND OriginSessionId={main} ORDER BY Id);
                        INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,Notes,EstimatedPrepMinutes)
                        SELECT @batch,Id,{b},Name,Unit,Price,1,N'legacy mixed batch',EstimatedPrepMinutes FROM dbo.MenuItems WHERE Id=1;
                        """);
                return new(a,b,main,child);
            }
            async Task<string> Version(long session)
            {
                await using var cn=new SqlConnection(connection); await cn.OpenAsync();
                await using var cmd=new SqlCommand("SELECT RowVersion FROM dbo.DiningSessions WHERE Id=@id",cn);
                cmd.Parameters.AddWithValue("@id",session);
                return Convert.ToHexString((byte[])(await cmd.ExecuteScalarAsync())!);
            }
            Task<string> Fingerprint(Pair p) => Text($"""
                SELECT i.Id,i.OriginalTableId,i.Quantity,i.UnitPrice,i.ItemName,i.Unit,i.Notes,i.QuickNotes,i.Status,i.SubmittedAt
                FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
                WHERE b.SessionId IN({p.Main},{p.Child}) ORDER BY i.Id FOR JSON PATH,INCLUDE_NULL_VALUES;
                """);
            async Task Serve(Pair p) => await DatabaseTool.Execute(connection,$"""
                DECLARE @id bigint;
                DECLARE lines CURSOR LOCAL STATIC READ_ONLY FOR SELECT i.Id FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId IN({p.Main},{p.Child});
                OPEN lines; FETCH NEXT FROM lines INTO @id;
                WHILE @@FETCH_STATUS=0 BEGIN
                 EXEC dbo.usp_TransitionOrderItem @OrderItemId=@id,@ToStatus='Preparing',@ActorUserId=3;
                 EXEC dbo.usp_TransitionOrderItem @OrderItemId=@id,@ToStatus='Ready',@ActorUserId=3;
                 EXEC dbo.usp_TransitionOrderItem @OrderItemId=@id,@ToStatus='Served',@ActorUserId=2;
                 FETCH NEXT FROM lines INTO @id;
                END; CLOSE lines; DEALLOCATE lines;
                """);
            string Checkout(long session,string version,Guid request) => $"EXEC dbo.usp_Checkout @SessionId={session},@ExpectedSessionVersion=0x{version},@RequestId='{request}',@Method='Cash',@CashReceived=10000000,@ActorUserId=4;";
            await using var web=await Web.Start(connection,new() { ["Email__RetryPollSeconds"]="0" });
            await Login(web.Client,"waiter",password);
            using var cashierHandler=new HttpClientHandler { AllowAutoRedirect=false,CookieContainer=new CookieContainer() };
            using var cashier=new HttpClient(cashierHandler) { BaseAddress=web.Client.BaseAddress };
            await Login(cashier,"cashier",password);
            using(var forbidden=await cashier.GetAsync("/Ordering/Unmerge?tableId=999999"))
                Assert(forbidden.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,"Cashier cannot use the waiter unmerge screen");
            static string Value(string html,string field) => Regex.Match(html,$"name=\"{field}\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            async Task<HttpResponseMessage> Post(Pair p,string page,bool confirm=true,bool token=true) => await web.Client.PostAsync("/Ordering/Unmerge",Form(
                ("tableId",p.B.ToString()),("childSessionId",p.Child.ToString()),("mergeId",Value(page,"mergeId")),
                ("mainVersion",Value(page,"mainVersion")),("childVersion",Value(page,"childVersion")),("confirmed",confirm.ToString()),
                ("__RequestVerificationToken",token ? Token(page) : "")));

            foreach(var awaiting in new[]{false,true})
            {
                var p=await Setup(awaiting ? "WAIT" : "OPEN",mixed:true);
                if(!awaiting)
                    await DatabaseTool.Execute(connection,$"""
                        DECLARE @preparing bigint=(SELECT MIN(i.Id) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId={p.Main} AND i.OriginalTableId={p.A});
                        DECLARE @cancel bigint=(SELECT MIN(i.Id) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId={p.Main} AND i.OriginalTableId={p.B});
                        EXEC dbo.usp_TransitionOrderItem @OrderItemId=@preparing,@ToStatus='Preparing',@ActorUserId=3;
                        EXEC dbo.usp_CancelOrderItem @OrderItemId=@cancel,@Reason='ChangedMind',@ActorUserId=2;
                        """);
                if(awaiting)
                {
                    var stalePage=await Html(web.Client,$"/Ordering/Unmerge?tableId={p.B}");
                    await DatabaseTool.Execute(connection,$"EXEC dbo.usp_SetPaymentState @SessionId={p.Main},@Awaiting=1,@ActorUserId=2;");
                    using(var stale=await Post(p,stalePage)) Assert(stale.StatusCode==HttpStatusCode.Redirect,"A changed payment state requires reloading the unmerge confirmation");
                    Assert(await Scalar(connection,$"SELECT COUNT(*) FROM dbo.DiningSessions WHERE Id={p.Child} AND BillingSessionId={p.Main}")==1,
                        "Stale unmerge confirmation cannot change the group");
                }
                var page=await Html(web.Client,$"/Ordering/Unmerge?tableId={p.B}");
                Assert(page.Contains("U3-") && page.Contains("Tạm tính theo bàn gốc") && page.Contains("Đang phục vụ"),"Unmerge confirmation lists both tables, sessions, individual totals and the agreed resulting state");
                using(var csrf=await Post(p,page,token:false)) Assert(csrf.StatusCode==HttpStatusCode.BadRequest,"Unmerge requires antiforgery protection");
                using(var missing=await Post(p,page,confirm:false)) Assert(missing.StatusCode==HttpStatusCode.Redirect,"Unmerge requires explicit confirmation");
                Assert(await Scalar(connection,$"SELECT COUNT(*) FROM dbo.DiningSessions WHERE Id={p.Child} AND BillingSessionId={p.Main}")==1,"Unconfirmed requests cannot change the group");
                var before=await Fingerprint(p);
                var oldLabel=await Text($"SELECT TableGroupLabel FROM dbo.DiningSessions WHERE Id={p.Main}");
                using(var response=await Post(p,page)) Assert(response.StatusCode==HttpStatusCode.Redirect,"Waiter successfully unmerges through the real confirmation form");
                Assert(before==await Fingerprint(p),"Unmerge preserves every line ID, origin, quantity, price, note, status and submission time, including a mixed-origin batch");
                await Check(connection,$"""
                    SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
                     JOIN dbo.SessionTables st ON st.SessionId=b.SessionId AND st.ReleasedAt IS NULL
                     WHERE b.SessionId IN({p.Main},{p.Child}) AND i.OriginalTableId<>st.TableId)
                     AND (SELECT COUNT(*) FROM dbo.DiningSessions WHERE Id IN({p.Main},{p.Child}) AND Status='Open' AND PaymentRequestedAt IS NULL AND BillingSessionId IS NULL AND TableGroupLabel IS NULL)=2
                     THEN 1 ELSE 0 END;
                    ""","All old and extra items return to their actual origin; both sessions reopen and the payment request/relationship/label are cleared");
                await Check(connection,$"SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.vw_ActiveTableGroups WHERE TableId IN({p.A},{p.B})) AND EXISTS(SELECT 1 FROM dbo.SessionMerges WHERE MainSessionId={p.Main} AND ChildSessionId={p.Child} AND UndoneAt IS NOT NULL AND UndoneBy=2) THEN 1 ELSE 0 END;","Group disappears from the map while the undo audit is retained");
                foreach(var table in new[]{p.A,p.B})
                {
                    var ownPage=await Html(web.Client,$"/Ordering?tableId={table}");
                    Assert(!ownPage.Contains("data-table-group=") && !ownPage.Contains("legacy mixed batch"),"Each table is a standalone serving session after unmerge");
                }
                await Check(connection,$"""
                    SELECT CASE WHEN (SELECT Subtotal FROM dbo.vw_SessionTotals WHERE SessionId={p.Main})=(SELECT SUM(CASE WHEN i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1 THEN i.LineTotal ELSE 0 END) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId={p.Main})
                    AND (SELECT Subtotal FROM dbo.vw_SessionTotals WHERE SessionId={p.Child})=(SELECT SUM(CASE WHEN i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1 THEN i.LineTotal ELSE 0 END) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId={p.Child}) THEN 1 ELSE 0 END;
                    ""","Provisional bills recalculate exact independent totals");
                await Refuse($"EXEC dbo.usp_UnmergeSession @ChildSessionId={p.Child},@ActorUserId=2;",51160,"Duplicate unmerge is refused");
                await DatabaseTool.Execute(connection,$"EXEC dbo.usp_MergeSessions @MainSessionId={p.Main},@ChildSessionId={p.Child},@ActorUserId=2;");
                Assert(before==await Fingerprint(p) && oldLabel!=await Text($"SELECT TableGroupLabel FROM dbo.DiningSessions WHERE Id={p.Main}"),"Regrouping after unmerge preserves all items and creates a new group label");
                await Refuse($"EXEC dbo.usp_UnmergeSession @ChildSessionId={p.Child},@ExpectedMergeId={Value(page,"mergeId")},@ActorUserId=2;",51161,"An old confirmation cannot undo a newly regrouped group");
            }

            // Test both deterministic orderings and a genuine simultaneous race.
            foreach(var mode in new[]{"UNDOFIRST","PAYFIRST","RACE"})
            {
                Console.WriteLine("Unmerge/checkout ordering: "+mode);
                var p=await Setup(mode); Console.WriteLine("Race fixture created"); await Serve(p); Console.WriteLine("Race fixture served");
                await DatabaseTool.Execute(connection,$"EXEC dbo.usp_SetPaymentState @SessionId={p.Main},@Awaiting=1,@ActorUserId=2;");
                var mv=await Version(p.Main); var cv=await Version(p.Child);
                Assert(mv.Length==16 && mv.All(Uri.IsHexDigit) && cv.Length==16 && cv.All(Uri.IsHexDigit),"Checkout snapshots have valid binary versions");
                var merge=await Scalar(connection,$"SELECT Id FROM dbo.SessionMerges WHERE MainSessionId={p.Main} AND UndoneAt IS NULL");
                var undo=$"EXEC dbo.usp_UnmergeSession @ChildSessionId={p.Child},@ExpectedMergeId={merge},@ExpectedMainVersion=0x{mv},@ExpectedChildVersion=0x{cv},@ActorUserId=2;";
                var pay=Checkout(p.Main,mv,Guid.NewGuid());
                if(mode=="UNDOFIRST")
                {
                    var cashierPage=await Html(cashier,"/Cashier");
                    var oldRow=Regex.Match(cashierPage,$"<tr data-session-id=\"{p.Main}\">[\\s\\S]*?</tr>").Value;
                    Assert(Value(oldRow,"sessionVersion")==mv,"Cashier form captures the exact displayed session version");
                    await DatabaseTool.Execute(connection,undo);
                    using(var rejected=await cashier.PostAsync("/Cashier/Checkout",Form(("sessionId",p.Main.ToString()),("requestId",Guid.NewGuid().ToString()),("method","Cash"),("cashReceived","10000000"),("sessionVersion",mv),("__RequestVerificationToken",Token(cashierPage)))))
                        Assert(rejected.StatusCode==HttpStatusCode.Redirect,"Stale cashier form returns to reload current table totals");
                    Assert((await Html(cashier,"/Cashier")).Contains("vừa thay đổi") && await Scalar(connection,$"SELECT COUNT(*) FROM dbo.Invoices WHERE SessionId={p.Main}")==0,
                        "Stale cashier HTTP checkout explains the unmerge and creates no invoice");
                    await Refuse(pay,51165,"Checkout of an old group snapshot is refused after unmerge");
                    await Refuse($"EXEC dbo.usp_Checkout @SessionId={p.Main},@RequestId='{Guid.NewGuid()}',@Method='Cash',@CashReceived=10000000,@ActorUserId=4;",51165,"An unversioned old checkout cannot pay a formerly grouped root after unmerge");
                    var request=Guid.NewGuid(); var freshPay=Checkout(p.Main,await Version(p.Main),request);
                    await DatabaseTool.Execute(connection,freshPay); await DatabaseTool.Execute(connection,freshPay);
                    await DatabaseTool.Execute(connection,Checkout(p.Child,await Version(p.Child),Guid.NewGuid()));
                    await Check(connection,$"SELECT CASE WHEN COUNT(*)=2 AND SUM(Total)=(SELECT SUM(i.LineTotal) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId WHERE b.SessionId IN({p.Main},{p.Child})) THEN 1 ELSE 0 END FROM dbo.Invoices WHERE SessionId IN({p.Main},{p.Child});","Fresh checkout pays each split table once with exact totals; retry does not duplicate an invoice");
                }
                else if(mode=="PAYFIRST")
                {
                    await DatabaseTool.Execute(connection,pay);
                    await Refuse(undo,51160,"A paid group cannot be unmerged");
                    await Check(connection,$"SELECT CASE WHEN COUNT(*)=1 THEN 1 ELSE 0 END FROM dbo.Invoices WHERE SessionId={p.Main};","Rejected unmerge leaves the paid group invoice intact");
                }
                else
                {
                    async Task<bool> Attempt(string sql)
                    {
                        try { await DatabaseTool.Execute(connection,sql); return true; }
                        catch(SqlException ex) when(ex.Number is 51160 or 51161 or 51165) { return false; }
                    }
                    var outcomes=await Task.WhenAll(Attempt(undo),Attempt(pay));
                    Assert(outcomes.Count(x=>x)==1,"Concurrent unmerge and checkout have exactly one winner");
                }
            }
            Console.WriteLine("PASS: all unmerge acceptance checks.");
        }
        catch(SqlException ex)
        {
            Console.Error.WriteLine($"SQL verification detail: procedure={ex.Procedure}, line={ex.LineNumber}, number={ex.Number}");
            throw;
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
