# Task 1 — Báo bàn đã dọn xong

Quy tắc PO đã đồng ý: Phục vụ và Quản lý được báo đã dọn xong chỉ khi bàn đang Cleaning; chặn bàn có phiên phục vụ chưa giải phóng. Thao tác chuyển trạng thái lưu trong database sang Available, không bổ sung cảnh báo quá 10 phút hay quyết định xử lý đặt bàn kế tiếp.

## Demo

1. Chạy lại web trong Visual Studio, đăng nhập Quản lý hoặc Phục vụ.
2. Mở **Sơ đồ bàn** (`/`) trên hai trình duyệt/máy đang kết nối cùng ứng dụng và database. Có thể dùng `/TableMap` ở máy thứ hai.
3. Chọn bàn thử không có phiên khách đang mở, đang ở trạng thái **Đang dọn**. Thẻ bàn hiển thị **Báo đã dọn xong**. Các trạng thái khác ẩn nút.
4. Bấm nút: bàn về **Trống**, nút biến mất và các sơ đồ đang mở tự cập nhật trong 5 giây, không cần F5.
5. Nếu trạng thái đã thay đổi hoặc bàn còn phiên phục vụ, máy chủ chặn và hiển thị thông báo tại thẻ bàn.

Các quy tắc hiển thị đặt trước có sẵn trong dự án không được thay đổi ở Task 1. Demo chuyển về Trống dùng bàn không có lượt giữ kế tiếp đang hiệu lực; việc xử lý lượt đặt kế tiếp thuộc task sau.

## Thực hiện

- Migration `059_CompleteTableCleaning.sql`, dùng khóa giao dịch chung và kiểm tra trạng thái lại dưới khóa. Hai yêu cầu đồng thời chỉ một yêu cầu thành công.
- Sự kiện trạng thái được ghi trong cùng giao dịch nhờ trigger sẵn có. Sơ đồ chính nhận SSE; kiểm tra snapshot 2 giây dự phòng, không chồng yêu cầu. Sơ đồ `/TableMap` đồng bộ thay đổi mỗi 2 giây.
- API yêu cầu Manager/Waiter, xác thực chống CSRF, lấy mã nhân viên từ phiên đăng nhập.
- Đã sao lưu database phát triển và áp dụng migration 059; không sửa trạng thái bàn thật để demo.

## Kiểm thử

Build thành công, còn cảnh báo giấy phép ImageSharp sẵn có của dự án.

Kiểm thử SQL + Chrome dùng database tạm tự xóa: bàn Cleaning chuyển Available; các trạng thái khác ẩn nút và bị chặn ở API; phiên đang phục vụ không bị giải phóng; Phục vụ được phép, Bếp bị chặn; CSRF; gửi lặp; gửi đồng thời chỉ tạo một sự kiện chuyển trạng thái.

Ba phiên trình duyệt độc lập trên hai kiểu sơ đồ cập nhật trong khoảng **1,86 giây**, kể cả một phiên bị chặn SSE và phải dùng kiểm tra định kỳ. Đây là kiểm thử nhiều phiên trên cùng máy; demo nhiều máy cần các máy cùng truy cập máy chủ và có mạng hoạt động.

```powershell
dotnet build tools/RestaurantManagement.DbTool --no-restore -p:UseAppHost=false -o .local/db-startup-fix -m:1
dotnet build src/RestaurantManagement.Web --no-restore -p:UseAppHost=false -o .local/cleaning-check -m:1
# Cần LocalDB, sqlcmd, Chrome, Node và playwright; đặt NODE_PATH nếu module không ở đường dẫn mặc định.
.\tests\Verify-Cleaning.ps1
```
