using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

internal static class TableMergeVerification
{
    internal static async Task Run(string baseConnection)
    {
        var builder=new SqlConnectionStringBuilder(baseConnection);
        var name="RestaurantManagement_Test_Merge_"+Guid.NewGuid().ToString("N");
        builder.InitialCatalog=name;
        var connection=builder.ConnectionString;
        try
        {
            await DatabaseTool.Migrate(connection);
            await DatabaseTool.Migrate(connection);
            const string password="MergeVerification9!";
            await DatabaseTool.Seed(connection,password);
            await DatabaseTool.Execute(connection,"""
                UPDATE dbo.Users SET MustChangePassword=0;
                EXEC dbo.usp_OpenShift @Name=N'Merge test',@OpeningCash=0,@ActorUserId=4;
                INSERT dbo.DiningTables(AreaId,Code,MaxCapacity) VALUES(1,'MERGE-A',4),(1,'MERGE-B',4),(1,'MERGE-C',4),(2,'MERGE-D',4);
                DECLARE @t int;
                SELECT @t=Id FROM dbo.DiningTables WHERE Code='MERGE-A';
                EXEC dbo.usp_OpenSession @TableId=@t,@GuestCount=2,@ActorUserId=2;
                SELECT @t=Id FROM dbo.DiningTables WHERE Code='MERGE-B';
                EXEC dbo.usp_OpenSession @TableId=@t,@GuestCount=2,@ActorUserId=2;
                SELECT @t=Id FROM dbo.DiningTables WHERE Code='MERGE-C';
                EXEC dbo.usp_OpenSession @TableId=@t,@GuestCount=2,@ActorUserId=2;
                SELECT @t=Id FROM dbo.DiningTables WHERE Code='MERGE-D';
                EXEC dbo.usp_OpenSession @TableId=@t,@GuestCount=2,@ActorUserId=2;
                """);
            async Task<long> Table(string code) => await Scalar(connection,$"SELECT Id FROM dbo.DiningTables WHERE Code='MERGE-{code}'");
            async Task<long> Session(long table) => await Scalar(connection,$"SELECT SessionId FROM dbo.SessionTables WHERE TableId={table} AND ReleasedAt IS NULL");
            var a=await Table("A"); var b=await Table("B"); var c=await Table("C"); var d=await Table("D");
            var sa=await Session(a); var sb=await Session(b); var sc=await Session(c); var sd=await Session(d);
            string Merge(long x,long y) => $"EXEC dbo.usp_MergeSessions @MainSessionId={x},@ChildSessionId={y},@ActorUserId=2;";
            async Task Refuse(string sql,int error,string label)
            {
                try { await DatabaseTool.Execute(connection,sql); throw new Exception("Expected rejection: "+label); }
                catch(SqlException ex) when(ex.Number==error) { Assert(true,label); }
            }
            await Refuse(Merge(sa,sb),51155,"Non-adjacent tables refused");
            await Refuse(Merge(sa,sd),51154,"Different areas refused");
            await DatabaseTool.Execute(connection,$"EXEC dbo.usp_SetTableAdjacency @TableId={a},@AdjacentTableId={b},@IsAdjacent=1,@ActorUserId=1;");
            await Refuse($"EXEC dbo.usp_SetTableAdjacency @TableId={a},@AdjacentTableId={c},@IsAdjacent=1,@ActorUserId=2;",51001,"Waiter cannot configure adjacency");
            await Refuse($"EXEC dbo.usp_SetTableAdjacency @TableId={a},@AdjacentTableId={d},@IsAdjacent=1,@ActorUserId=1;",51152,"Cannot configure cross-area adjacency");
            await DatabaseTool.Execute(connection,$"UPDATE dbo.DiningTables SET Status='Cleaning' WHERE Id={b};");
            await Refuse(Merge(sa,sb),51153,"Non-serving table refused");
            await DatabaseTool.Execute(connection,$"UPDATE dbo.DiningTables SET Status='Serving' WHERE Id={b}; EXEC dbo.usp_SetPaymentState @SessionId={sb},@Awaiting=1,@ActorUserId=2;");
            await Refuse(Merge(sa,sb),51153,"Awaiting-payment table refused");
            await DatabaseTool.Execute(connection,$"EXEC dbo.usp_SetPaymentState @SessionId={sb},@Awaiting=0,@ActorUserId=2;");
            async Task Submit(long session,Guid request,int quantity) => await DatabaseTool.Execute(connection,$$"""
                EXEC dbo.usp_SubmitOrder @SessionId={{session}},@RequestId='{{request}}',@ItemsJson=N'[{"MenuItemId":1,"Quantity":{{quantity}},"Notes":"origin check"}]',@ActorUserId=2;
                """);
            var ra=Guid.NewGuid(); var rb=Guid.NewGuid();
            await Submit(sa,ra,2); await Submit(sb,rb,3);
            var ids=await Scalar(connection,"SELECT SUM(Id) FROM dbo.OrderItems");
            var total=await Scalar(connection,"SELECT SUM(LineTotal) FROM dbo.OrderItems");
            // HTTP form captures versions; a payment/reopen round trip invalidates that confirmation.
            await using var web=await Web.Start(connection,new() { ["Email__RetryPollSeconds"]="0" });
            var waiter=web.Client;
            await Login(waiter,"waiter",password);
            var page=await Html(waiter,$"/Ordering?tableId={a}");
            Assert(page.Contains("merge-tables-form") && page.Contains("MERGE-B"),"Waiter sees adjacent serving candidate and main-session choice");
            static string Value(string html,string field) => Regex.Match(html,$"name=\"{field}\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var va=Value(page,"currentVersion"); var vb=Value(page,"otherVersion");
            await DatabaseTool.Execute(connection,$"EXEC dbo.usp_SetPaymentState @SessionId={sb},@Awaiting=1,@ActorUserId=2; EXEC dbo.usp_SetPaymentState @SessionId={sb},@Awaiting=0,@ActorUserId=2;");
            async Task<HttpResponseMessage> PostMerge(string html,string av,string bv,bool token=true) => await waiter.PostAsync("/Ordering?handler=Merge",Form(
                ("tableId",a.ToString()),("currentSessionId",sa.ToString()),("otherSessionId",sb.ToString()),
                ("currentVersion",av),("otherVersion",bv),("keepCurrent","true"),("confirmed","true"),
                ("__RequestVerificationToken",token ? Token(html) : "")));
            using(var rejected=await PostMerge(page,va,vb)) Assert(rejected.StatusCode==HttpStatusCode.Redirect,"Stale confirmation returns to reload the table");
            page=await Html(waiter,$"/Ordering?tableId={a}");
            Assert(page.Contains("vừa thay đổi") && await Scalar(connection,"SELECT COUNT(*) FROM dbo.SessionMerges")==0,"Stale versions refuse a state change even after reopen");
            using(var csrf=await PostMerge(page,Value(page,"currentVersion"),Value(page,"otherVersion"),false))
                Assert(csrf.StatusCode==HttpStatusCode.BadRequest,"Merge requires antiforgery token");
            // Opposite directions overlap: exactly one may win, never a cycle or duplicate lines.
            async Task<bool> Race(long x,long y)
            {
                try { await DatabaseTool.Execute(connection,Merge(x,y)); return true; }
                catch(SqlException ex) when(ex.Number==51153) { return false; }
            }
            var winners=await Task.WhenAll(Race(sa,sb),Race(sb,sa));
            Assert(winners.Count(x=>x)==1,"Two simultaneous opposite merge requests have exactly one winner");
            var root=await Scalar(connection,$"SELECT COALESCE(BillingSessionId,Id) FROM dbo.DiningSessions WHERE Id={sa}");
            await Refuse(Merge(sa,sb),51153,"Duplicate merge refused");
            async Task<JsonElement> Snapshot() => JsonDocument.Parse(await Html(waiter,"/api/table-map/snapshot")).RootElement.Clone();
            static JsonElement Card(JsonElement map,string code) => map.GetProperty("areas").EnumerateArray()
                .SelectMany(area=>area.GetProperty("tables").EnumerateArray()).Single(t=>t.GetProperty("code").GetString()==code);
            var map=await Snapshot();
            var ga=Card(map,"MERGE-A").GetProperty("group"); var gb=Card(map,"MERGE-B").GetProperty("group");
            var label=ga.GetProperty("label").GetString()!;
            Assert(label==gb.GetProperty("label").GetString() && ga.GetProperty("mainSessionId").GetInt64()==root,
                "Task 2: both table cards expose the same persistent label and main session");
            Assert(ga.GetProperty("memberTableCodes").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(new[]{"MERGE-A","MERGE-B"}),
                "Task 2: the snapshot includes the complete member list");
            Assert((await Html(waiter,"/TableMap")).Split("Nhóm "+label).Length-1==2,
                "Task 2: server-rendered map displays the shared label on exactly two tables");
            Assert(Card(await Snapshot(),"MERGE-A").GetProperty("group").GetProperty("label").GetString()==label,
                "Task 2: reloading the map preserves the stored label");
            Assert((await Html(waiter,"/")).Split("Nhóm "+label).Length-1==2,
                "Task 2: the actual homepage map displays a badge on both members");
            var homeSnapshot=JsonDocument.Parse(await Html(waiter,"/api/table-status")).RootElement;
            Assert(homeSnapshot.EnumerateArray().Where(t=>t.GetProperty("code").GetString() is "MERGE-A" or "MERGE-B")
                .All(t=>t.GetProperty("group").GetProperty("label").GetString()==label),
                "Task 2: the homepage polling API carries the same persistent group");

            var changes=JsonDocument.Parse(await Html(waiter,"/api/table-map/changes?after=0")).RootElement.Clone();
            Assert(changes.GetProperty("tables").EnumerateArray().Where(x=>x.GetProperty("code").GetString() is "MERGE-A" or "MERGE-B")
                .All(x=>x.GetProperty("group").GetProperty("label").GetString()==label),
                "Task 2: polling includes the group even while status remains Serving");
            foreach(var code in new[]{"MERGE-A","MERGE-B"})
            {
                var details=JsonDocument.Parse(await Html(waiter,"/api/table-map/"+code)).RootElement;
                Assert(details.GetProperty("group").GetProperty("mainSessionId").GetInt64()==root,
                    "Task 2: details from either table resolve to the same main session");
                var groupPage=await Html(waiter,$"/Ordering?tableCode={code}&sessionId={root}");
                Assert(groupPage.Contains($"data-table-group=\"{label}\"") && groupPage.Contains($"data-main-session=\"{root}\"")
                    && groupPage.Contains("Các bàn thành viên: MERGE-A, MERGE-B"),
                    "Task 2: selecting either member shows members, main table and main session");
            }

            await Check(connection,$"SELECT CASE WHEN COUNT(*)=2 AND SUM(Id)={ids} AND SUM(LineTotal)={total} AND SUM(Quantity)=5 AND COUNT(DISTINCT OriginalTableId)=2 THEN 1 ELSE 0 END FROM dbo.OrderItems","Merge preserves IDs, prices, quantities, origins and exact totals");
            await Check(connection,$"SELECT CASE WHEN COUNT(*)=2 AND MIN(SessionId)={root} AND MAX(SessionId)={root} AND COUNT(DISTINCT BatchNumber)=2 THEN 1 ELSE 0 END FROM dbo.OrderBatches","All batches move to the main session without number collisions");
            await Refuse($"UPDATE dbo.OrderItems SET OriginalTableId={c};",51151,"Order-line origin is immutable");
            await Submit(sb,rb,3);
            Assert(await Scalar(connection,"SELECT COUNT(*) FROM dbo.OrderItems")==2,"Retry submitted before merge cannot duplicate items after merge");
            await Submit(sa,Guid.NewGuid(),1);
            // A QR guest at the member table also writes to the root, with the member as origin.
            var guestToken=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
            var guestHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(guestToken)));
            var qrHash=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await DatabaseTool.Execute(connection,$"EXEC dbo.usp_RotateTableQr @TableId={b},@TokenHash=0x{qrHash},@ActorUserId=1; EXEC dbo.usp_OpenGuestSession @QrTokenHash=0x{qrHash},@GuestTokenHash=0x{guestHash};");
            var guestRequest=Guid.NewGuid();
            var guestSubmit=$$"""EXEC dbo.usp_SubmitOrder @SessionId={{sb}},@RequestId='{{guestRequest}}',@ItemsJson=N'[{"MenuItemId":1,"Quantity":1}]',@GuestTokenHash=0x{{guestHash}};""";
            await DatabaseTool.Execute(connection,guestSubmit);
            await DatabaseTool.Execute(connection,guestSubmit);
            using var guestHandler=new HttpClientHandler { AllowAutoRedirect=false,CookieContainer=new CookieContainer() };
            guestHandler.CookieContainer.Add(waiter.BaseAddress!,new Cookie("RM.TableSession",guestToken,"/"));
            using var guestClient=new HttpClient(guestHandler) { BaseAddress=waiter.BaseAddress };
            var guestPage=await Html(guestClient,"/TableOrder");
            Assert(guestPage.Contains("Bàn gốc: MERGE-A") && guestPage.Contains("Bàn gốc: MERGE-B"),"QR guest sees the common list and both original tables");
            foreach(var table in new[]{a,b})
            {
                var html=await Html(waiter,$"/Ordering?tableId={table}");
                Assert(html.Contains("MERGE-A</td>") && html.Contains("MERGE-B</td>") && html.Contains("Bàn gốc"),"Either table shows the common list with original tables");
            }
            await Check(connection,$"SELECT CASE WHEN COUNT(*)=4 AND SUM(Quantity)=7 AND SUM(CASE WHEN OriginalTableId={a} THEN Quantity ELSE 0 END)=3 AND SUM(CASE WHEN OriginalTableId={b} THEN Quantity ELSE 0 END)=4 THEN 1 ELSE 0 END FROM dbo.OrderItems","Extra items from either table retain their own origin");
            // Complete a successful form submission too, explicitly choosing the other table as main.
            await DatabaseTool.Execute(connection,$"""
                INSERT dbo.DiningTables(AreaId,Code,MaxCapacity) VALUES(1,'MERGE-E',4);
                DECLARE @t int=(SELECT Id FROM dbo.DiningTables WHERE Code='MERGE-E');
                EXEC dbo.usp_OpenSession @TableId=@t,@GuestCount=2,@ActorUserId=2;
                """);
            var e=await Table("E"); var se=await Session(e);
            using var managerHandler=new HttpClientHandler { AllowAutoRedirect=false,CookieContainer=new CookieContainer() };
            using var manager=new HttpClient(managerHandler) { BaseAddress=waiter.BaseAddress };
            await Login(manager,"manager",password);
            var configPage=await Html(manager,"/TableAdjacency");
            using(var response=await manager.PostAsync("/TableAdjacency/Save",Form(("tableId",c.ToString()),("adjacentTableId",e.ToString()),("enabled","true"),("__RequestVerificationToken",Token(configPage)))))
                Assert(response.StatusCode==HttpStatusCode.Redirect,"Manager configures adjacency through the real form");
            using(var forbidden=await waiter.GetAsync("/TableAdjacency"))
                Assert(forbidden.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,"Waiter cannot open adjacency configuration");
            var formPage=await Html(waiter,$"/Ordering?tableId={c}");
            using(var response=await waiter.PostAsync("/Ordering?handler=Merge",Form(("tableId",c.ToString()),("currentSessionId",sc.ToString()),("otherSessionId",se.ToString()),
                ("currentVersion",Value(formPage,"currentVersion")),("otherVersion",Value(formPage,"otherVersion")),("keepCurrent","false"),("confirmed","true"),("__RequestVerificationToken",Token(formPage)))))
                Assert(response.StatusCode==HttpStatusCode.Redirect,"Waiter confirms a valid merge through the real form");
            Assert(await Scalar(connection,$"SELECT BillingSessionId FROM dbo.DiningSessions WHERE Id={sc}")==se,"The selected other table keeps the main session");
            var secondGroup=Card(await Snapshot(),"MERGE-C").GetProperty("group");
            Assert(secondGroup.GetProperty("label").GetString()!=label && secondGroup.GetProperty("mainTableCode").GetString()=="MERGE-E",
                "Task 2: another group has a different label and the explicitly selected main table");
            Assert(Card(await Snapshot(),"MERGE-A").GetProperty("group").GetProperty("label").GetString()==label,
                "Task 2: two active groups coexist with independent persistent labels");
            using(var response=await waiter.PostAsync("/Ordering?handler=RequestPayment",Form(("tableId",b.ToString()),("sessionId",sb.ToString()),("__RequestVerificationToken",Token(await Html(waiter,$"/Ordering?tableId={b}"))))))
                Assert(response.StatusCode==HttpStatusCode.Redirect,"Waiter requests payment from either table");
            await Check(connection,$"SELECT CASE WHEN COUNT(*)=2 THEN 1 ELSE 0 END FROM dbo.DiningSessions WHERE Id IN({sa},{sb}) AND Status='AwaitingPayment'","Whole group enters awaiting-payment state");
            var awaitingMap=await Snapshot();
            Assert(Card(awaitingMap,"MERGE-A").GetProperty("group").GetProperty("label").GetString()==label
                && Card(awaitingMap,"MERGE-B").GetProperty("status").GetString()=="AwaitingPayment"
                && Card(awaitingMap,"MERGE-B").GetProperty("group").GetProperty("status").GetString()=="AwaitingPayment",
                "Task 2: awaiting payment keeps the label and updates the group state");
            await Refuse($$"""EXEC dbo.usp_SubmitOrder @SessionId={{sb}},@RequestId='{{Guid.NewGuid()}}',@ItemsJson=N'[{"MenuItemId":1,"Quantity":1}]',@ActorUserId=2;""",51025,"Cannot add items after group requests payment");
            // Follow the real kitchen and serving transitions before checkout.
            await DatabaseTool.Execute(connection,$"""
                DECLARE @id bigint;
                DECLARE lines CURSOR LOCAL STATIC READ_ONLY FOR SELECT Id FROM dbo.OrderItems;
                OPEN lines; FETCH NEXT FROM lines INTO @id;
                WHILE @@FETCH_STATUS=0 BEGIN
                  EXEC dbo.usp_TransitionOrderItem @OrderItemId=@id,@ToStatus='Preparing',@ActorUserId=3;
                  EXEC dbo.usp_TransitionOrderItem @OrderItemId=@id,@ToStatus='Ready',@ActorUserId=3;
                  EXEC dbo.usp_TransitionOrderItem @OrderItemId=@id,@ToStatus='Served',@ActorUserId=2;
                  FETCH NEXT FROM lines INTO @id;
                END;
                CLOSE lines; DEALLOCATE lines;
                """);
            var payment=Guid.NewGuid();
            var checkout=$"EXEC dbo.usp_Checkout @SessionId={root},@RequestId='{payment}',@Method='Cash',@CashReceived=10000000,@ActorUserId=4;";
            await Task.WhenAll(DatabaseTool.Execute(connection,checkout),DatabaseTool.Execute(connection,checkout));
            await Check(connection,"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.Invoices)=1 AND (SELECT COUNT(*) FROM dbo.Payments)=1 AND (SELECT COUNT(*) FROM dbo.InvoiceLines)=4 AND (SELECT Total FROM dbo.Invoices)=(SELECT SUM(LineTotal) FROM dbo.OrderItems) THEN 1 ELSE 0 END","One invoice, one payment, exact combined total even on simultaneous checkout retries");
            await Check(connection,$"SELECT CASE WHEN (SELECT COUNT(*) FROM dbo.DiningTables WHERE Id IN({a},{b}) AND Status='Cleaning')=2 AND (SELECT COUNT(*) FROM dbo.DiningSessions WHERE Id IN({sa},{sb}) AND Status='Closed')=2 AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId IN({a},{b}) AND ReleasedAt IS NULL) THEN 1 ELSE 0 END","Payment closes both sessions, releases both tables and marks both for cleaning");
            await Check(connection,$"SELECT CASE WHEN COUNT(*)=1 THEN 1 ELSE 0 END FROM dbo.GuestSessions WHERE SessionId={sb} AND RevokedAt IS NOT NULL","Payment revokes the member table guest session");
            var paidHome=JsonDocument.Parse(await Html(waiter,"/api/table-status")).RootElement;
            Assert(paidHome.EnumerateArray().Where(t=>t.GetProperty("code").GetString() is "MERGE-A" or "MERGE-B")
                .All(t=>t.GetProperty("group").ValueKind==JsonValueKind.Null),
                "Task 2: the homepage API removes both serving-group labels after checkout");
            var paidMap=await Snapshot();
            Assert(Card(paidMap,"MERGE-A").GetProperty("group").ValueKind==JsonValueKind.Null
                && Card(paidMap,"MERGE-B").GetProperty("group").ValueKind==JsonValueKind.Null,
                "Task 2: neither paid table retains a serving group label");
            var paidChanges=JsonDocument.Parse(await Html(waiter,"/api/table-map/changes?after=0")).RootElement;
            Assert(paidChanges.GetProperty("tables").EnumerateArray().Where(x=>x.GetProperty("code").GetString() is "MERGE-A" or "MERGE-B")
                .All(x=>x.GetProperty("group").ValueKind==JsonValueKind.Null),
                "Task 2: polling removes both labels after checkout");
            Assert(Card(await Snapshot(),"MERGE-C").GetProperty("group").GetProperty("label").GetString()==secondGroup.GetProperty("label").GetString(),
                "Task 2: paying one group does not remove another serving group's label");
            Console.WriteLine("PASS: all table-merge and group-label acceptance checks.");
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
