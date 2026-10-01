namespace RestaurantManagement.Web.Authentication;

// Automatic background reads must not keep an otherwise idle login session alive.
[AttributeUsage(AttributeTargets.Method)]
public sealed class PassiveSessionReadAttribute : Attribute;
