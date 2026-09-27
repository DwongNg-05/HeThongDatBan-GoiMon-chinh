# S1-06 — rà soát Lát 2–4

## Thay đổi
- Danh sách quản lý hiển thị cả đang sử dụng/ngừng sử dụng, ghi chú và thao tác Sửa, Ngừng sử dụng, Xóa. Sắp xếp thứ tự tăng dần, tên, Id.
- Sửa tải đúng dữ liệu hiện tại và cập nhật trên Id cũ; giữ nguyên tên khi chỉ sửa ghi chú/thứ tự. Dữ liệu sai hoặc tên trùng không thay đổi database; form giữ nội dung người dùng nhập.
- Thêm/sửa dùng cùng tên chuẩn hóa: trim, gộp khoảng trắng, chuyển tab/xuống dòng/khoảng trắng Unicode thông dụng thành khoảng trắng thường; không phân biệt hoa/thường, vẫn phân biệt dấu tiếng Việt. Tên của khu vực ngừng sử dụng vẫn giữ chỗ.
- Index duy nhất trên tên chuẩn hóa bảo vệ cả SQL trực tiếp và các yêu cầu đồng thời. Không tự đổi tên/gộp/xóa khu vực khi nâng cấp gặp trùng tên cũ.
- Ngừng sử dụng có màn hình xác nhận riêng. GET và Hủy không ghi dữ liệu; chỉ POST có anti-forgery mới cập nhật. Bàn và đặt bàn không bị xóa theo.
- Xóa bị chặn nếu khu vực còn bàn (kể cả bàn ngừng dùng) hoặc đặt bàn; thông báo đề nghị ngừng sử dụng. Khu vực chưa có dữ liệu liên quan có thể xóa.
- Dropdown đặt bàn chỉ có khu vực đang sử dụng. Server và stored procedure chặn khu vực không tồn tại/ngừng dùng, kể cả khi form cũ được gửi sau lúc khu vực ngừng dùng.
- Thêm AreaNameSnapshot cho đặt bàn, tự chụp tên khi tạo, backfill dữ liệu trước migration bằng tên hiện có. Danh sách và trang chi tiết dùng tên này, kể cả sau đổi tên/ngừng sử dụng khu vực. Hiển thị thời gian theo Việt Nam.

## Nâng cấp
Migration mới: `database/migrations/008_AreaLifecycle.sql`. Không sửa checksum các migration cũ.

```powershell
# Dùng RM_CONNECTION_STRING trỏ tới database phát triển của bạn.
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
# Sau đó khởi động lại ứng dụng web.
```
Nếu migration báo 51406: các khu vực đã tồn tại có tên trùng sau chuẩn hóa khoảng trắng/hoa thường. Cần xem các Id bị trùng và quyết định đổi tên; migration rollback, không tự gộp/xóa dữ liệu.

```sql
SELECT Id, Name, dbo.fn_NormalizeAreaName(Name) AS ProposedName
FROM dbo.Areas
ORDER BY dbo.fn_NormalizeAreaName(Name) COLLATE Vietnamese_100_CI_AS, Id;
```

Tên lịch sử đã thay đổi trước lần nâng cấp này không thể khôi phục nếu hệ thống chưa từng lưu snapshot. Backfill chỉ sử dụng tên hiện có tại thời điểm nâng cấp.

## Kiểm thử
```powershell
dotnet build RestaurantManagement.sln --artifacts-path artifacts/area-review
dotnet run --project tests/RestaurantManagement.AreaTests --artifacts-path artifacts/area-review
$env:RM_CONNECTION_STRING = 'Server=(localdb)\MSSQLLocalDB;Database=RestaurantManagement_AreaReview;Integrated Security=true;TrustServerCertificate=true'
dotnet run --project tools/RestaurantManagement.DbTool -- verify
dotnet run --project tests/RestaurantManagement.AreaTests --artifacts-path artifacts/area-review -- --integration
```

- Unit runner kiểm tra validation tên/thứ tự/ghi chú, GET form và POST không hợp lệ giữ dữ liệu.
- SQL runner kiểm tra sửa ghi chú/thứ tự/tên không thêm bản ghi, tên trùng hoa thường/khoảng trắng/tab, rollback khi từ chối, tên không tồn tại, quyền DB, thêm đồng thời, ngừng dùng có/không có bàn, chặn xóa có bàn, tên lịch sử của nhiều đặt bàn và chặn đặt bàn khu vực ngừng dùng. Chạy kèm các kiểm thử nghiệp vụ cũ.
- HTTP runner khởi động web thực ở loopback trên cổng riêng; kiểm tra Razor form, POST có cookie/token, redirect/thông báo/danh sách mới, dữ liệu lỗi, 404, anti-forgery, hủy/xác nhận, dropdown và lịch sử qua danh sách/trang chi tiết.
- Các runner là executable C# (không dùng `dotnet test`); thất bại trả exit code khác 0. SQL/HTTP runner tạo database ngẫu nhiên riêng và dọn đúng database đó trong finally; không sửa database phát triển. HTTP runner dừng đúng process web nó đã tạo.

## Demo
1. Chọn Sửa ở “Tầng một”, đổi thành “Tầng 1”, chỉnh thứ tự/ghi chú; lưu và thấy dữ liệu mới trong danh sách.
2. Thử đổi thành tên “Tầng hai” hoặc “  TẦNG   HAI ”: bị chặn.
3. Chọn Ngừng sử dụng → Hủy: trạng thái giữ nguyên. Làm lại và Xác nhận: danh sách vẫn có khu vực với trạng thái ngừng dùng.
4. Thử Xóa khu vực có bàn: thông báo không thể xóa và đề nghị ngừng sử dụng.
5. Tạo đặt bàn mới: khu vực ngừng dùng không xuất hiện. Mở nhiều đặt bàn cũ và trang chi tiết: tên khu vực cũ vẫn còn.

## Phạm vi chưa thay đổi
Dự án chưa có đăng nhập web: ActorUserId vẫn là cấu hình demo (mặc định 1). Stored procedure kiểm tra quyền Catalog.Manage nhưng chưa xác minh danh tính người truy cập web. Cần nối cơ chế đăng nhập/phân quyền của dự án trước khi triển khai cho người dùng thực. Đây là hạn chế đã có ở Lát 1, không phải đã hoàn thành xác thực quản lý.

Quy tắc từ Lát 2–4 thay thế mô tả tạm thời của Lát 1 về khoảng trắng và danh sách chỉ hiển thị khu vực đang sử dụng. Các quyết định PO về dấu tiếng Việt, tên khu vực ngừng dùng giữ chỗ và giới hạn thứ tự vẫn cần xác nhận nghiệp vụ.
