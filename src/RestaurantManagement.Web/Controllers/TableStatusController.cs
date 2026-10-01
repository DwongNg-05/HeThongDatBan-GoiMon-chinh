using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RestaurantManagement.Web.Services;

namespace RestaurantManagement.Web.Controllers;

[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Waiter")]
[Route("api/table-status")]
public sealed class TableStatusController(DemoTableCatalog catalog, TableMapEventBroker eventBroker) : ControllerBase
{
    [HttpGet]
    public IActionResult Snapshot() => Ok(catalog.GetAll());

    [HttpPost("{code}")]
    public IActionResult Update(string code, [FromBody] UpdateTableStatusRequest request)
    {
        if (!catalog.TryUpdateStatus(code, request.Status, out var table, out var transition) || table is null)
            return BadRequest(new { message = "Mã bàn hoặc trạng thái không hợp lệ." });

        if (transition is not null)
            eventBroker.Publish(transition);
        return Ok(table);
    }

    [HttpGet("stream")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        using var subscription = eventBroker.Subscribe();
        await Response.WriteAsync("retry: 1000\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var waitTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                if (!await subscription.Reader.WaitToReadAsync(waitTimeout.Token)) break;
                while (subscription.Reader.TryRead(out var update))
                {
                    var json = JsonSerializer.Serialize(update, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    await Response.WriteAsync($"event: statusChanged\ndata: {json}\n\n", cancellationToken);
                }
                await Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // EventSource closes and reconnects routinely; RequestAborted is the normal end of this stream.
                break;
            }
        }
    }
}

public sealed record UpdateTableStatusRequest(string Status);
