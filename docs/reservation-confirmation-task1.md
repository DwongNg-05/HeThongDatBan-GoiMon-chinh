# Xác nhận đặt bàn – Task 1

## Quy tắc đang áp dụng (cần PO xác nhận)

- Dùng `StartsAt`/`EndsAt` đã lưu trên lượt đặt; thời lượng mặc định hiện có là 90 phút, chỉnh trong Giờ hoạt động (`DefaultBookingMinutes`). Đổi cấu hình không thay đổi các lượt cũ.
- Khoảng giữ bàn là `[bắt đầu, kết thúc)`: A trùng B khi `A.StartsAt < B.EndsAt AND A.EndsAt > B.StartsAt`. Hai lượt sát nhau không trùng. SQL lưu UTC, giao diện/email hiển thị giờ Việt Nam.
- Lượt chờ dùng trạng thái `Pending` sẵn có. Chỉ chuyển `Pending` → `Confirmed`; chặn xác nhận lại và giờ hẹn đã qua. Không có đổi bàn hoặc từ chối trong màn hình này.
- Bàn/khu vực phải đang sử dụng, sức chứa tối đa >= số khách, không có lượt `Confirmed`/`Arrived` trùng giờ. Khu vực khách chọn là mong muốn: xếp bàn ở đó trước, vẫn gợi ý các khu vực phù hợp khác.
- Giữ bàn theo lượt đặt và khung giờ. Lịch giữ bàn là nguồn xem lịch tương lai; trạng thái vận hành toàn cục của bàn vẫn theo quy tắc hiện có (Reserved khi gần giờ hẹn 30 phút).
- Chọn email làm kênh tích hợp ban đầu, chưa coi là PO đã duyệt. Nội dung: mã đặt, tên khách, số bàn, số khách, giờ hẹn và giờ kết thúc giữ bàn. Không bổ sung SMS hay màn hình khách tra cứu.

## Chạy và demo

Dùng connection string của môi trường đang chạy. Ví dụ PowerShell cho máy phát triển:

```powershell
$env:RM_CONNECTION_STRING='Server=(localdb)\MSSQLLocalDB;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True'
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project tools/RestaurantManagement.DbTool -- seed-confirmation-demo
dotnet run --project src/RestaurantManagement.Web
```

Seed bổ sung khu vực `Demo xác nhận`, bàn `DEMO-5` bốn chỗ, `DEMO-2` hai chỗ và lượt `CF0001` bốn khách lúc 18:00 ngày hôm sau. Chạy lại giữ nguyên dữ liệu, không tự sửa ngày của lượt cũ hoặc tài khoản. Email `demo@example.test` chỉ là dữ liệu giả.

Đăng nhập bằng tài khoản Manager/Waiter hiện có → **Xác nhận đặt bàn** → chọn lượt → chọn `DEMO-5` → **Xác nhận và giữ bàn** → **Xem lịch giữ bàn**. `DEMO-2` không được gợi ý. Lượt đã xác nhận không còn trong danh sách chờ. Trang chi tiết có trạng thái gửi email và nút làm mới.

## Email

`ConfirmationEmail:Enabled=false` mặc định. Chưa có SMTP thật thì email nằm chờ và giao diện thông báo rõ; không báo đã gửi. Cấu hình bằng biến môi trường hoặc user secrets, không commit mật khẩu:

- `ConfirmationEmail__Enabled=true`
- `ConfirmationEmail__Host`, `ConfirmationEmail__Port` (mặc định 587)
- `ConfirmationEmail__From`, `ConfirmationEmail__Username`, `ConfirmationEmail__Password`
- `ConfirmationEmail__EnableSsl=true`
- Nếu dùng SQL account giới hạn quyền: `ConnectionStrings__EmailWorker` là connection có role `restaurant_worker`; nếu bỏ trống dùng connection mặc định. Worker cần quyền chạy claim/complete email; không cần thêm quyền này vào người dùng phục vụ.

Khởi động lại ứng dụng sau khi đổi cấu hình. Chỉ dùng email thử do bạn kiểm soát khi demo gửi thật. Worker chỉ nhận `BookingConfirmed`, timeout gửi 30 giây, thử tối đa 4 lần cách nhau 5 phút. Thiếu email được ghi Failed; lỗi gửi được lưu, xác nhận vẫn còn hiệu lực. Trạng thái Sent nghĩa là SMTP đã nhận, không đảm bảo thư đã vào hộp thư khách. Khi worker chết sau SMTP nhận nhưng trước ghi kết quả, có thể gửi lặp (at-least-once), không giữ bàn lặp.

## Kiểm thử

```powershell
dotnet run --project tests/RestaurantManagement.AreaTests -- --confirmation
```

Cần `RM_CONNECTION_STRING` tới SQL Server có quyền tạo/xóa database thử. Test tạo tên `RestaurantManagement_ConfirmationTest_<guid>`, tự dọn đúng database đó, không sửa database thật. Bao gồm SQL concurrency, HTTP đăng nhập/CSRF/xác nhận/lỗi tranh chấp, email lỗi/retry và SMTP giả lập trên loopback (không gửi ra ngoài). Các test đơn vị hiện có cũng chạy cùng.

## Kết quả kiểm tra trên máy phát triển

- Build ra `.local/confirmation-build`: thành công, 0 cảnh báo/lỗi.
- 203 kiểm tra sẵn có của AreaTests và 35 kiểm tra xác nhận mới: đạt.
- Bộ `DbTool verify` rộng hơn dừng ở kiểm tra cũ `Session identifies manager`: assertion tìm `Xin chào, manager`, trong khi layout hiện hiển thị `Tài khoản hiện tại` và tên trong thẻ strong. Không coi toàn bộ bộ kiểm thử này đã đạt.
- Build vào thư mục mặc định bị tiến trình ứng dụng đang chạy khóa file exe. Dừng Debug (Shift+F5), build rồi chạy lại để dùng giao diện mới. Bản build riêng dùng kiểm thử không gặp lỗi này.
