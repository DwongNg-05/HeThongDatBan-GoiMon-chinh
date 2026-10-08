namespace RestaurantManagement.Web.Models.Ordering;

public sealed record GuestOrderLineInput(int DishId, int Quantity);

public sealed record GuestOrderContext(long SessionId, string GuestToken);

public static class GuestOrderRules
{
    // S3-02 Task 1 accepts the database's existing positive quantity range.
    // Task 2 narrows this value to 20 and exposes increment/decrement controls.
    public const int MaximumQuantity = 99;

    public static string? Validate(IReadOnlyList<GuestOrderLineInput>? items)
    {
        if (items is null || items.Count == 0)
            return "Giỏ món đang trống. Vui lòng chọn ít nhất một món.";
        if (items.Count > 100)
            return "Giỏ món có quá nhiều dòng. Vui lòng kiểm tra lại.";
        if (items.Any(item => item.DishId <= 0))
            return "Có món không hợp lệ trong giỏ.";
        if (items.GroupBy(item => item.DishId).Any(group => group.Count() > 1))
            return "Mỗi món chỉ được có một dòng trong giỏ.";
        var invalid = items.FirstOrDefault(item => item.Quantity is < 1 or > MaximumQuantity);
        return invalid is null
            ? null
            : $"Số lượng của món mã {invalid.DishId} phải là số nguyên từ 1 đến {MaximumQuantity}.";
    }
}
