# S1-03: bắt buộc đổi mật khẩu

Khôi phục chức năng từ commit `30c23f4c26d00a38cc73f5144bf5413fb0aa5eae`
(Ha Thi Thu Tra), đã bị hoàn tác bởi `723b2a5`.
Giữ trang đổi mật khẩu gốc; chuyển xử lý Identity sang bảng `Users` và
`LoginSessions` hiện tại để tương thích EP-01/EP-02.

## Cập nhật database

Chạy DbTool `migrate` với `RM_CONNECTION_STRING` trước khi khởi động bản mới.
Migration `019_RequiredPasswordChange.sql` chỉ thêm thủ tục và quyền thực thi;
không đặt lại mật khẩu hoặc thay đổi cờ của tài khoản hiện có.

## Hành vi

- `MustChangePassword=1`: đăng nhập thành công chuyển tới `/Account/ChangePassword`.
- Chặn truy cập trực tiếp các màn hình MVC, Razor Pages và API quản lý cho đến khi đổi mật khẩu; vẫn cho phép đăng xuất.
- Mật khẩu mới có ít nhất 8 ký tự, gồm chữ và số, tối đa 72 byte UTF-8; phải khác mật khẩu hiện tại và khớp xác nhận.
- Sai mật khẩu hiện tại không tăng bộ đếm khóa đăng nhập.
- Thành công: lưu bcrypt, tắt cờ bắt buộc, ghi thời điểm UTC, đổi SecurityStamp và thu hồi các phiên khác; giữ phiên đang thực hiện.
- Kiểm tra hash cũ và phiên trong giao dịch để tránh thay đổi bằng thông tin cũ hoặc phiên đã bị thu hồi.

## Kiểm thử

`dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify`
tạo database kiểm thử riêng, kiểm tra S1-01 và S1-03 rồi xóa database đó.
