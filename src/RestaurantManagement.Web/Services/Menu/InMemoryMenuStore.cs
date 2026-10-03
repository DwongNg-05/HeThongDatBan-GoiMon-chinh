using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using RestaurantManagement.Data.Models;

namespace RestaurantManagement.Web.Services
{
    public class InMemoryMenuStore : IMenuStore
    {

        private readonly ConcurrentDictionary<int, Dish> _dishes = new();
        private readonly ConcurrentDictionary<int, DishCategory> _categories = new();
        private readonly ConcurrentDictionary<int, Order> _orders = new();
        private readonly ConcurrentDictionary<int, OrderLine> _orderLines = new();
        private readonly ConcurrentDictionary<int, RestaurantManagement.Data.Models.PriceChangeLog> _priceHistory = new();
        private int _nextPriceLogId = 0;
        private int _nextDishId = 0;
        private int _nextCategoryId = 0;
        private readonly object _categoryLock = new();
        private int _nextOrderId = 0;
        private int _nextOrderLineId = 0;

        public InMemoryMenuStore()
        {
            var defaults = new[]
            {
                ("Khai vị", "Gỏi cuốn", 45000, "Đĩa", "Tôm, thịt, bún và rau sống cuốn bánh tráng, chấm tương đậu.", "/images/thuc-don/khai-vi.svg"),
                ("Món chính", "Cơm chiên hải sản", 85000, "Đĩa", "Cơm chiên tôm mực, trứng và rau củ, đảo lửa lớn.", "/images/thuc-don/mon-chinh.svg"),
                ("Lẩu", "Lẩu Thái", 250000, "Nồi", "Nước lẩu chua cay, hải sản tươi, nấm và rau ăn kèm cho 2–3 người.", "/images/thuc-don/lau.svg"),
                ("Tráng miệng", "Chè hạt sen", 30000, "Chén", "Hạt sen bùi, nước đường phèn thanh mát.", "/images/thuc-don/trang-mieng.svg"),
                ("Đồ uống", "Trà đào", 35000, "Ly", "Trà đen ủ lạnh với đào miếng và sả.", "/images/thuc-don/do-uong.svg")
            };
            for (var index = 0; index < defaults.Length; index++)
            {
                var (categoryName, dishName, price, unit, description, image) = defaults[index];
                var category = AddCategory(new DishCategory { Name = categoryName, SortOrder = index + 1 });
                AddDish(new Dish
                {
                    Name = dishName, CategoryId = category.Id, PriceVnd = price,
                    Unit = unit, PrepMinutes = 15,
                    ShortDescription = description, ImagePath = image
                });
            }
        }

      
        public IEnumerable<DishCategory> GetAllCategories()
        {
            lock (_categoryLock)
                return _categories.Values.OrderBy(n => n.SortOrder).ThenBy(n => n.Id).ToArray();
        }

        public void SaveCategoryOrder(IReadOnlyList<int> ids, IReadOnlyList<int> originalIds)
        {
            lock (_categoryLock)
            {
                var current = GetAllCategories().Select(n => n.Id).ToArray();
                if (!current.SequenceEqual(originalIds))
                    throw new ValidationException("Danh sách hoặc thứ tự nhóm món đã thay đổi. Vui lòng tải lại trang và sắp xếp lại.");
                if (ids.Count != current.Length || ids.Distinct().Count() != ids.Count || !ids.ToHashSet().SetEquals(current))
                    throw new ValidationException("Thứ tự không hợp lệ: cần đủ mỗi nhóm món đúng một lần.");
                for (var i = 0; i < ids.Count; i++)
                {
                    var category = _categories[ids[i]];
                    _categories[ids[i]] = new DishCategory
                    {
                        Id = category.Id, Name = category.Name, IsActive = category.IsActive, SortOrder = i + 1
                    };
                }
            }
        }

        public IEnumerable<DishCategory> GetActiveCategories() => GetAllCategories().Where(n => n.IsActive);

        public IEnumerable<Dish> GetDishesByCategory(int categoryId) =>
            GetAllDishes().Where(m => m.CategoryId == categoryId);

        public IReadOnlyList<CategoryWithDishes> GetMenuByCategory()
        {
            var onSaleDishes = GetAllDishes().Where(m => m.Status == DishStatus.OnSale)
                .ToLookup(m => m.CategoryId);
            return GetActiveCategories()
                .Select(n => new CategoryWithDishes(n.Id, n.Name, onSaleDishes[n.Id].ToArray())).ToArray();
        }

