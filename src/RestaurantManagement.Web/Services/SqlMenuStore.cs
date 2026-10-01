using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using RestaurantManagement.Data.Data;
using RestaurantManagement.Data.Models;
using RestaurantManagement.Web.Security;

namespace RestaurantManagement.Web.Services
{
    public class SqlMenuStore : IMenuStore
    {
        private readonly RestaurantDbContext _db;
        private readonly ICurrentUser _user;

        public SqlMenuStore(RestaurantDbContext db, ICurrentUser user)
        {
            _db = db;
            _user = user;
        }

        public IEnumerable<DishCategory> GetAllCategories() =>
            _db.DishCategories.OrderBy(n => n.SortOrder).ThenBy(n => n.Id).AsNoTracking().ToList();

        public IEnumerable<DishCategory> GetActiveCategories() =>
            GetAllCategories().Where(n => n.IsActive);

        public DishCategory? GetCategory(int id) =>
            _db.DishCategories.AsNoTracking().FirstOrDefault(n => n.Id == id);

        public DishCategory AddCategory(DishCategory category)
        {
            var name = ValidateCategoryName(category.Name);
            if (category.SortOrder < 0)
                throw new ValidationException("Thứ tự hiển thị phải là số nguyên không âm.");
            category.Name = name;
            _db.DishCategories.Add(category);
            _db.SaveChanges();
            return category;
        }

        public bool RenameCategory(int id, string? name)
        {
            var category = _db.DishCategories.FirstOrDefault(n => n.Id == id);
            if (category == null) return false;

            category.Name = ValidateCategoryName(name, id);
            _db.SaveChanges();
            return true;
        }

        public bool SetCategoryActive(int id, bool isActive)
        {
            var category = _db.DishCategories.FirstOrDefault(n => n.Id == id);
            if (category == null) return false;

            category.IsActive = isActive;
            _db.SaveChanges();
            return true;
        }

        public bool DeleteCategory(int id)
        {
            var category = _db.DishCategories.FirstOrDefault(n => n.Id == id);
            if (category == null) return false;

            var count = _db.Dishes.Count(m => m.CategoryId == id);
            if (count > 0)
                throw new ValidationException($"Không thể xóa nhóm đang chứa {count} món ăn. Vui lòng chuyển toàn bộ món sang nhóm khác trước khi xóa.");

            _db.DishCategories.Remove(category);
            _db.SaveChanges();

            var remaining = _db.DishCategories.OrderBy(n => n.SortOrder).ThenBy(n => n.Id).ToList();
            for (var i = 0; i < remaining.Count; i++)
                remaining[i].SortOrder = i + 1;
            _db.SaveChanges();

            return true;
        }

        public void SaveCategoryOrder(IReadOnlyList<int> ids, IReadOnlyList<int> originalIds)
        {
            var current = _db.DishCategories.OrderBy(n => n.SortOrder).ThenBy(n => n.Id).Select(n => n.Id).ToArray();
            if (!current.SequenceEqual(originalIds))
                throw new ValidationException("Danh sách hoặc thứ tự nhóm món đã thay đổi. Vui lòng tải lại trang và sắp xếp lại.");
            if (ids.Count != current.Length || ids.Distinct().Count() != ids.Count || !ids.ToHashSet().SetEquals(current))
                throw new ValidationException("Thứ tự không hợp lệ: cần đủ mỗi nhóm món đúng một lần.");

            for (var i = 0; i < ids.Count; i++)
            {
                var category = _db.DishCategories.FirstOrDefault(n => n.Id == ids[i]);
                if (category != null) category.SortOrder = i + 1;
            }
            _db.SaveChanges();
        }

        private string ValidateCategoryName(string? name, int? exceptId = null)
        {
            var normalized = CategoryNameRules.Normalize(name);
            if (normalized.Length == 0) throw new ValidationException(CategoryNameRules.EmptyName);
            if (normalized.Length > 50) throw new ValidationException(CategoryNameRules.NameTooLong);
            if (_db.DishCategories.AsNoTracking().Where(n => !exceptId.HasValue || n.Id != exceptId.Value)
                .Select(n => n.Name).AsEnumerable().Any(name =>
                    string.Equals(CategoryNameRules.Normalize(name), normalized, StringComparison.OrdinalIgnoreCase)))
                throw new ValidationException(CategoryNameRules.DuplicateName);
            return normalized;
        }

        public IEnumerable<Dish> GetAllDishes() =>
            _db.Dishes.OrderBy(m => m.Id).AsNoTracking().ToList();

        public IEnumerable<Dish> GetDishesByCategory(int categoryId) =>
            _db.Dishes.Where(m => m.CategoryId == categoryId).OrderBy(m => m.Id).AsNoTracking().ToList();

        public Dish AddDish(Dish dish)
        {
            EnsureCategoryExists(dish.CategoryId);
            _db.Dishes.Add(dish);
            _db.SaveChanges();
            return dish;
        }

        public Dish? GetDish(int id) =>
            _db.Dishes.AsNoTracking().FirstOrDefault(m => m.Id == id);

        public IReadOnlyList<CategoryWithDishes> GetMenuByCategory()
        {
            var onSaleDishes = _db.Dishes.AsNoTracking()
                .Where(m => m.Status == DishStatus.OnSale)
                .ToList()
                .ToLookup(m => m.CategoryId);

            return GetActiveCategories()
                .Select(n => new CategoryWithDishes(n.Id, n.Name, onSaleDishes[n.Id].ToArray()))
                .ToList();
        }

