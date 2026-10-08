using System.Net;
using Microsoft.Data.SqlClient;
using static RestaurantManagement.DbTool.BookingConfirmationVerification;

namespace RestaurantManagement.DbTool;

internal static class KitchenBatchVerification
{
    internal static async Task Run(string connection, HttpClient kitchen, HttpClient waiter, string kitchenToken, string waiterToken)
    {
        async Task<long> NewBatch()
        {
            await DatabaseTool.Execute(connection, """
                DECLARE @request uniqueidentifier=NEWID();
                EXEC dbo.usp_SubmitOrder @SessionId=1,@RequestId=@request,@ActorUserId=2,
                  @ItemsJson=N'[{"MenuItemId":1,"Quantity":1,"Notes":"batch-one"},{"MenuItemId":1,"Quantity":1,"Notes":"batch-two"},{"MenuItemId":1,"Quantity":1,"Notes":"batch-three"}]';
                """);
            return await Scalar(connection, "SELECT MAX(Id) FROM dbo.OrderBatches");
        }
        async Task Start(long batch, bool firstOnly = false)
        {
            var ids = new List<(long Id, byte[] Version)>();
            await using var cn = new SqlConnection(connection); await cn.OpenAsync();
            await using (var cmd = new SqlCommand($"SELECT Id,RowVersion FROM dbo.OrderItems WHERE BatchId={batch} AND Status='Pending' ORDER BY Id", cn))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) ids.Add((reader.GetInt64(0), (byte[])reader[1]));
            }
            foreach (var item in firstOnly ? ids.Take(1) : ids)
            {
                await using var cmd = new SqlCommand("EXEC dbo.usp_KitchenLineTransition @id,'Pending','Preparing',@version,3",cn);
                cmd.Parameters.AddWithValue("@id",item.Id);
                cmd.Parameters.Add("@version",System.Data.SqlDbType.Binary,8).Value=item.Version;
                await cmd.ExecuteNonQueryAsync();
            }
        }
        async Task<int> Finish(long batch, int actor=3)
        {
            await using var cn = new SqlConnection(connection); await cn.OpenAsync();
            await using var cmd = new SqlCommand("EXEC dbo.usp_KitchenBatchComplete @batch,@actor", cn);
            cmd.Parameters.AddWithValue("@batch",batch); cmd.Parameters.AddWithValue("@actor",actor);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        async Task Reject(long batch, int expected, int actor=3)
        {
            try { await Finish(batch,actor); throw new Exception("Batch should be rejected."); }
            catch (SqlException ex) when (ex.Number==expected) { Console.WriteLine($"PASS: batch rejected ({expected})."); }
        }
        var batch = await NewBatch();
        await Start(batch,firstOnly:true);
        var before = await Scalar(connection,$"SELECT COUNT(*) FROM dbo.OrderItemEvents e JOIN dbo.OrderItems i ON i.Id=e.OrderItemId WHERE i.BatchId={batch}");
        await Reject(batch,51030);
        await Check(connection,$"SELECT CASE WHEN COUNT(*)={before} THEN 1 ELSE 0 END FROM dbo.OrderItemEvents e JOIN dbo.OrderItems i ON i.Id=e.OrderItemId WHERE i.BatchId={batch}","Batch with pending dish writes no history");
        await Start(batch);
        await Reject(batch,51001,2);
        var first = await Scalar(connection,$"SELECT MIN(Id) FROM dbo.OrderItems WHERE BatchId={batch}");
        await DatabaseTool.Execute(connection,$"DECLARE @v binary(8)=(SELECT RowVersion FROM dbo.OrderItems WHERE Id={first}); EXEC dbo.usp_KitchenLineTransition {first},'Preparing','Ready',@v,3;");
        var last = await Scalar(connection,$"SELECT MAX(Id) FROM dbo.OrderItems WHERE BatchId={batch}");
        // The trigger exists only in this disposable verification database and fails after an earlier line was updated.
        try
        {
            await DatabaseTool.Execute(connection,$"CREATE TRIGGER dbo.TestKitchenBatchFailure ON dbo.OrderItems AFTER UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Id={last} AND Status='Ready') THROW 51490,'Injected mid-batch failure',1; END;");
            await Reject(batch,51490);
        }
        finally { await DatabaseTool.Execute(connection,"DROP TRIGGER IF EXISTS dbo.TestKitchenBatchFailure;"); }
        await Check(connection,$"SELECT CASE WHEN COUNT(*)=2 AND MIN(Status)='Preparing' AND MAX(Status)='Preparing' AND COUNT(ReadyAt)=0 THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE BatchId={batch} AND Id<>{first}","Mid-batch failure rolls back every affected line and timestamps");
        await Check(connection,$"SELECT CASE WHEN COUNT(*)=4 THEN 1 ELSE 0 END FROM dbo.OrderItemEvents e JOIN dbo.OrderItems i ON i.Id=e.OrderItemId WHERE i.BatchId={batch} AND e.FromStatus IS NOT NULL", "Mid-batch failure rolls back inserted history");
        var oldVersion = await Scalar(connection,$"SELECT CONVERT(bigint,RowVersion) FROM dbo.OrderItems WHERE Id={first}");
        var attempts = await Task.WhenAll(Finish(batch),Finish(batch));
        Assert(attempts.Order().SequenceEqual(new[]{0,2}),"Concurrent batch requests complete once, retry is no-op");
        await Check(connection,$"SELECT CASE WHEN COUNT(*)=3 AND MIN(Status)='Ready' AND MAX(Status)='Ready' AND MIN(DATEDIFF_BIG(millisecond,PreparingAt,ReadyAt))>0 THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE BatchId={batch}","All batch dishes ready with actual durations");
        await Check(connection,$"SELECT CASE WHEN CONVERT(bigint,RowVersion)={oldVersion} THEN 1 ELSE 0 END FROM dbo.OrderItems WHERE Id={first}","Already-ready dish and timestamps remain untouched");
        await Check(connection,$"SELECT CASE WHEN COUNT(*)=6 THEN 1 ELSE 0 END FROM dbo.OrderItemEvents e JOIN dbo.OrderItems i ON i.Id=e.OrderItemId WHERE i.BatchId={batch} AND e.FromStatus IS NOT NULL", "Exactly two transition events per dish, no duplicates");

        var httpBatch=await NewBatch();
        async Task<HttpResponseMessage> Post(HttpClient client,string token) => await client.PostAsync("/Kitchen/CompleteBatch",Form(("batchId",httpBatch.ToString()),("__RequestVerificationToken",token)));
        using(var blocked=await Post(kitchen,kitchenToken)) Assert(blocked.StatusCode==HttpStatusCode.Conflict,"HTTP pending batch rejected");
        await Start(httpBatch);
        using(var denied=await Post(waiter,waiterToken)) Assert(denied.StatusCode==HttpStatusCode.Forbidden,"Waiter cannot complete batch");
        using(var missingToken=await kitchen.PostAsync("/Kitchen/CompleteBatch",Form(("batchId",httpBatch.ToString())))) Assert(missingToken.StatusCode==HttpStatusCode.BadRequest,"Batch requires antiforgery token");
        using(var success=await Post(kitchen,kitchenToken)) Assert(success.StatusCode==HttpStatusCode.OK,"Kitchen completes batch through HTTP");
        using(var repeat=await Post(kitchen,kitchenToken)) Assert(repeat.StatusCode==HttpStatusCode.OK && (await repeat.Content.ReadAsStringAsync()).Contains("\"changed\":0"),"Repeated HTTP batch request changes nothing");
        using var snapshot=System.Text.Json.JsonDocument.Parse(await waiter.GetStringAsync("/Kitchen/Snapshot"));
        var ready=snapshot.RootElement.EnumerateArray().Where(i=>i.GetProperty("batchId").GetInt64()==httpBatch).ToArray();
        Assert(ready.Length==3 && ready.All(i=>i.GetProperty("status").GetString()=="Ready" && i.GetProperty("actualCookingMilliseconds").GetDouble()>0),"Waiter snapshot receives entire completed batch with durations");
    }
}
