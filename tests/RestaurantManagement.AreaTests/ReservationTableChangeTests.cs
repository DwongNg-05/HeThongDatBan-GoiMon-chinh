using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Services;
internal static class ReservationTableChangeTests
{
    internal static async Task Http(System.Net.Http.HttpClient client,Func<string,Task<object?>> sql,long id,int oldTable)
    {
        var path=$"/ReservationConfirmations/Details/{id}";
        var html=await client.GetStringAsync(path);
        Check(html.Contains("name=\"newTableId\""),"confirmed detail renders change form");
        var target=Convert.ToInt32(await sql("SELECT Id FROM dbo.DiningTables WHERE Code='T6'"));
        var noToken=await client.PostAsync($"/ReservationConfirmations/ChangeTable/{id}",new System.Net.Http.FormUrlEncodedContent(new Dictionary<string,string>()));
        Check(noToken.StatusCode==System.Net.HttpStatusCode.BadRequest,"change requires CSRF token");
        async Task<System.Net.Http.HttpResponseMessage> Post(int table,int expected)
        {
            var form=await client.GetStringAsync(path);
            var token=System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(form,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            return await client.PostAsync($"/ReservationConfirmations/ChangeTable/{id}",new System.Net.Http.FormUrlEncodedContent(new Dictionary<string,string> { ["newTableId"]=table.ToString(),["expectedTableId"]=expected.ToString(),["changeReason"]="HTTP test",["__RequestVerificationToken"]=token }));
        }
        var changed=await Post(target,oldTable);
        Check(changed.StatusCode==System.Net.HttpStatusCode.Redirect,"HTTP change succeeds");
        html=System.Net.WebUtility.HtmlDecode(await client.GetStringAsync(path));
        Check(html.Contains("HTTP test") && html.Contains("waiter") && html.Contains("Lịch sử đổi bàn"),"history rendered with actor and reason");
        var stale=await Post(oldTable,oldTable);
        Check(System.Net.WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()).Contains("Bàn đã được nhân viên khác thay đổi"),"stale form gets clear error");
        await sql($"UPDATE dbo.Reservations SET StartsAt=DATEADD(day,-1,SYSUTCDATETIME()),EndsAt=DATEADD(hour,-22,SYSUTCDATETIME()) WHERE Id={id}");
        html=System.Net.WebUtility.HtmlDecode(await client.GetStringAsync(path));
        Check(!html.Contains("name=\"newTableId\"") && html.Contains("Không thể đổi bàn"),"expired detail hides change form");
        var expired=await Post(oldTable,target);
        Check(System.Net.WebUtility.HtmlDecode(await expired.Content.ReadAsStringAsync()).Contains("Đã đến hoặc quá giờ hẹn"),"direct expired POST blocked");
    }
    static void Check(bool ok,string label) {if(!ok)throw new Exception("FAIL table change: "+label);Console.WriteLine("PASS table change: "+label);}
    internal static async Task Run(ReservationConfirmationService service,Func<string,Task<object?>> sql)
    {
        var area=Convert.ToInt32(await sql("INSERT dbo.Areas(Name,SortOrder) VALUES(N'Change test',999); SELECT CONVERT(int,SCOPE_IDENTITY());"));
        async Task<int> Table(string code,int capacity) => Convert.ToInt32(await sql($"INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity) VALUES({area},'{code}',1,{capacity}); SELECT CONVERT(int,SCOPE_IDENTITY());"));
        var a=await Table("CHANGE5",4);var b=await Table("CHANGE8",4);var c=await Table("CHANGE9",6);var small=await Table("CHANGE2",2);
        int seq=0;
        async Task<long> Booking(int minutes=20) => Convert.ToInt64(await sql($"DECLARE @s datetime2=DATEADD(minute,{minutes},SYSUTCDATETIME()); INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,StartsAt,EndsAt) VALUES('X{++seq:D5}',N'Change guest','0901234567',4,@s,DATEADD(minute,90,@s)); SELECT CONVERT(bigint,SCOPE_IDENTITY());"));
        async Task<int> Change(long id,int target,int expected,int actor=2,string? reason=null) { try{await service.ChangeTable(actor,id,target,expected,reason);return 0;}catch(SqlException ex){return ex.Number;} }
        var id=await Booking(); await service.Confirm(2,id,a);
        var model=(await service.Details(2,id))!;
        Check(!model.ReplacementTables.Any(t=>t.Id==a || t.Id==small) && model.ReplacementTables.Any(t=>t.Id==b),"suggestions exclude held and small table, include exact capacity");
        Check(await Change(id,b,a,2,"Khách muốn đổi vị trí")==0,"change to free table succeeds");
        model=(await service.Details(2,id))!;
        Check(model.TableId==b && model.Status=="Confirmed" && model.TableChanges.Count==1 && model.TableChanges[0].OldTableCode=="CHANGE5" && model.TableChanges[0].NewTableCode=="CHANGE8" && model.TableChanges[0].ActorName=="waiter","history snapshots and confirmed state");
        Check(Convert.ToString(await sql($"SELECT Status FROM dbo.DiningTables WHERE Id={a}"))=="Available" && Convert.ToString(await sql($"SELECT Status FROM dbo.DiningTables WHERE Id={b}"))=="Reserved","old operational table released and new reserved near appointment");
        var other=await Booking();await service.Confirm(2,other,a);
        Check((await service.Details(2,other))!.TableId==a,"another booking can confirm on released old table");
        Check(await Change(id,a,b)==51009,"overlap rejected");
        Check(await Change(id,small,b)==51008,"too-small table rejected");
        Check(await Change(id,b,b)==51705,"same table rejected");
        Check(await Change(id,c,a)==51704,"stale original table rejected");
        Check(await Change(id,c,b,2,new string('x',501))==51706,"oversized reason rejected");
        Check((await service.Details(2,id))!.TableChanges.Count==1 && (await service.Details(2,id))!.TableId==b,"failures preserve hold and do not create history");
        Check(await Change(id,c,b,1)==0,"second change succeeds");
        model=(await service.Details(2,id))!;
        Check(model.TableChanges.Count==2 && model.TableChanges[0].Id>model.TableChanges[1].Id && model.TableChanges[0].OldTableCode=="CHANGE8" && model.TableChanges[0].NewTableCode=="CHANGE9" && model.TableChanges[0].ActorName=="manager","two changes ordered newest first with correct actors");
        var pending=await Booking();Check(await Change(pending,b,a)==51702,"pending cannot change");await service.Reject(2,pending,"NoTable");Check(await Change(pending,b,a)==51702,"rejected cannot change");
        await sql($"UPDATE dbo.Reservations SET StartsAt=SYSUTCDATETIME() WHERE Id={id}");
        Check(await Change(id,b,c)==51703 && !(await service.Details(2,id))!.CanChangeTable,"at/past appointment server and UI block change");
        var ra=await Booking(300);var rb=await Booking(300);await service.Confirm(2,ra,a);await service.Confirm(2,rb,b);
        var results=await Task.WhenAll(Change(ra,c,a,1),Change(rb,c,b,2));
        Check(results.Count(n=>n==0)==1 && results.Count(n=>n==51009)==1,"two staff moving to same table: exactly one succeeds");
        var winner=results[0]==0?ra:rb;var loser=results[0]==0?rb:ra;
        Check((await service.Details(2,winner))!.TableChanges.Count==1 && (await service.Details(2,loser))!.TableChanges.Count==0,"race only logs successful change");
        Check(await Change(winner,a,c,3)==51001,"kitchen cannot change");
        var denied=false; try{await service.Details(3,winner);}catch(SqlException ex)when(ex.Number==51001){denied=true;}Check(denied,"unauthorized role cannot view history");
    }
}
