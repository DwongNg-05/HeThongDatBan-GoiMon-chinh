using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace RestaurantManagement.Web.Authentication;

public sealed class BookingAntiforgeryRecoveryFilter(ITempDataDictionaryFactory tempDataFactory)
    : IAsyncAlwaysRunResultFilter
{
    public const string FormKey = "BookingExpiredForm";
    public const string MessageKey = "BookingExpiredFormMessage";

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.HttpContext.Request.Path.Equals("/Reservations/Create") &&
            HttpMethods.IsPost(context.HttpContext.Request.Method) &&
            context.Result is Microsoft.AspNetCore.Mvc.Core.Infrastructure.IAntiforgeryValidationFailedResult)
        {
            // The rejected request never creates a reservation. A new GET issues a fresh token.
            var form = await context.HttpContext.Request.ReadFormAsync(context.HttpContext.RequestAborted);
            var fields = new[] { "CustomerName", "Phone", "GuestCount", "StartsAt", "ReservationDate", "ReservationTime", "PreferredAreaId", "Email", "Notes", "TableId" };
            var values = fields.ToDictionary(field => field, field => form[field].ToString());
            var tempData = tempDataFactory.GetTempData(context.HttpContext);
            // Bound cookie size; never store the submitted verification token.
            if (values.Values.Sum(value => value.Length) <= 2000)
                tempData[FormKey] = JsonSerializer.Serialize(values);
            tempData[MessageKey] = "Phiên của biểu mẫu đã thay đổi. Vui lòng kiểm tra thông tin và bấm Đặt bàn lại.";
            tempData.Save();
            context.Result = new RedirectToActionResult("Create", "Reservations", null);
        }
        await next();
    }
}
