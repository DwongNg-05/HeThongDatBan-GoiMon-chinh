using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RestaurantManagement.Web.Models.Reservations;
using RestaurantManagement.Web.Services;

internal static class ReservationRejectionTests
{
    static void Check(bool ok,string label) { if(!ok) throw new Exception("FAIL rejection: "+label); Console.WriteLine("PASS rejection: "+label); }
    internal static async Task Run(ReservationConfirmationService service,Func<string,Task<object?>> sql,int table,long confirmed)
    {
        var seq=0;
        async Task<long> New()
        {
            var code="R"+(++seq).ToString("D5");
            return Convert.ToInt64(await sql($"DECLARE @s datetime2=DATEADD(day,10,SYSUTCDATETIME()); INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,Email,StartsAt,EndsAt) VALUES('{code}',N'Rejection fixture','0901234567',4,'test@example.test',@s,DATEADD(minute,90,@s)); SELECT CONVERT(bigint,SCOPE_IDENTITY());"));
        }
        async Task<int> Reject(long id,string? reason,int actor=2)
        { try { await service.Reject(actor,id,reason);return 0; } catch(SqlException ex) {return ex.Number;} }
        foreach(var reason in RejectionReasons.Labels.Keys)
        {
            var id=await New(); var before=(await service.Details(2,id))!;
            Check(await Reject(id,reason)==0,"accept reason "+reason);
            var after=(await service.Details(2,id))!;
            Check(after.Status=="Rejected" && after.RejectionReason==reason && after.TableCode==null,"persist rejection and no held table "+reason);
            Check(!(await service.Pending(2)).Any(r=>r.Id==id),"removed from pending "+reason);
            var guest=await service.Lookup(before.Code,"0901234567","test-rejection");
            Check(guest?.Status=="Rejected" && guest.RejectionText==RejectionReasons.CustomerText(reason),"customer sees exact reason "+reason);
            Check(await Reject(id,"NoTable")==51011,"second rejection blocked "+reason);
            Check(Convert.ToInt32(await sql($"SELECT COUNT(*) FROM dbo.EmailOutbox WHERE ReservationId={id} AND MessageType='BookingRejected'"))==0,"no automatic rejection email "+reason);
        }
        var pending=await New();
        foreach(var reason in new string?[]{null,"","Unknown","NoTable ","notable"})
            Check(await Reject(pending,reason)==51010,"invalid/missing fixed reason rejected: "+(reason??"NULL"));
        Check((await service.Details(2,pending))!.Status=="Pending","invalid requests leave status unchanged");
        Check(await Reject(confirmed,"NoTable")==51011,"confirmed reservation cannot be rejected");
        var c=(await service.Details(2,confirmed))!;
        var lookup=await service.Lookup(c.Code,"0901234567","test-rejection");
        Check(lookup?.Status=="Confirmed" && lookup.RejectionText==null,"confirmed lookup never shows rejection reason");
        Check(await service.Lookup(c.Code,"0909999999","test-rejection")==null,"wrong phone cannot access booking");
        Check(await service.Lookup("ZZZZZZ","0901234567","test-rejection")==null,"wrong code returns no booking");
        Check(await Reject(pending,"NoTable",3)==51001,"kitchen cannot reject");
        for(var i=0;i<4;i++)
        {
            var id=await New();
            async Task<int> Confirm() { try { await service.Confirm(1,id,table);return 0; } catch(SqlException ex){return ex.Number;} }
            var results=await Task.WhenAll(Confirm(),Reject(id,"NoTable"));
            Check(results.Count(n=>n==0)==1,"confirm/reject race has exactly one winner "+i);
            var final=(await service.Details(2,id))!;
            Check((final.Status=="Confirmed" && final.TableCode!=null && final.RejectionReason==null) || (final.Status=="Rejected" && final.TableCode==null && final.RejectionReason=="NoTable"),"race leaves consistent final state "+i);
            // Move only this test fixture out of the next race's interval.
            await sql($"UPDATE dbo.Reservations SET StartsAt=DATEADD(day,{20+i},StartsAt),EndsAt=DATEADD(day,{20+i},EndsAt) WHERE Id={id}");
        }
        for(var i=0;i<20;i++) await service.Lookup("ZZZZZZ","0901234567","rate-test");
        var limited=false;
        try {await service.Lookup("ZZZZZZ","0901234567","rate-test");}catch(SqlException ex) when(ex.Number==51620){limited=true;}
        Check(limited,"anonymous lookup is rate limited");
    }
    internal static async Task Http(HttpClient staff,Func<string,Task<object?>> sql)
    {
        async Task<HttpResponseMessage> Post(HttpClient client,string form,string action,Dictionary<string,string> fields)
        {
            var html=await client.GetStringAsync(form);
            fields["__RequestVerificationToken"]=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            return await client.PostAsync(action,new FormUrlEncodedContent(fields));
        }
        var id=Convert.ToInt64(await sql("DECLARE @s datetime2=DATEADD(day,5,SYSUTCDATETIME()); INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,StartsAt,EndsAt) VALUES('REJWEB',N'Private guest','0901234567',4,@s,DATEADD(minute,90,@s)); SELECT CONVERT(bigint,SCOPE_IDENTITY());"));
        var path=$"/ReservationConfirmations/Details/{id}";
        var html=await staff.GetStringAsync(path);
        Check(html.Contains("id=\"submit-rejection\"") && Regex.IsMatch(html,"<button[^>]+id=\"submit-rejection\"[^>]+disabled"),"reject submit starts disabled");
        Check(RejectionReasons.Labels.Keys.All(k=>html.Contains($"value=\"{k}\"")),"three fixed reasons rendered");
        var missingCsrf=await staff.PostAsync($"/ReservationConfirmations/Reject/{id}",new FormUrlEncodedContent(new Dictionary<string,string> { ["reason"]="NoTable" }));
        Check(missingCsrf.StatusCode==HttpStatusCode.BadRequest,"rejection requires CSRF");
        foreach(var reason in new[]{"","forged"})
        {
            var result=await Post(staff,path,$"/ReservationConfirmations/Reject/{id}",new() { ["reason"]=reason });
            Check(WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync()).Contains("Vui lòng chọn một trong ba lý do"),"server shows invalid reason error");
        }
        var rejected=await Post(staff,path,$"/ReservationConfirmations/Reject/{id}",new() { ["reason"]="NoTable" });
        Check(rejected.StatusCode==HttpStatusCode.Redirect,"valid rejection redirects to pending list");
        var pending=WebUtility.HtmlDecode(await staff.GetStringAsync("/ReservationConfirmations"));
        Check(pending.Contains("Đã từ chối lượt đặt") && !pending.Contains("REJWEB"),"success message and removal from pending list");
        using var guest=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress=staff.BaseAddress };
        Check((await guest.GetAsync("/ReservationLookup")).StatusCode==HttpStatusCode.OK,"lookup needs no login");
        var found=await Post(guest,"/ReservationLookup","/ReservationLookup",new() { ["Code"]="REJWEB",["Phone"]="0901234567" });
        var body=WebUtility.HtmlDecode(await found.Content.ReadAsStringAsync());
        Check(body.Contains("Đã từ chối") && body.Contains(RejectionReasons.CustomerText("NoTable")!),"anonymous customer sees rejected status and correct text");
        Check(!body.Contains("Private guest") && found.Headers.CacheControl?.NoStore==true,"lookup hides personal details and forbids response caching");
        var wrong=await Post(guest,"/ReservationLookup","/ReservationLookup",new() { ["Code"]="REJWEB",["Phone"]="0909999999" });
        Check(WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync()).Contains("Không tìm thấy lượt đặt phù hợp"),"wrong phone has generic not-found result");
        var anonymousReject=await guest.PostAsync($"/ReservationConfirmations/Reject/{id}",new FormUrlEncodedContent(new Dictionary<string,string>()));
        Check(anonymousReject.StatusCode==HttpStatusCode.Redirect,"anonymous users cannot reject");
    }
}

