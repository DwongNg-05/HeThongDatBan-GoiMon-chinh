using RestaurantManagement.Web.Models.Kitchen;

internal static class KitchenTicketTests
{
    internal static void Run(Action<bool, string> check)
    {
        var time = new DateTime(2026, 10, 11, 9, 0, 0);
        var later = time.AddMinutes(1);
        var rows = new[]
        {
            new KitchenTicketRow(30, "B03", 2, later, new KitchenOrderItem(301, "Phở", "tô", 99, new string('x', 200), "Pending", 15)),
            new KitchenTicketRow(20, "B02", 1, time, new KitchenOrderItem(201, "Cơm", "phần", 1, null, "Ready", 10)),
            new KitchenTicketRow(10, "B01", 1, time, new KitchenOrderItem(101, "Gỏi", "đĩa", 2, "Ít cay", "Pending", 8)),
            new KitchenTicketRow(10, "B01", 1, time, new KitchenOrderItem(102, "Trà", "ly", 3, "Không đá", "Preparing", 5))
        };
        var tickets = KitchenTicketOrdering.Group(rows);
        check(tickets.Select(ticket => ticket.BatchId).SequenceEqual(new long[] { 10, 20, 30 }), "Kitchen tickets order oldest first and tie-break by batch ID");
        check(tickets[0].TableCode == "B01" && tickets[0].BatchNumber == 1 && tickets[0].Items.Count == 2,
            "Kitchen ticket groups all items from one call and keeps table and batch identity");
        check(tickets[0].Items[0] is { ItemName: "Gỏi", Quantity: 2, Notes: "Ít cay" }
            && tickets[2].Items[0] is { ItemName: "Phở", Quantity: 99 } && tickets[2].Items[0].Notes?.Length == 200,
            "Kitchen ticket preserves names, large quantities, and long notes");
        check(tickets[0].StatusLabel == "Đang xử lý" && tickets[1].StatusLabel == "Đã chế biến xong"
            && tickets[2].StatusLabel == "Chờ chế biến", "Kitchen ticket shows a useful aggregate status");
        check(KitchenTicketOrdering.Group(Array.Empty<KitchenTicketRow>()).Count == 0, "Kitchen queue handles no waiting tickets");
    }
}