        // Đọc qua view dbo.vw_PublicMenu (migration 004): chỉ món IsActive=1 thuộc nhóm IsActive=1,
        // ảnh = ảnh món hoặc ảnh mặc định của nhóm, IsSoldOut chỉ đúng trong ngày nghiệp vụ UTC+7 hiện tại.
        public IReadOnlyList<PublicMenuCategory> GetPublicMenu()
        {
            var category = GetActiveCategories().Select(n => (n.Id, n.Name)).ToList();
            var dish = _db.Database.SqlQuery<PublicMenuRow>($"""
                SELECT Id, CategoryId, Name, CAST(Price AS int) AS Price, Unit, Description,
                       ImagePath, SortOrder, IsSoldOut
                FROM dbo.vw_PublicMenu
                """)
                .AsEnumerable()
                .Select(m => new OnSaleDishRow(m.Id, m.CategoryId, m.Name, m.Description, m.Price,
                    m.Unit, m.ImagePath, m.SortOrder, m.IsSoldOut))
                .ToList();
            return PublicMenuBuilder.Build(category, dish);
        }

        internal sealed class PublicMenuRow
        {
            public int Id { get; set; }
            public int CategoryId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Price { get; set; }
            public string Unit { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string? ImagePath { get; set; }
            public int SortOrder { get; set; }
            public bool IsSoldOut { get; set; }
        }

        private void EnsureCategoryExists(int categoryId)
        {
            if (!_db.DishCategories.AsNoTracking().Any(n => n.Id == categoryId))
                throw new ArgumentException("Nhóm món không tồn tại.", nameof(categoryId));
        }

        public Dish? UpdateDish(Dish updated)
        {
            var existing = _db.Dishes.FirstOrDefault(m => m.Id == updated.Id);
            if (existing == null) return null;

            if (!_user.IsAuthenticated)
                throw new UnauthorizedAccessException("Chưa đăng nhập.");

            var userName = _user.UserName;
            var actorUserId = _db.Database.SqlQuery<int>($"SELECT Id AS Value FROM dbo.Users WHERE UserName = {userName} AND IsActive = 1")
                .SingleOrDefault();
            if (actorUserId == 0)
                throw new UnauthorizedAccessException("Tài khoản không tồn tại hoặc đã ngừng hoạt động.");
            EnsureCategoryExists(updated.CategoryId);
            using var transaction = _db.Database.BeginTransaction();
            _db.Database.ExecuteSqlInterpolated($"EXEC dbo.usp_RequirePermission {actorUserId}, 'Catalog.Manage'");

            var oldPrice = existing.PriceVnd;
            var newPrice = updated.PriceVnd;

            existing.Name = updated.Name;
            existing.CategoryId = updated.CategoryId;
            existing.Unit = updated.Unit;
            existing.ShortDescription = updated.ShortDescription;
            existing.PrepMinutes = updated.PrepMinutes;
            existing.Status = updated.Status;
            existing.ImagePath = updated.ImagePath;

            if (oldPrice != newPrice)
            {
                _db.Database.ExecuteSqlInterpolated($"EXEC dbo.usp_UpdateMenuPrice {existing.Id}, {newPrice}, {actorUserId}, {_user.IpAddress}");
            }

            _db.SaveChanges();
            transaction.Commit();
            _db.Entry(existing).Reload();
            return existing;
        }

        public Order CreateOrder()
        {
            var dh = new Order { CreatedAt = DateTime.UtcNow, IsOpen = true };
            _db.Orders.Add(dh);
            _db.SaveChanges();
            return dh;
        }

        public Order? GetOrder(int orderId) =>
            _db.Orders.AsNoTracking().FirstOrDefault(d => d.Id == orderId);

        public OrderLine AddOrderLine(int orderId, OrderLine line)
        {
            line.OrderId = orderId;
            _db.OrderLines.Add(line);
            _db.SaveChanges();
            return line;
        }

        public IEnumerable<OrderLine> GetOrderLines(int orderId) =>
            _db.OrderLines.Where(d => d.OrderId == orderId).OrderBy(d => d.Id).AsNoTracking().ToList();

        public IEnumerable<Order> GetAllOrders() =>
            _db.Orders.OrderBy(d => d.Id).AsNoTracking().ToList();

        public IEnumerable<PriceChangeLog> GetPriceHistory(int dishId) =>
            _db.Database.SqlQuery<PriceChangeLog>($"""
                SELECT CAST(h.Id AS int) AS Id, h.MenuItemId AS DishId, m.Name AS DishName,
                       CAST(h.OldPrice AS int) AS OldPriceVnd, CAST(h.NewPrice AS int) AS NewPriceVnd,
                       h.ChangedAt AS ChangedAt, u.UserName AS ChangedBy
                FROM dbo.MenuPriceHistory h
                JOIN dbo.MenuItems m ON m.Id = h.MenuItemId
                JOIN dbo.Users u ON u.Id = h.ChangedBy
                WHERE h.MenuItemId = {dishId}
                """).OrderByDescending(e => e.ChangedAt).ToList();

        public IEnumerable<PriceChangeLog> GetRecentPriceChanges(int count) =>
            _db.Database.SqlQuery<PriceChangeLog>($"""
                SELECT CAST(h.Id AS int) AS Id, h.MenuItemId AS DishId, m.Name AS DishName,
                       CAST(h.OldPrice AS int) AS OldPriceVnd, CAST(h.NewPrice AS int) AS NewPriceVnd,
                       h.ChangedAt AS ChangedAt, u.UserName AS ChangedBy
                FROM dbo.MenuPriceHistory h
                JOIN dbo.MenuItems m ON m.Id = h.MenuItemId
                JOIN dbo.Users u ON u.Id = h.ChangedBy
                """).OrderByDescending(e => e.ChangedAt).ThenByDescending(e => e.Id).Take(count).ToList();
    }
}