        // S2-01 Task 3: món hết trong ngày (bản SQL lưu ở MenuItems.IsSoldOut + SoldOutBusinessDate).
        // Store bộ nhớ chỉ dùng cho test, không có khái niệm sang ngày mới.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _soldOutToday = new();

        /// <summary>Đánh dấu món hết trong ngày (true) hoặc còn bán lại (false).</summary>
        public bool SetSoldOutToday(int dishId, bool soldOut)
        {
            if (!_dishes.ContainsKey(dishId)) return false;
            if (soldOut) _soldOutToday[dishId] = true;
            else _soldOutToday.TryRemove(dishId, out _);
            return true;
        }

        public bool IsSoldOutToday(int dishId) => _soldOutToday.ContainsKey(dishId);

        public IReadOnlyList<PublicMenuCategory> GetPublicMenu() =>
            PublicMenuBuilder.Build(
                GetActiveCategories().Select(n => (n.Id, n.Name)),
                GetAllDishes().Where(m => m.Status == DishStatus.OnSale)
                    .Select(m => new OnSaleDishRow(m.Id, m.CategoryId, m.Name, m.ShortDescription, m.PriceVnd,
                        m.Unit, m.ImagePath, 0, IsSoldOutToday(m.Id))));

        // no external DB integration in the in-memory store

    
        public DishCategory AddCategory(DishCategory category)
        {
            lock (_categoryLock)
            {
                var name = ValidateCategoryName(category.Name);
                if (category.SortOrder < 0)
                    throw new ValidationException("Thứ tự hiển thị phải là số nguyên không âm.");
                category.Id = ++_nextCategoryId;
                category.Name = name;
                _categories[category.Id] = category;
                return category;
            }
        }

        public DishCategory? GetCategory(int id) => _categories.GetValueOrDefault(id);

        public bool RenameCategory(int id, string? name)
        {
            lock (_categoryLock)
            {
                if (!_categories.TryGetValue(id, out var category)) return false;
                var validName = ValidateCategoryName(name, id);
                _categories[id] = new DishCategory
                {
                    Id = id, Name = validName,
                    IsActive = category.IsActive, SortOrder = category.SortOrder
                };
                return true;
            }
        }

        public bool SetCategoryActive(int id, bool isActive)
        {
            lock (_categoryLock)
            {
                if (!_categories.TryGetValue(id, out var category)) return false;
                _categories[id] = new DishCategory
                {
                    Id = id, Name = category.Name, SortOrder = category.SortOrder, IsActive = isActive
                };
                return true;
            }
        }

        public bool DeleteCategory(int id)
        {
            lock (_categoryLock)
            {
                if (!_categories.ContainsKey(id)) return false;
                var count = _dishes.Values.Count(m => m.CategoryId == id);
                if (count > 0)
                    throw new ValidationException($"Không thể xóa nhóm đang chứa {count} món ăn. Vui lòng chuyển toàn bộ món sang nhóm khác trước khi xóa.");
                _categories.TryRemove(id, out _);
                var remaining = GetAllCategories().Select(n => n.Id).ToArray();
                SaveCategoryOrder(remaining, remaining);
                return true;
            }
        }

        private string ValidateCategoryName(string? name, int? exceptId = null)
        {
            var normalized = CategoryNameRules.Normalize(name);
            if (normalized.Length == 0) throw new ValidationException(CategoryNameRules.EmptyName);
            if (normalized.Length > 50) throw new ValidationException(CategoryNameRules.NameTooLong);
            if (_categories.Values.Any(n => n.Id != exceptId &&
                string.Equals(CategoryNameRules.Normalize(n.Name), normalized, StringComparison.OrdinalIgnoreCase)))
                throw new ValidationException(CategoryNameRules.DuplicateName);
            return normalized;
        }

        public IEnumerable<Dish> GetAllDishes() => _dishes.Values.OrderBy(m => m.Id);

        public Dish AddDish(Dish dish)
        {
            lock (_categoryLock)
            {
                EnsureCategoryExists(dish.CategoryId);
                var id = System.Threading.Interlocked.Increment(ref _nextDishId);
                dish.Id = id;
                _dishes.TryAdd(dish.Id, dish);
                return dish;
            }
        }

