using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RestaurantManagement.Web.Security
{
    public class RequireRoleFilter : IPageFilter
    {
        private readonly string _role;
        public RequireRoleFilter(string role) => _role = role;

        public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

        public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            var session = context.HttpContext.Session;
            var user = session.GetString(SessionCurrentUser.KeyUserName);
            var role = session.GetString(SessionCurrentUser.KeyRole);

            if (string.IsNullOrEmpty(user))
            {
                context.Result = new RedirectToPageResult("/Login");
                return;
            }

            if (!string.Equals(role, _role, StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new StatusCodeResult(403);
            }
        }

        public void OnPageHandlerExecuted(PageHandlerExecutedContext context) { }
    }
}