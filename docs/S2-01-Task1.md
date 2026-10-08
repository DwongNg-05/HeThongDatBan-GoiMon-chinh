# S2-01 Task 1 — Thực đơn công khai không cần đăng nhập

Khách mở `/Menu` trên điện thoại khi chưa đăng nhập và xem các nhóm món cùng ảnh, tên, mô tả ngắn và giá VND. AC hoàn tất: **AC1**.

## Quy tắc trạng thái món (đề xuất, cần PO xác nhận)

Quy tắc dựa trên các cột đã có trong `001_Schema.sql` và view `dbo.vw_PublicMenu` (migration 004), nên không cần migration mới.

| Trạng thái | Dữ liệu | Thực đơn công khai |
| --- | --- | --- |
| **Đang bán** | `MenuItems.IsActive = 1` và nhóm `MenuCategories.IsActive = 1` | Hiển thị |
| **Hết trong ngày** (tạm hết) | Đang bán và `IsSoldOut = 1` với `SoldOutBusinessDate` = ngày nghiệp vụ hiện tại (giờ Việt Nam, UTC+7) | Vẫn hiển thị. Cờ `hetTrongNgay` đã có trong dữ liệu trả về; nhãn “Tạm hết” và chặn gọi món thuộc task sau |
| **Ngừng bán** | `IsActive = 0` (trong code: `DishStatus.Discontinued`) | Ẩn |
| Nhóm ngừng sử dụng | `MenuCategories.IsActive = 0` | Ẩn cả nhóm và mọi món trong nhóm |

- “Hết trong ngày” tự hết hiệu lực khi sang ngày nghiệp vụ mới, vì so sánh với `SoldOutBusinessDate`. Nhân viên không phải mở bán lại thủ công. `usp_RunMaintenance` dọn cờ cũ.
- Quản lý chuyển món giữa *Đang bán* và *Ngừng bán* trong **Quản lý món**. Nhân viên có quyền `Menu.Availability` đánh dấu *hết trong ngày* qua `usp_SetMenuAvailability`.
- Nhóm đang sử dụng nhưng chưa có món nào đang bán vẫn hiển thị kèm dòng “Nhóm này chưa có món.”, giống trang gọi món hiện tại.
- Thứ tự: nhóm theo `SortOrder` rồi `Id`; món theo `MenuItems.SortOrder` rồi `Id`.

Nếu PO chọn cách khác (ví dụ ẩn món hết trong ngày), chỉ cần sửa `PublicMenuBuilder` hoặc điều kiện trong `SqlMenuStore.GetPublicMenu`.

## Thay đổi

- **MVC**: `Controllers/MenuController.cs` (`[AllowAnonymous]`, action `Index`) trả về view `Views/Menu/Index.cshtml`. Trang Razor cũ `Pages/Menu` được thay thế; **xoá thư mục `src/RestaurantManagement.Web/Pages/Menu`** (hai file còn lại chỉ là ghi chú, không tạo route).
- **Trang `/Menu`** chỉ để xem, ưu tiên màn hình điện thoại: thanh nhóm món cuộn ngang, mỗi món có ảnh, tên, mô tả ngắn (tối đa 3 dòng), giá và đơn vị tính. Trang không có giỏ món; giỏ gọi món của nhân viên vẫn ở `/Ordering` và vẫn cần đăng nhập.
- **API `GET /api/menu`** — `Controllers/MenuApiController.cs` (`[ApiController]`, `[AllowAnonymous]`), không cần đăng nhập, trả về danh sách nhóm, mỗi nhóm có `monAn[]` gồm `id`, `ten`, `moTaNgan`, `giaVnd`, `giaHienThi`, `donViTinh`, `anhUrl` và `hetTrongNgay`.
- **Giá VND** hiển thị dạng `45.000 ₫` (dấu chấm phân tách hàng nghìn, ký hiệu ₫). Cách định dạng không phụ thuộc cấu hình ngôn ngữ của máy chủ; xem `MoneyFormat.Vnd`.
- **Ảnh**: ưu tiên `MenuItems.ImagePath`, sau đó đến `MenuCategories.DefaultImagePath`, cuối cùng là `/images/thuc-don/mac-dinh.svg`. Hệ thống chỉ nhận đường dẫn nội bộ `/…` hoặc `https://…`. Ảnh mẫu là hình minh hoạ SVG theo nhóm trong `wwwroot/images/thuc-don/`; quản lý thay bằng ảnh thật qua **Quản lý món → Tạo món / Sửa** (xem mục bên dưới).
- `Dish.ImagePath` ánh xạ cột `ImagePath`. Thao tác sửa món hiện tại không xoá ảnh.

## Dữ liệu mẫu

```powershell
$env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project tools/RestaurantManagement.DbTool -- seed-menu-demo
```

Lệnh `seed-menu-demo` không cần mật khẩu, nạp được vào database đã có dữ liệu và có thể chạy lại nhiều lần. Lệnh tạo 5 nhóm (Khai vị, Món chính, Lẩu, Tráng miệng, Đồ uống) kèm ảnh mặc định, 15 món đang bán có ảnh, mô tả và giá VND, cùng 1 món ngừng bán (“Cua rang me”) để kiểm tra món này bị ẩn. Nhóm hoặc món đã có cùng tên được giữ nguyên; giá, mô tả và trạng thái do quản lý chỉnh sửa không bị ghi đè. Không chạy lệnh trên database sản xuất.

