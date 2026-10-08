# S2-09 Task 3: Nghiệm thu toàn bộ luồng đặt bàn và gửi email

Luồng được kiểm chứng: **đặt bàn → hiển thị mã → gửi email → thử lại khi thất bại → ghi nhận kết quả → nhân viên xem trạng thái**. Ba tình huống: thành công ngay, thất bại rồi thành công khi gửi lại, và thất bại cả 3 lần gửi lại.

## Bộ kiểm thử

| Bộ kiểm thử | Chạy bằng | Kiểm tra |
| --- | --- | --- |
| `BookingEmailEndToEndVerification` (mới) | `dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-booking-email` (cũng chạy trong `verify`) | SQL Server thật, web thật và **máy chủ SMTP giả** trên máy (`FakeSmtpServer`, mới). Web gửi email qua SMTP thật; máy chủ giả nhận thư, giải mã tiêu đề và nội dung đúng như khách sẽ nhận, và cố tình từ chối thư (lỗi tạm thời 450) theo từng người nhận. |
| `BookingEmailFlowTests` (mới) | `dotnet run --project tests/RestaurantManagement.AreaTests --no-build` | Không cần database: `SmtpEmailSender` thật của web gửi qua mạng tới `FakeSmtpServer`; hàng đợi giả lập theo đúng quy tắc gửi lại của migration 030, dùng đồng hồ giả. |

Trong `verify-booking-email`, mỗi khách dùng một trình duyệt riêng (cookie riêng), và các tình huống chạy song song trên cùng một web. Worker gửi lại kiểm tra mỗi giây. “Chờ 5 phút” được tua nhanh, nhưng chỉ sau khi đã kiểm tra database hẹn **đúng 300 giây** và web **không gửi sớm**.

## Đối chiếu yêu cầu với bài kiểm tra

| Yêu cầu | Bài kiểm tra |
| --- | --- |
| Từ lúc ghi nhận đặt bàn tới lúc gửi thành công ≤ 60 giây | A: đo thời gian từ khi gửi form tới khi máy chủ SMTP nhận thư. Đồng thời trong database: `SentAt − Reservations.CreatedAt` nằm trong khoảng 0–60.000 ms |
| Thông tin email khớp lượt đặt bàn | Mỗi email đọc lại từ SMTP được kiểm tra: tiêu đề `Xác nhận đặt bàn <mã bàn> – Bếp Nhà`; nội dung (bản chữ thường và HTML) có đúng mã, ngày giờ, số khách, địa chỉ quán, tên khách; không có mã nội bộ. Lượt đặt bàn lưu đúng tên, SĐT, email, số khách, giờ, bàn đã nhập |
| Mã đặt bàn không đổi khi email thất bại | Sau mọi lần thử (A, B, C), `Reservations.Code`, bàn và mã bàn vẫn như lúc đặt |
| Trang xác nhận vẫn đầy đủ khi email lỗi | B, C: trang có mã đặt bàn, ngày giờ, số khách, lời nhắc lưu mã, kèm thông báo “chưa gửi được… sẽ tự gửi lại lúc …” |
| Trạng thái gửi cập nhật đúng sau từng lần thử | Sau mỗi lần thử: `EmailOutbox` (Pending/Sent/Failed, số lần), dòng `EmailAttempts` của lần đó (Thất bại kèm lỗi SMTP / Thành công), giờ hẹn lần sau = lúc kết thúc + 300 giây, `Reservations.ConfirmationEmailStatus/Attempts` |
| Kết quả cuối cùng khi gửi lại thành công | B: *Sent* ở lần thử 2, 2 dòng lần thử (Thất bại → Thành công), khách nhận đúng **1** email |
| Kết quả cuối cùng khi cả 3 lần gửi lại đều lỗi | C: *Failed* sau 4 lần thử (lần đầu + 3 lần gửi lại). Máy chủ SMTP ghi nhận đúng 4 lần; tua thêm vẫn không có lần thứ 5; lượt đặt bàn vẫn *Chờ xác nhận* |
| **Test:** thành công ngay lần đầu | Tình huống A |
| **Test:** thất bại lần đầu, thành công lần sau | Tình huống B |
| **Test:** thất bại cả 3 lần thử | Tình huống C |
| **Test:** nhân viên xem đúng trạng thái trong cả ba tình huống | Chi tiết đặt bàn của A, B, C: trạng thái, `x/4`, kết quả cuối cùng, bảng từng lần thử. Danh sách đặt bàn: dòng “Email: …” |
| **Test:** nhiều lượt liên tiếp không nhầm mã/thông tin | 3 lượt đặt liên tiếp cùng khung giờ, khác bàn. Mỗi email chỉ có tên và mã của lượt đó; hàng đợi email đúng người nhận và đúng mã; trang xác nhận của từng khách và chi tiết của nhân viên chỉ hiện lượt của mình. Tổng cộng đúng 5 email được gửi (A, B sau khi gửi lại, 3 lượt liên tiếp; C không có) |

