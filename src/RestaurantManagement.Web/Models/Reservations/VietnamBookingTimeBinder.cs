using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RestaurantManagement.Web.Models.Reservations;

public sealed class VietnamBookingTimeBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        context.ModelState.SetModelValue(context.ModelName, value);
        if (value.Length != 1 || !VietnamTime.TryParseBooking(value.FirstValue, out var local))
        {
            context.ModelState.TryAddModelError(context.ModelName, "Vui lòng nhập ngày giờ hợp lệ theo giờ Việt Nam hoặc ISO 8601 có UTC offset.");
            context.Result = ModelBindingResult.Failed();
        }
        else
        {
            // Redisplay offset-bearing submissions as Vietnam wall time in datetime-local.
            context.ModelState.SetModelValue(context.ModelName, local.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture), local.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));
            context.Result = ModelBindingResult.Success(local);
        }
        return Task.CompletedTask;
    }
}