## Demo trên điện thoại

1. Chạy web cho máy khác truy cập trong mạng LAN: `dotnet run --project src/RestaurantManagement.Web --urls http://0.0.0.0:5105`. Có thể phải mở cổng trên Windows Firewall.
2. Trên điện thoại cùng mạng Wi-Fi, mở `http://<IP-máy-tính>:5105/Menu`. Đây là trình duyệt chưa đăng nhập.
3. Kiểm tra: trang mở ngay, không chuyển sang màn hình đăng nhập; chạm từng nhóm để đến đúng nhóm; mỗi món có ảnh, tên, mô tả và giá dạng `45.000 ₫`; không thấy “Cua rang me”.
4. Đăng nhập trên máy tính, vào **Quản lý món**, chuyển một món sang *Ngừng bán*. Tải lại trang trên điện thoại: món đó biến mất.

Không commit địa chỉ IP hoặc chuỗi kết nối cá nhân.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

- `AreaTests` (không cần database) gồm `PublicMenuTests`: định dạng VND, chọn ảnh và loại URL không an toàn, đủ ảnh/tên/mô tả/giá cho từng món, thứ tự nhóm và món, ẩn món ngừng bán, ẩn nhóm ngừng sử dụng, vẫn hiện nhóm rỗng, `MenuController.Index` trả về view có đúng danh sách nhóm, `MenuApiController` trả về cùng dữ liệu, cả hai có `[AllowAnonymous]` còn `/Ordering` thì không.
- `verify` (SQL Server, database kiểm thử riêng) gồm `PublicMenuVerification`: nạp `seed-menu-demo` hai lần không trùng; mở `/Menu` khi **chưa đăng nhập** và nhận 200; kiểm tra danh sách nhóm theo thứ tự, ảnh và alt, mô tả, giá `45.000 ₫`/`280.000 ₫`, ảnh mặc định của nhóm cho món không có ảnh, món ngừng bán và nhóm ngừng sử dụng bị ẩn; tải ảnh không cần đăng nhập; kiểm tra JSON `/api/menu`; `/Ordering` vẫn yêu cầu đăng nhập.
- `AuthenticationHttpTests` không còn coi `/Menu` là trang bị chặn.

## Tải ảnh món khi tạo/sửa món

- Màn hình **Tạo món** (`/Dishes/Create`) có ô **Ảnh món**. Không bắt buộc: bỏ trống thì dùng ảnh mặc định của nhóm. Màn hình **Sửa** hiển thị ảnh hiện tại; chọn ảnh mới để thay, bỏ trống để giữ nguyên. Danh sách món có cột ảnh thu nhỏ.
- Chỉ nhận **JPG (.jpg, .jpeg) và PNG (.png)**, tối đa **5 MB**, khớp ràng buộc `ImageSizeBytes` của bảng `MenuItems`. Máy chủ kiểm tra đuôi tệp **và** chữ ký nội dung (JPEG `FF D8 FF`, PNG `89 50 4E 47…`), nên tệp đổi đuôi hoặc GIF/WebP đều bị từ chối và hiện lỗi tiếng Việt. Trình duyệt cũng báo lỗi sớm và cho xem trước ảnh (`wwwroot/js/dish-image-preview.js`).
- Tệp được lưu vào `wwwroot/uploads/mon-an/` với tên ngẫu nhiên (`{guid}.jpg|.png`), không dùng tên tệp người dùng gửi. **Database lưu đường dẫn ảnh**, ví dụ `/uploads/mon-an/3f2a…c9.jpg`, vào cột `MenuItems.ImagePath` (thuộc tính `Dish.ImagePath`). Đường dẫn chỉ do máy chủ tạo; giá trị `Dish.ImagePath` gửi kèm biểu mẫu bị bỏ qua.
- Ảnh tải lên được phục vụ tại `/uploads/mon-an/...` mà không cần đăng nhập (khách xem thực đơn), với header `X-Content-Type-Options: nosniff`. Khi thay ảnh, ảnh tải lên cũ bị xoá; ảnh mẫu trong `/images/thuc-don` không bị xoá. Nếu lưu món thất bại, ảnh vừa tải lên cũng bị xoá.
- `.gitignore` đã loại `wwwroot/uploads/*` nên ảnh thật không bị commit. Mỗi máy tự có ảnh theo database riêng của mình. Khi triển khai nhiều máy chủ, cần chuyển `IDishImageStorage` sang kho dùng chung (ví dụ blob storage).
- Kiểm thử: `MenuImageTests` (AreaTests) kiểm tra JPG/PNG hợp lệ, từ chối GIF/WebP/tệp đổi đuôi/tệp rỗng/quá 5 MB, tên tệp ngẫu nhiên, không xoá ra ngoài thư mục ảnh, trang Tạo món lưu đúng đường dẫn và bỏ qua giá trị giả mạo. `MenuImageVerification` (trong `verify`, SQL Server) đăng nhập quản lý, tải PNG/JPG qua biểu mẫu thật, kiểm tra `ImagePath` trong database, tải ảnh khi chưa đăng nhập, ảnh hiển thị trên `/Menu`, thay ảnh ở màn hình Sửa và giữ ảnh khi không chọn ảnh mới.

## Chưa có trong task này

Tìm kiếm món, nhãn và hành vi “tạm hết”, tối ưu hiển thị cho nhiều kích thước màn hình, kiểm thử tải 200 món, thu nhỏ/cắt ảnh khi tải lên.
