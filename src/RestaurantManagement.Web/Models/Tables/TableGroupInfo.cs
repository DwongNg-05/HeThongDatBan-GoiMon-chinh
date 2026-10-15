namespace RestaurantManagement.Web.Models;

public sealed record TableGroupInfo(string Label, long MainSessionId, string MainTableCode,
    IReadOnlyList<string> MemberTableCodes, string Status);
