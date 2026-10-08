using System.Collections.Generic;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Services
{
    public interface IMenuStore
    {
        // DishCategory (Category) Management
        IEnumerable<DishCategory> GetAllCategories();
        IEnumerable<DishCategory> GetActiveCategories();
        DishCategory? GetCategory(int id);
        DishCategory AddCategory(DishCategory category);
        bool RenameCategory(int id, string? name);
        bool SetCategoryActive(int id, bool isActive);
        bool DeleteCategory(int id);
        void SaveCategoryOrder(IReadOnlyList<int> ids, IReadOnlyList<int> originalIds);

        // Dish (Dish/Menu Item) Management
        IEnumerable<Dish> GetAllDishes();
        IEnumerable<Dish> GetDishesByCategory(int categoryId);
        Dish AddDish(Dish dish);
        Dish? GetDish(int id);
        Dish? UpdateDish(Dish updated);

        // Public Menu (Thực đơn công khai)
        IReadOnlyList<CategoryWithDishes> GetMenuByCategory();

        // Thực đơn cho khách (không cần đăng nhập): chỉ nhóm đang sử dụng và món đang bán,
        // kèm ảnh, mô tả ngắn, giá VND và cờ hết trong ngày. Xem docs/S2-01-Task1.md.
        IReadOnlyList<PublicMenuCategory> GetPublicMenu();

        // Order (Order) Management
        Order CreateOrder();
        Order? GetOrder(int orderId);
        OrderLine AddOrderLine(int orderId, OrderLine line);
        IEnumerable<OrderLine> GetOrderLines(int orderId);
        IEnumerable<Order> GetAllOrders();

        // Audit (Nhật ký thay đổi giá)
        IEnumerable<RestaurantManagement.Data.Models.PriceChangeLog> GetPriceHistory(int dishId);
        // Các lần đổi giá gần nhất của mọi món (mới nhất trước), hiển thị trong màn hình Quản lý món.
        IEnumerable<RestaurantManagement.Data.Models.PriceChangeLog> GetRecentPriceChanges(int count);
    }
}
