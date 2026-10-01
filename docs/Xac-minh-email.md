# Xác minh đăng nhập bằng email

## Quy tắc

| Nội dung | Cách làm |
| --- | --- |
| Ai phải xác minh | Mọi tài khoản **trừ Quản lý** (Phục vụ, Bếp, Thu ngân…). Mỗi lần đăng nhập (mỗi phiên) xác minh một lần. |
| Khi nào | Ngay sau khi đăng nhập đúng mật khẩu, **trước** bước bắt buộc đổi mật khẩu. Chưa xác minh thì mọi trang (kể cả Đổi mật khẩu) đều chuyển về màn hình xác minh; chỉ được đăng xuất. |
| Cơ chế | Gửi mã về email. Lần đầu tài khoản chưa có email: người dùng nhập email, nhận mã; email chỉ được lưu vào tài khoản **sau khi** nhập đúng mã. Các lần sau mã tự gửi tới email đã lưu. |
| Mã | 6 ký tự, chỉ **chữ in hoa và số**, luôn có cả chữ lẫn số, không có ký tự đặc biệt. Bỏ các ký tự dễ nhầm 0/O, 1/I/L. Người dùng gõ chữ thường hoặc có khoảng trắng vẫn được nhận. |
| Hiệu lực | 10 phút. Sai tối đa 5 lần cho một mã, sau đó phải gửi lại mã. |
| Gửi lại mã | Nút **“Gửi lại mã”** (đếm ngược 60 giây giữa hai lần gửi, tối đa 5 lần/15 phút). Mã mới làm mã cũ hết hiệu lực. |
| Email | Tiêu đề “Mã xác minh đăng nhập Bếp Nhà: XXXXXX”; mã hiển thị to, mỗi ký tự một ô, kèm thời hạn và cảnh báo không chia sẻ; có bản chữ thường cho ứng dụng không hiển thị HTML. |
| Bảo mật | Database chỉ lưu SHA-256 của mã (gắn với tài khoản và phiên), không lưu mã gốc; tài khoản ứng dụng chỉ dùng mã qua stored procedure. |

Các thông số đổi được trong `appsettings.json`, mục `EmailVerification`.

## Cài đặt

1. Chạy migration (thêm cột `Users.Email`, `LoginSessions.EmailVerifiedAt`, bảng `EmailVerificationCodes`):

   ```powershell
   cd D:\HeThongDatBan-GoiMon-Chinh
   $env:RM_CONNECTION_STRING = 'Server=.\MSSQLSERVER07;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
   dotnet run --project tools/RestaurantManagement.DbTool -- migrate      # phải thấy "Applied: 023_EmailVerification.sql"
   ```

2. Cấu hình gửi email thật (ví dụ Gmail):
   - Bật xác minh 2 bước cho tài khoản Gmail, rồi tạo **Mật khẩu ứng dụng** (App password) tại https://myaccount.google.com/apppasswords.
   - Lưu cấu hình bằng user-secrets (không ghi mật khẩu vào appsettings.json hay đẩy lên GitHub):

   ```powershell
   cd D:\HeThongDatBan-GoiMon-Chinh
   dotnet user-secrets set "Email:Host" "smtp.gmail.com" --project src/RestaurantManagement.Web
   dotnet user-secrets set "Email:Port" "587" --project src/RestaurantManagement.Web
   dotnet user-secrets set "Email:UserName" "tenban@gmail.com" --project src/RestaurantManagement.Web
   dotnet user-secrets set "Email:Password" "mat-khau-ung-dung-16-ky-tu" --project src/RestaurantManagement.Web
   dotnet user-secrets set "Email:FromAddress" "tenban@gmail.com" --project src/RestaurantManagement.Web
   ```

   Khi chạy thật (không phải Development) dùng biến môi trường `Email__Host`, `Email__Password`…

3. **Chưa cấu hình SMTP** (`Email:Host` trống): email không gửi đi mà được lưu thành tệp trong `src/RestaurantManagement.Web/App_Data/emails/` (đã bỏ qua trong git). Mở tệp `.html` bằng trình duyệt để xem đúng giao diện email, hoặc tệp `.txt` để lấy mã.

## Thử nhanh

1. Đăng nhập bằng `waiter` → màn hình **Xác minh đăng nhập** hiện ra; thử mở `/` hay `/Account/DoiMatKhau` đều bị đưa về đây.
2. Nhập email → mở hộp thư (hoặc `App_Data/emails`) → nhập mã → vào hệ thống.
3. Bấm **Gửi lại mã** ngay: báo phải chờ; sau 60 giây bấm lại: nhận mã mới, mã cũ không dùng được.
4. Đăng nhập bằng `manager`: vào thẳng, không cần mã.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify
```

- `EmailVerificationTests` (AreaTests, không cần database): định dạng mã (2.000 mã ngẫu nhiên), chuẩn hoá chữ thường/khoảng trắng, băm gắn với phiên, che email, ai phải xác minh, nội dung email.
- `EmailVerificationVerification` (`verify`, SQL Server + HTTP thật, email ghi ra thư mục tạm): chặn mọi trang trước khi xác minh; nhập email; mã đúng định dạng; email lưu sau khi xác minh; chờ trước khi gửi lại; mã sai/sai định dạng; gửi lại làm mã cũ hết hiệu lực; mã chữ thường vẫn nhận; đăng nhập lại phải xác minh lại và tự gửi tới email đã lưu; xác minh email trước rồi mới đổi mật khẩu; Quản lý không cần xác minh.
- Các bộ kiểm thử đăng nhập cũ chạy web với `EmailVerification__Enabled=false` vì không nhập mã email.
