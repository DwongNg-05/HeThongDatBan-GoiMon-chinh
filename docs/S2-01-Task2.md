# S2-01 Task 2 — Tìm món theo tên, không phân biệt dấu tiếng Việt

Khách tìm nhanh món ăn theo tên trên trang thực đơn `/Menu`, kể cả khi không gõ dấu tiếng Việt. AC hoàn tất: **AC1** và **AC2**.

**Demo:** nhập `com rang`, hệ thống hiển thị món “Cơm rang dưa bò” cùng nhóm món, ảnh, mô tả và giá `75.000 ₫`.

**Chưa làm trong task này:** trạng thái món tạm hết và tối ưu hiệu năng cho 200 món.

## Quy tắc tìm kiếm

- Hệ thống chỉ tìm theo **tên món**, không tìm trong mô tả hay tên nhóm. Món chỉ cần chứa từ khoá ở bất kỳ vị trí nào, ví dụ `rang` tìm được “Cơm rang dưa bò”.
- Trước khi so khớp, cả từ khoá lẫn tên món đều được chuẩn hoá (`MenuSearch.Normalize`):
  - bỏ dấu tiếng Việt: chuẩn hoá Unicode về dạng NFD rồi bỏ các dấu tổ hợp; chữ `đ`/`Đ` được đổi thành `d`;
  - chuyển về chữ thường;
  - bỏ khoảng trắng ở hai đầu và gộp nhiều khoảng trắng liền nhau thành một.
- Từ khoá rỗng, hoặc chỉ toàn khoảng trắng, hiển thị toàn bộ thực đơn. Từ khoá dài quá 100 ký tự bị cắt bớt.
- Kết quả giữ nguyên thông tin của món (nhóm món, ảnh, tên, mô tả, giá, đơn vị tính) và giữ nguyên thứ tự nhóm, thứ tự món. Chỉ hiển thị những nhóm có món khớp với từ khoá.
- Món ngừng bán và nhóm ngừng sử dụng vẫn bị ẩn như ở Task 1, vì tìm kiếm chỉ lọc trên kết quả của `GetPublicMenu()`.

## Thay đổi

| Lớp | File | Nội dung |
| --- | --- | --- |
| Service | `Services/Menu/MenuSearch.cs` (mới) | `Normalize`, `CleanKeyword`, `Matches`, `Filter` |
| Model | `Models/Menu/PublicMenuViewModel.cs` (mới) | Gồm từ khoá, danh sách nhóm sau khi lọc và cờ thực đơn rỗng; thêm `IsSearching`, `DishCount`, `NoResults` |
| Controller | `Controllers/Menu/MenuController.cs` | `GET /Menu?q=...` trả về `PublicMenuViewModel` |
| Controller | `Controllers/Menu/MenuApiController.cs` | `GET /api/menu?q=...` lọc giống trang `/Menu` |
| View | `Views/Menu/Index.cshtml` | Thêm ô tìm kiếm (form GET, `type="search"`), số món tìm thấy, thông báo khi không có kết quả và liên kết “Xem toàn bộ thực đơn” |
| JS/CSS | `wwwroot/js/menu-search.js` (mới), `wwwroot/css/public-menu.css` | Bấm nút × trong ô tìm kiếm thì tải lại toàn bộ thực đơn; thêm style cho ô tìm kiếm trên điện thoại |
| Dữ liệu mẫu | `database/seeds/PublicMenuDemo.sql`, `tools/.../MenuDemo.cs` | Thêm món “Cơm rang dưa bò” (Món chính, 75.000 ₫) để demo |

Việc lọc chạy ở máy chủ nên trang vẫn tìm được khi trình duyệt tắt JavaScript. Đường dẫn có dạng `/Menu?q=com+rang`, có thể gửi cho người khác.

## Demo

```powershell
dotnet run --project tools/RestaurantManagement.DbTool -- seed-menu-demo
dotnet run --project src/RestaurantManagement.Web
```

1. Mở `/Menu` khi chưa đăng nhập, gõ `com rang` rồi bấm **Tìm**. Kết quả: “Cơm rang dưa bò” nằm trong nhóm *Món chính*, có ảnh, mô tả và giá `75.000 ₫ / Đĩa`.
2. Lần lượt thử `Cơm rang`, `CƠM RANG` và `cOm RaNg`: cả ba cho cùng kết quả.
3. Thử `pizza`: trang hiện “Không tìm thấy món nào phù hợp với “pizza”. Hãy thử từ khóa khác.”
4. Bấm × trong ô tìm kiếm, hoặc bấm **Xem toàn bộ thực đơn**: toàn bộ thực đơn hiện lại.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

- **`MenuSearchTests`** (AreaTests, không cần database). Kiểm tra:
  - `com rang` → “Cơm rang dưa bò”, giữ nguyên nhóm, ảnh, mô tả và giá;
  - từ khoá có dấu và không dấu;
  - chữ hoa, chữ thường và chữ lẫn lộn hoa/thường;
  - khoảng trắng thừa, chữ `đ` và chuỗi Unicode dạng NFD;
  - tìm theo một phần tên;
  - không tìm trong mô tả hay tên nhóm, không trả về món ngừng bán;
  - từ khoá không tồn tại;
  - xoá từ khoá thì hiện lại toàn bộ thực đơn;
  - controller `/Menu?q=` và API `/api/menu?q=`.
- **`PublicMenuVerification.VerifySearch`** (`verify`, SQL Server, client chưa đăng nhập). Gọi HTTP thật tới `/Menu?q=` với các từ khoá `com rang`, `Cơm rang`, `CƠM RANG`, `COM RANG`, `ca phe`, `lau ga la e`, `rang me` (món ngừng bán không xuất hiện), một từ khoá không tồn tại và từ khoá rỗng, sau đó kiểm tra `/api/menu?q=COM RANG`.
- `PublicMenuTests` của Task 1 đã được cập nhật theo model mới (`PublicMenuViewModel`).
