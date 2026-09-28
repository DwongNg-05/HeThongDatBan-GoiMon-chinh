# S1-06 Task1 — rà soát và nghiệm thu

> Tài liệu lịch sử của Lát 1. Quy tắc hiện tại về tên chuẩn hóa và danh sách quản lý đã được mở rộng ở [Lát 2–4](S1-06-Task2-4-review.md).

## Phạm vi và hợp đồng hiện tại
- GET /Areas: bảng Tên khu vực | Thứ tự hiển thị | Trạng thái; chỉ IsActive=1.
- Sắp xếp SortOrder tăng dần, sau đó Name và Id để ổn định thứ tự khi bằng nhau.
- GET /Areas/Create và POST /Areas/Create: endpoint MVC nhận form, có anti-forgery; thành công redirect về danh sách, đọc lại database và hiện thông báo.
- Tên bắt buộc, tối đa 80 ký tự; thứ tự bắt buộc, số nguyên từ 0 đến 2147483647. Khu vực mới luôn đang sử dụng.
- Tên so sánh không phân biệt hoa/thường, có phân biệt dấu tiếng Việt; không trim/gộp khoảng trắng. Tên của khu vực ngừng sử dụng vẫn được giữ chỗ.
- Index duy nhất dùng collation Vietnamese_100_CI_AS và độ dài byte để SQL Server không bỏ qua khoảng trắng cuối khi so sánh. Transaction bảo vệ các yêu cầu đồng thời.
- Form/danh sách Task1 không hiển thị ghi chú hoặc sửa/xóa/ngừng sử dụng. Endpoint mở rộng cũ được giữ lại, chưa thuộc nghiệm thu Task1.

## Những lỗi đã sửa
- Migration 006 có đuôi .sql.txt, công cụ chỉ đọc *.sql.
- Khóa transaction gọi trước BEGIN TRANSACTION làm thêm/sửa lỗi.
- Danh sách trả cả khu vực không sử dụng.
- Thiếu kiểm tra thứ tự hiển thị; dữ liệu thiếu bị mặc định thành 0.
- Quy tắc chuẩn hóa tên, bỏ dấu và index trim không khớp lát Task1.
- Form thêm có ghi chú ngoài phạm vi; danh sách có thao tác ngoài phạm vi; thiếu đường dẫn trên thanh điều hướng.
- Lỗi trùng tên giờ gắn vào trường tên, giữ dữ liệu đã nhập.

## Kiểm thử tự động
```powershell
dotnet run --project tests/RestaurantManagement.AreaTests --artifacts-path artifacts/area-review
$env:RM_CONNECTION_STRING = 'Server=(localdb)\MSSQLLocalDB;Database=RestaurantManagement_AreaReview;Integrated Security=true;TrustServerCertificate=true'
dotnet run --project tools/RestaurantManagement.DbTool -- verify
```
Unit test là executable test runner (không dùng xUnit/dotnet test), trả exit code khác 0 nếu lỗi. Đã đạt 10 ca kiểm tra model và controller. SQL integration tạo/xóa database riêng; đã đạt toàn bộ kiểm thử, gồm thêm, trùng tên khác hoa/thường, khoảng trắng cuối, dấu, dữ liệu sai, quyền DB, danh sách đang dùng, thứ tự và tạo đồng thời. Không sửa dữ liệu database phát triển.

## Demo thủ công sau khi áp dụng migration
1. Dùng cấu hình kết nối đúng database phát triển, chạy DbTool migrate; cần có user quản lý trong database (seed-demo nếu là database mẫu mới).
2. Khởi động lại web, mở Quản lý khu vực → Thêm khu vực.
3. Nhập tên mới và thứ tự 0; lưu; xác nhận chuyển về danh sách, có thông báo thành công và bản ghi xuất hiện đúng vị trí.
4. Thêm lại tên, hoặc đổi hoa/thường: hiện lỗi ở tên, giữ lại thứ tự đã nhập.
5. Thử thiếu thứ tự, số âm, chữ hoặc số thập phân: không thêm được.
6. Xác nhận tên như “Sân vườn”, “San vuon”, “Sân  vườn”, “Sân vườn ” được xử lý đúng quy tắc đã nêu.

## Cần PO xác nhận (chưa được xác nhận)
- Ba cột và quy tắc sắp xếp trên có phù hợp không?
- Thứ tự bắt đầu từ 0, cho phép nhiều khu vực cùng thứ tự?
- Không phân biệt hoa/thường nhưng phân biệt dấu/khoảng trắng; tên khu vực ngừng dùng vẫn chặn trùng?

## Giới hạn còn lại
- Chưa chạy demo bằng trình duyệt trên database phát triển; chỉ kiểm tra build, unit và SQL integration.
- Dự án chưa có đăng nhập web. ActorUserId đang lấy từ cấu hình, mặc định 1; kiểm tra quyền SQL không chứng minh danh tính người truy cập. Chỉ dùng cho demo nội bộ; cần tích hợp xác thực/phân quyền thực trước nghiệm thu quyền “Quản lý” trên môi trường triển khai.
- Entity EF KhuVuc hiện không được dùng bởi luồng này; luồng Areas dùng bảng dbo.Areas qua stored procedure. Không dùng EnsureCreated để tạo schema thay migration.

