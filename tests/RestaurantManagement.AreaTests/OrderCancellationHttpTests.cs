using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Text.Json;

internal static class OrderCancellationHttpTests
{
    internal static async Task Run(string connection,long id,Action<bool,string> check)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !Directory.Exists(Path.Combine(root.FullName,"database"))) root=root.Parent;
        var content=Path.Combine(root!.FullName,"src","RestaurantManagement.Web");
        using var listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); var port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start=new ProcessStartInfo("dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add(Path.Combine(content,"bin","s305","RestaurantManagement.Web.dll"));
        start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(content);start.ArgumentList.Add("--urls");start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";start.Environment["RM_CONNECTION_STRING"]=connection;
        using var process=Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        HttpClient Client() => new(new HttpClientHandler { CookieContainer=new CookieContainer(),AllowAutoRedirect=false }) { BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(5) };
        string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<string> Login(HttpClient client,string user,string page) {
            var html=await client.GetStringAsync("/Account/Login");
            using var response=await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Identifier",user},{"Password","S305TestPassword9!"},{"__RequestVerificationToken",Token(html)}}));
            check(response.StatusCode==HttpStatusCode.Redirect,"S3-05 HTTP login "+user);
            return Token(await client.GetStringAsync(page));
        }
        try
        {
            using var waiter=Client();using var kitchen=Client();using var cashier=Client();
            for(var retry=0;retry<30;retry++) { try { using var probe=await waiter.GetAsync("/Account/Login");break; } catch(HttpRequestException) { await Task.Delay(200); } }
            var token=await Login(waiter,"waiter","/Orders");
            var kitchenToken=await Login(kitchen,"kitchen","/Orders/Kitchen");
            await Login(cashier,"cashier","/Orders");
            var menuHtml=await waiter.GetStringAsync("/GoiMon");
            check(menuHtml.Contains("name=\"SessionId\""),"S3-05 HTTP menu offers active serving session");
            using(var submitted=await waiter.PostAsync("/GoiMon/Checkout",new FormUrlEncodedContent(new Dictionary<string,string>{
                {"CartJson","[{\"monId\":1,\"soLuong\":2}]"},{"SessionId","1"},{"RequestId",Guid.NewGuid().ToString()},{"__RequestVerificationToken",Token(menuHtml)}})))
                check(submitted.StatusCode==HttpStatusCode.Redirect && submitted.Headers.Location!.ToString().Contains("Orders"),"S3-05 HTTP submitted cart connects to SQL order workflow");
            using(var forbidden=await kitchen.PostAsync("/Orders/Cancel",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",id.ToString()},{"reason","Mistake"},{"__RequestVerificationToken",kitchenToken}})))
                check(forbidden.StatusCode==HttpStatusCode.Redirect || forbidden.StatusCode==HttpStatusCode.Forbidden,"S3-05 HTTP rejects kitchen cancellation");
            using(var missingToken=await waiter.PostAsync("/Orders/Cancel",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",id.ToString()},{"reason","Mistake"}})))
                check(missingToken.StatusCode==HttpStatusCode.BadRequest,"S3-05 HTTP requires anti-forgery token");
            using(var missingReason=await waiter.PostAsync("/Orders/Cancel",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",id.ToString()},{"__RequestVerificationToken",token}})))
                check(missingReason.StatusCode==HttpStatusCode.BadRequest,"S3-05 HTTP requires cancellation reason");
            var watch=Stopwatch.StartNew();
            using(var cancelled=await waiter.PostAsync("/Orders/Cancel",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",id.ToString()},{"reason","ChangedMind"},{"__RequestVerificationToken",token}})))
                check(cancelled.StatusCode==HttpStatusCode.OK,"S3-05 HTTP cancellation succeeds");
            using var snapshot=JsonDocument.Parse(await kitchen.GetStringAsync("/Orders/Snapshot?kitchen=true"));
            check(!snapshot.RootElement.GetProperty("items").EnumerateArray().Any(item=>item.GetProperty("id").GetInt64()==id) && watch.Elapsed<TimeSpan.FromSeconds(5),"S3-05 HTTP kitchen queue removes line within five seconds");
        }
        finally
        {
            if(!process.HasExited) process.Kill(entireProcessTree:true);
            await process.WaitForExitAsync();
            var log=await errors;
            if(!string.IsNullOrWhiteSpace(log)) Console.WriteLine(log);
        }
    }
}