## 4 tiêu chí chấp nhận của story S2-09

| AC | Nội dung | Đáp ứng bởi |
| --- | --- | --- |
| 1 | Email xác nhận gửi trong vòng 60 giây, đủ mã đặt bàn, ngày giờ, số khách, địa chỉ quán | Task 1 + nghiệm thu A và kiểm tra nội dung email |
| 2 | Trang xác nhận luôn hiển thị đầy đủ mã đặt bàn, kể cả khi email lỗi | Task 1 + nghiệm thu B, C (trang khách trước và sau khi có kết quả cuối cùng) |
| 3 | Nhân viên xem được trạng thái gửi email của lượt đặt bàn | Task 3 (khu vực Email xác nhận) + nghiệm thu nhân viên xem A, B, C |
| 4 | Email lỗi được thử lại tối đa 3 lần, cách nhau 5 phút, ghi kết quả cuối cùng | Task 2 + nghiệm thu B, C (300 giây, không gửi sớm, không có lần thứ 5) |

## Demo nghiệm thu bằng tay

1. `dotnet run --project tools/RestaurantManagement.DbTool -- migrate`. Cần có đủ migration 024 → 030.
2. **A — thành công:** chạy web với email ghi ra tệp (`Email:Host` để trống). Đặt bàn có email.
   - Trang xác nhận hiện mã bàn và dòng “Email xác nhận đã được gửi tới …”.
   - Tệp `.html` trong `src/RestaurantManagement.Web/App_Data/emails` có đúng mã, ngày giờ, số khách, địa chỉ.
   - Chi tiết đặt bàn: *Đã gửi thành công · 1/4*.
3. **C — thất bại 3 lần:** dừng web và chạy lại với SMTP lỗi:
   ```powershell
   $env:Email__Host='127.0.0.1'; $env:Email__Port='2599'; $env:Email__EnableSsl='false'; $env:Email__FromAddress='no-reply@example.com'
   ```
   - Đặt bàn: trang vẫn hiện mã và “sẽ tự gửi lại lúc …”.
   - Chạy `dotnet run --project tools/RestaurantManagement.DbTool -- email-retry-now` 3 lần, mỗi lần chờ khoảng 30 giây.
   - Chi tiết hiện 4 lần thử và *Thất bại sau 4 lần thử (lần gửi đầu và 3 lần gửi lại)*. Trang xác nhận của khách tự đổi sang kết quả cuối cùng.
4. **B — thành công khi gửi lại:**
   - Khi SMTP đang lỗi: đặt thêm một lượt.
   - Dừng web, xoá các biến `$env:Email__*`, chạy lại web.
   - Chạy `email-retry-now`. Chi tiết hiện *Thành công … (ở lần thử thứ 2)*, bảng lần thử có *Lần gửi đầu – Thất bại*, *Lần gửi lại 1 – Thành công*.
5. **Nhân viên:** mở *Danh sách đặt bàn*. Mỗi lượt có dòng trạng thái email riêng, đúng với từng tình huống.

## Thay đổi

| File | Nội dung |
| --- | --- |
| `tools/.../FakeSmtpServer.cs` (mới) | Máy chủ SMTP giả: nhận thư, giải mã MIME (base64, quoted-printable, tiêu đề RFC 2047), từ chối có chủ đích theo người nhận, đếm số lần thử |
| `tools/.../BookingEmailEndToEndVerification.cs` (mới) | Bộ nghiệm thu end-to-end ở trên |
| `tools/.../Verification.cs`, `DatabaseTool.cs` | Gọi bộ nghiệm thu trong `verify`; thêm lệnh `verify-booking-email` |
| `tools/.../BookingConfirmationVerification.cs` | Cho phép dùng chung các hàm trợ giúp (khởi động web, đặt bàn, đăng nhập…) |
| `tests/.../BookingEmailFlowTests.cs` (mới), `EmailRetryTests.cs`, `Program.cs` | Kiểm thử luồng qua SMTP không cần database |