        // Lấy món theo id
        public Dish? GetDish(int id) => _dishes.TryGetValue(id, out var dish) ? dish : null;

        // Cập nhật món (thay đổi giá, tên, mô tả, trạng thái...).
        // Lưu ý: không chạm vào các OrderLine đã lưu - chúng đã snapshot giá tại thời điểm gọi.
        public Dish? UpdateDish(Dish updated)
        {
            lock (_categoryLock)
            {
                if (!_dishes.ContainsKey(updated.Id)) return null;
                EnsureCategoryExists(updated.CategoryId);
                // retrieve existing for comparison
                var existing = _dishes[updated.Id];

                var oldPrice = existing.PriceVnd;
                var newPrice = updated.PriceVnd;

                // Replace atomically and update fields
                _dishes.AddOrUpdate(updated.Id, updated, (key, old) =>
                {
                    old.Name = updated.Name;
                    old.CategoryId = updated.CategoryId;
                    old.PriceVnd = updated.PriceVnd;
                    old.Unit = updated.Unit;
                    old.ShortDescription = updated.ShortDescription;
                    old.PrepMinutes = updated.PrepMinutes;
                    old.Status = updated.Status;
                    old.ImagePath = updated.ImagePath;
                    return old;
                });

                // If price changed, record a journal entry
                if (oldPrice != newPrice)
                {
                    var gid = System.Threading.Interlocked.Increment(ref _nextPriceLogId);
                    var entry = new RestaurantManagement.Data.Models.PriceChangeLog
                    {
                        Id = gid,
                        DishId = updated.Id,
                        DishName = updated.Name,
                        OldPriceVnd = oldPrice,
                        NewPriceVnd = newPrice,
                        ChangedAt = DateTime.UtcNow,
                        ChangedBy = GetCurrentUserName() ?? "(không rõ)"
                    };
                    _priceHistory.TryAdd(entry.Id, entry);
                }

                return _dishes[updated.Id];
            }
        }

        // Lấy nhật ký thay đổi giá cho một món theo thời gian giảm dần (gần nhất trước)
        public IEnumerable<RestaurantManagement.Data.Models.PriceChangeLog> GetPriceHistory(int dishId)
            => _priceHistory.Values.Where(e => e.DishId == dishId).OrderByDescending(e => e.ChangedAt);

        // Các lần đổi giá gần nhất của mọi món (gần nhất trước)
        public IEnumerable<RestaurantManagement.Data.Models.PriceChangeLog> GetRecentPriceChanges(int count)
            => _priceHistory.Values.OrderByDescending(e => e.ChangedAt).Take(count).ToList();

        // Very small helper to resolve current user - in this demo read from environment thread principal if available
        private string? GetCurrentUserName()
        {
            try
            {
                var name = System.Security.Claims.ClaimsPrincipal.Current?.Identity?.Name;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
            return Environment.UserName;
        }

        // Tạo đơn hàng mới và trả về Order
        public Order CreateOrder()
        {
            var id = System.Threading.Interlocked.Increment(ref _nextOrderId);
            var dh = new Order { Id = id, CreatedAt = DateTime.UtcNow, IsOpen = true };
            _orders.TryAdd(dh.Id, dh);
            return dh;
        }

        // Lấy đơn hàng theo id
        public Order? GetOrder(int orderId) => _orders.TryGetValue(orderId, out var dh) ? dh : null;

        // Thêm dòng hàng vào đơn: lưu snapshot tên, giá, đơn vị
        public OrderLine AddOrderLine(int orderId, OrderLine line)
        {
            var id = System.Threading.Interlocked.Increment(ref _nextOrderLineId);
            line.Id = id;
            line.OrderId = orderId;
            _orderLines.TryAdd(line.Id, line);
            return line;
        }

        // Lấy các dòng hàng của một đơn
        public IEnumerable<OrderLine> GetOrderLines(int orderId) => _orderLines.Values.Where(d => d.OrderId == orderId).OrderBy(d => d.Id);

        // Lấy tất cả đơn hàng (dùng cho demo/admin)
        public IEnumerable<Order> GetAllOrders() => _orders.Values.OrderBy(d => d.Id);

        private void EnsureCategoryExists(int categoryId)
        {
            if (!_categories.ContainsKey(categoryId))
                throw new ArgumentException("Nhóm món không tồn tại.", nameof(categoryId));
        }
    }
}
