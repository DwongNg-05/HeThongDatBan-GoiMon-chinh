# S2-01 Task 3: Món tạm hết trong ngày

Món đã hết trong ngày vẫn hiển thị trên thực đơn khách (`/Menu`), nhưng có nhãn **“Tạm hết”** và bị làm mờ để khách biết món hiện không phục vụ. Khi khách tìm kiếm theo tên, món tạm hết vẫn xuất hiện trong kết quả. Khi nhân viên mở bán lại, món hiển thị bình thường.

**AC hoàn tất:** AC1, AC2, AC3.

**Chưa làm:** tối ưu bố cục cho nhiều kích thước màn hình, kiểm thử tải với 200 món.

## Lưu và đọc trạng thái

Cơ sở dữ liệu đã có sẵn cơ chế lưu trạng thái này. Task 3 chỉ dùng lại, không thêm bảng mới.

| Bước | Cách hoạt động |
| --- | --- |
| Lưu | Vào **Quản lý món**, bấm nút **Báo hết** hoặc **Mở bán lại**. Nút gọi `ManagementController.Availability`, sau đó gọi `usp_SetMenuAvailability`. Thủ tục ghi `MenuItems.IsSoldOut`, `SoldOutBusinessDate = ngày nghiệp vụ (UTC+7)` và thêm một dòng vào `MenuAvailabilityEvents`. Cần quyền `Menu.Availability`. |
| Đọc (thực đơn khách) | `vw_PublicMenu` chỉ trả về `IsSoldOut = 1` khi `SoldOutBusinessDate` là ngày nghiệp vụ hôm nay. `SqlMenuStore.GetPublicMenu` đưa giá trị này vào `PublicMenuDish.SoldOutToday`. |
| Hết hạn | Sang ngày nghiệp vụ mới, cờ tự hết hiệu lực. `usp_RunMaintenance` dọn các cờ cũ. |
| Đọc (Quản lý món) | **Migration `025_SoldOutToday.sql`** sửa `usp_ManagementMenu` để dùng cùng quy tắc ngày nghiệp vụ. Trước đây, sau nửa đêm nhân viên vẫn thấy “Tạm hết” trong khi khách thấy món bình thường. |

Lưu ý:
- **Ngừng bán** khác với **tạm hết**. Món ngừng bán bị ẩn hoàn toàn khỏi thực đơn. Món tạm hết vẫn hiển thị.
- Món tạm hết vẫn ở đúng nhóm, đúng vị trí, giữ nguyên ảnh, mô tả và giá.

## Hiển thị

- Mỗi món có thuộc tính `data-sold-out="true|false"`. Món tạm hết có thêm nhãn `<span class="public-dish-status">Tạm hết</span>` ngay trên tên món. Nhãn kèm một câu ẩn cho trình đọc màn hình: “Món này hiện không phục vụ trong hôm nay.”
- CSS trong `public-menu.css` làm mờ món tạm hết: ảnh chuyển sang xám và giảm độ đậm còn 45%; tên, mô tả và giá giảm còn 55%. Nhãn “Tạm hết” không bị làm mờ nên vẫn dễ đọc.
- Khi tìm kiếm (Task 2), món tạm hết vẫn nằm trong kết quả và giữ nhãn. API `/api/menu?q=` trả về `soldOutToday: true` cho món này.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/025_SoldOutToday.sql` (mới) |
| Service | `Services/Menu/PublicMenu.cs`: thêm `IsTemporarilyUnavailable`, `AvailabilityLabel` và `SoldOutDisplay`. `Services/Menu/InMemoryMenuStore.cs`: thêm `SetSoldOutToday` và `IsSoldOutToday`, dùng cho test. |
| View/CSS | `Views/Menu/Index.cshtml`, `wwwroot/css/public-menu.css` |
| Test | `tests/.../SoldOutTests.cs` (mới); `tools/.../PublicMenuVerification.cs`: thêm `VerifySoldOut` |

## Demo

```powershell
cd D:\HeThongDatBan-GoiMon-Chinh
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project tools/RestaurantManagement.DbTool -- seed-menu-demo
dotnet run --project src/RestaurantManagement.Web
```

1. Đăng nhập bằng tài khoản quản lý, vào **Quản lý món**, bấm **Báo hết** ở món “Cơm rang dưa bò”.
2. Mở `/Menu` ở cửa sổ ẩn danh. Món vẫn nằm trong nhóm *Món chính*, có nhãn **Tạm hết**, ảnh xám và chữ mờ.
3. Tìm `com rang`. Món vẫn hiện trong kết quả, kèm nhãn “Tạm hết”.
4. Quay lại **Quản lý món**, bấm **Mở bán lại**. Tải lại `/Menu`: món hiển thị bình thường.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

**`SoldOutTests`** (không cần database) kiểm tra:
- món còn bán không có nhãn;
- món tạm hết có nhãn “Tạm hết”, vẫn ở đúng nhóm và vị trí, giữ nguyên ảnh, tên, mô tả và giá;
- chỉ món được đánh dấu mới bị tạm hết;
- tìm `com rang`, `Cơm rang dưa bò`, `COM RANG DUA BO` vẫn ra món tạm hết kèm nhãn, qua cả `/Menu?q=` lẫn API;
- mở bán lại thì món hiển thị bình thường;
- món ngừng bán bị ẩn, khác với món tạm hết.

**`PublicMenuVerification.VerifySoldOut`** (lệnh `verify`, chạy trên SQL Server thật, client chưa đăng nhập) kiểm tra:
- đánh dấu món bằng `usp_SetMenuAvailability`, rồi mở `/Menu`: món có `data-sold-out="true"` và nhãn “Tạm hết”, giữ nguyên ảnh, mô tả và giá; các món khác vẫn bình thường;
- tìm kiếm có dấu và không dấu đều ra món tạm hết;
- API trả về `soldOutToday`;
- thay đổi được ghi vào `MenuAvailabilityEvents`;
- cờ của ngày hôm trước không còn hiệu lực, cả ở `/Menu` lẫn ở `usp_ManagementMenu`;
- mở bán lại thì món hiển thị bình thường.
