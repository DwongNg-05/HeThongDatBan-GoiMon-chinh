namespace RestaurantManagement.Web.Authentication;

/// <summary>Automatic background reads must not extend the employee's idle session.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PassiveSessionReadAttribute : Attribute;
