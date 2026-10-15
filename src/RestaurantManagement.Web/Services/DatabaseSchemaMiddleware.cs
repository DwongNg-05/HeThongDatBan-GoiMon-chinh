namespace RestaurantManagement.Web.Services;

// Explain an incomplete deployment before controllers execute queries against missing columns.
// This never creates/changes database objects and does not replace running migrations.
public sealed class DatabaseSchemaMiddleware(RequestDelegate next)
{
    private static readonly string[] DependentPaths =
    ["/Kitchen","/Serving","/Ordering","/GuestOrdering","/TableOrder","/TableQr","/TableAdjacency","/ProvisionalBill","/Cashier","/api/table-map","/api/table-status","/TableMap"];

    public async Task InvokeAsync(HttpContext context, DatabaseSchemaReadiness schema, IWebHostEnvironment environment)
    {
        if(!(DependentPaths.Any(path=>context.Request.Path.StartsWithSegments(path)) || context.Request.Path=="/" || context.Request.Path=="/Home" || context.Request.Path=="/Home/Index")
            || await schema.IsReadyAsync(context.RequestAborted))
        {
            await next(context);
            return;
        }
        context.Response.StatusCode=StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.CacheControl="no-store";
        context.Response.Headers.RetryAfter="5";
        var message="Cơ sở dữ liệu chưa được cập nhật. Vui lòng liên hệ người quản lý hệ thống để hoàn tất cập nhật trước khi sử dụng chức năng này.";
        if(context.Request.Path.StartsWithSegments("/api"))
        {
            await context.Response.WriteAsJsonAsync(new { message, code="DatabaseUpgradeRequired" },context.RequestAborted);
            return;
        }
        context.Response.ContentType="text/html; charset=utf-8";
        var help=environment.IsDevelopment()
            ? "<p>Cần áp dụng các migration còn thiếu, gồm 047_S303QuickNotes.sql, 059_MergeTablesTask1.sql, 060_TableGroupLabels.sql và 061_UnmergeTables.sql. Kiểm tra bằng lệnh migration-status của RestaurantManagement.DbTool.</p>"
            : "";
        await context.Response.WriteAsync("<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Cần cập nhật cơ sở dữ liệu</title><main style=\"max-width:48rem;margin:4rem auto;padding:1rem;font-family:system-ui\"><h1>Cần cập nhật cơ sở dữ liệu</h1><p>"+message+"</p>"+help+"<p><a href=\"\">Thử lại</a></p></main></html>",context.RequestAborted);
    }
}
