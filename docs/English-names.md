# Tên tiếng Anh trong code (đổi tên ngày 02/10/2026)

Chỉ đổi **tên trong code** (class, file, thư mục, thuộc tính, biến, route). Chữ hiển thị cho nhân viên/khách vẫn là tiếng Việt. Database (bảng, cột, stored procedure, migration, dữ liệu mẫu, đường dẫn ảnh `/images/thuc-don/...`, `/uploads/mon-an/...`) **không đổi**.

## Đường dẫn (URL)

| Cũ | Mới |
| --- | --- |
| `/ThucDon` | `/Menu` |
| `/api/thuc-don` | `/api/menu` |
| `/GoiMon`, `/GoiMon/Checkout` | `/Ordering`, `/Ordering/Checkout` |
| `/DonHang/Details/{id}` | `/Orders/Details/{id}` |
| `/QuanLyMon`, `/Tao`, `/Sua/{id}`, `/NhatKyGia/{id}` | `/Dishes`, `/Dishes/Create`, `/Dishes/Edit/{id}`, `/Dishes/PriceHistory/{id}` |
| `/QuanLyNhomMon`, `/Tao`, `/Sua`, `/SapXep`, `/TrangThai`, `/Xoa` | `/DishCategories`, `/Create`, `/Edit`, `/Reorder`, `/Status`, `/Delete` |
| `/Account/DoiMatKhau` | `/Account/ChangePassword` |
| `/Account/XacMinhEmail`, `/GuiLaiMaXacMinh`, `/DatEmailXacMinh` | `/Account/VerifyEmail`, `/ResendVerificationCode`, `/SetVerificationEmail` |

`/ThucDon`, `/api/thuc-don`, `/GoiMon`, `/QuanLyMon`, `/QuanLyNhomMon` tự chuyển (301) sang địa chỉ mới.

## Tên chính

| Cũ | Mới |
| --- | --- |
| `MonAn` (Ten, NhomMonId, GiaBanVnd, DonViTinh, MoTaNgan, ThoiGianCheBienPhut, TrangThai, DuongDanAnh) | `Dish` (Name, CategoryId, PriceVnd, Unit, ShortDescription, PrepMinutes, Status, ImagePath) |
| `TrangThaiMon.DangBan / NgungBan` | `DishStatus.OnSale / Discontinued` |
| `NhomMon` (Ten, DangSuDung, ThuTuHienThi) | `DishCategory` (Name, IsActive, SortOrder) |
| `DonHang`, `DongHang` | `Order`, `OrderLine` |
| `GhiNhanThayDoiGia` | `PriceChangeLog` |
| `IQuanLyMonStore`, `SqlQuanLyMonStore`, `InMemoryQuanLyMonStore` | `IMenuStore`, `SqlMenuStore`, `InMemoryMenuStore` |
| `LayTatCaMonAn`, `LayMonAn`, `ThemMonAn`, `CapNhatMonAn` | `GetAllDishes`, `GetDish`, `AddDish`, `UpdateDish` |
| `LayTatCaNhomMon`, `AddNhomMon`, `SuaTenNhomMon`, `DatTrangThaiNhomMon`, `XoaNhomMon`, `LuuThuTuNhomMon` | `GetAllCategories`, `AddCategory`, `RenameCategory`, `SetCategoryActive`, `DeleteCategory`, `SaveCategoryOrder` |
| `LayThucDonTheoNhom`, `LayThucDonCongKhai` | `GetMenuByCategory`, `GetPublicMenu` |
| `NhomThucDonCongKhai`, `MonThucDonCongKhai` | `PublicMenuCategory`, `PublicMenuDish` (JSON `/api/menu`: `name`, `dishes`, `priceVnd`, `displayPrice`, `imageUrl`, `shortDescription`, `soldOutToday`) |
| `QuyTacAnhMonAn`, `IKhoAnhMonAn`, `AnhMonAn` | `DishImageRules`, `IDishImageStorage`, `DishImage` |
| `ThucDonController`, `ThucDonApiController` | `MenuController`, `MenuApiController` |

Bảng EF không có trong SQL (`DonHangs`, `DongHangs`, `GhiNhanThayDoiGias`) giữ nguyên tên bảng.
