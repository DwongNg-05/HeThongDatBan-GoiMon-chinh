# S4-10 Task 3 — diễn tập đặt bàn đến giải phóng bàn

**Trình tự đề xuất cho PO:** đăng nhập nhân viên → khách chọn khu vực/bàn và đặt giờ → khách xác nhận bằng liên kết email → nhân viên đón khách → thu ngân thanh toán → phục vụ báo dọn xong → khôi phục mẫu cho lượt diễn tập sau. Thời gian trong form là giờ Việt Nam. Dùng giờ đặt hợp lệ trong 30 ngày; chọn A01, 2–4 khách và email do người chạy kiểm soát.

## Chuẩn bị

Chạy từ PowerShell tại thư mục repo. Database demo phải được migrate và seed. `reset-demo` giữ mật khẩu hiện có, kích hoạt bốn tài khoản và yêu cầu đổi mật khẩu ở lần đăng nhập kế tiếp. Với tài khoản ngoài Manager, hoàn thành bước xác minh email nếu staging bật `EmailVerification`.

```powershell
$env:RM_CONNECTION_STRING = 'Server=.\SQLEXPRESS;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tools/RestaurantManagement.DbTool -- reset-demo
dotnet run --project src/RestaurantManagement.Web
```

## Các bước và kết quả cần ghi

| # | Thao tác / dữ liệu | Kết quả mong đợi và điều kiện đạt |
|---|---|---|
| 1 | Đăng nhập `manager`, `waiter`, `kitchen`, `cashier`. | Manager/Waiter thấy sơ đồ và đặt bàn; Kitchen/Cashier không mở được danh sách/chi tiết đặt bàn (403). Mỗi vai trò chỉ thấy chức năng được cấp. |
| 2 | Trên sơ đồ, ghi trạng thái A01; mở **Đặt bàn**, chọn A01, 2–4 khách, giờ hợp lệ trong tương lai, tên/điện thoại/email thử. | Trước: A01 `Available`. Sau: mã khách là A01, lượt `Pending`, email có đúng mã/giờ/số khách; A01 vẫn `Available` trên sơ đồ nếu giờ hẹn còn xa nhưng không còn trong danh sách bàn trống của khung giờ. Đặt lại A01 cùng giờ phải bị từ chối và không tăng số lượt đặt. |
| 3 | Khách mở email, vào trang xác nhận và bấm **Xác nhận đặt bàn** (không đăng nhập). | Trang và database ghi `Confirmed`, đúng mã/bàn. Bàn hẹn trong tương lai vẫn `Available` trên sơ đồ; khi xác nhận trong vòng 30 phút trước giờ hẹn, trạng thái vận hành chuyển `Reserved`. |
| 4 | Khi khách đến, Manager/Waiter mở phiên phục vụ từ bàn/đặt bàn. | Lượt thành `Arrived`; A01 chuyển `Serving`. Chụp/ghi trạng thái trước và sau thao tác. |
| 5 | Thu ngân thanh toán; Waiter/Manager đánh dấu dọn xong. | Sau thanh toán: `Cleaning`; sau dọn: `Available` (hoặc `Reserved` nếu có lượt khác sắp đến). Ghi trạng thái và mã lượt ở từng bước. |
| 6 | Chuẩn bị lần tiếp theo bằng `dotnet run --project tools/RestaurantManagement.DbTool -- reset-demo`. | Lệnh báo số lượng: 3 khu vực, 25 bàn, 5 nhóm món, 60 món, 4 tài khoản, 20 đặt bàn chờ xác nhận; hoàn tất dưới 2 phút. Chạy lại không tạo trùng. Lệnh chủ động từ chối nếu đã phát sinh phiên phục vụ/hóa đơn để giữ lịch sử kế toán. |

## Biên bản chạy trên máy phát triển

Thực hiện bằng lệnh `verify-s410-task3` trên database SQL Server Express tạm được tạo mới cho mỗi lượt; kịch bản tích hợp đã chạy liên tiếp và đều kết thúc **PASS**. Ở lượt chạy lại cuối cùng, baseline là 3 khu vực/25 bàn/5 nhóm món/60 món/4 tài khoản/20 lượt đặt; bước `reset-demo` hoàn tất trong **0,69 giây**. Manager và Waiter đăng nhập; Kitchen/Cashier đăng nhập nhưng bị chặn khỏi danh sách/chi tiết đặt bàn. Đặt A01 thành công, email có mã/giờ/số khách; đặt trùng bị từ chối và số bản ghi không tăng; khách xác nhận qua liên kết email mà không đăng nhập. Cùng lượt đặt được nhận vào phục vụ rồi thanh toán và dọn: `Pending → Confirmed → Arrived`, bàn `Available → Reserved → Serving → Cleaning → Available`. Các kiểm tra log/email/quyền liên quan đều **PASS**. Bộ chạy tạo DB riêng, seed/reset baseline rồi xóa DB sau khi kết thúc, nên có thể lặp lại mà không đụng dữ liệu phát triển.

Trong lúc chạy đã phát hiện controller bỏ lựa chọn `TableId` và trang thành công không đọc được ngày giờ từ TempData; đã sửa, build thành công và chạy lại kịch bản đạt. Bộ `verify` tổng thể vẫn có lỗi riêng ở kiểm tra tìm món hết hàng trong fixture 200 món; lệnh diễn tập `verify-s410-task3` đạt. Đây là chạy trên SQL Express cục bộ, chưa phải staging Internet vì chưa có địa chỉ staging công khai. `reset-demo` chưa thể khôi phục dữ liệu dùng chung sau khi demo đã thanh toán: nó bảo vệ phiên phục vụ/hóa đơn và sẽ từ chối thay đổi. Với môi trường staging thật cần dùng database rehearsal riêng hoặc khôi phục snapshot được quản trị; không xóa lịch sử hóa đơn. Trình tự nghiệp vụ trong tài liệu là đề xuất; PO cần ghi nhận xác nhận tại buổi demo.
