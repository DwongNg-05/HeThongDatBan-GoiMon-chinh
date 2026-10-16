# S4-05 Task 3 — tự động cập nhật thống kê doanh thu

## Quy tắc được người yêu cầu xác nhận

Mốc bắt đầu tính 60 giây là lúc giao dịch thanh toán commit thành công: hóa đơn `Paid` và bản ghi `Payments` đã được lưu cùng giao dịch. Không tính từ lúc mở phiên, in tạm tính, bấm nút hay bắt đầu yêu cầu thanh toán. `usp_Checkout` đã ghi hóa đơn/thanh toán trong cùng giao dịch; báo cáo đọc dữ liệu đã commit, không dùng NOLOCK.

Giữ cách phân bổ giờ/ngày theo `Payments.ReceivedAt` UTC+7 của Task 1–2. Mốc đo độ trễ (commit) và thời điểm phân bổ doanh thu (ReceivedAt) có mục đích khác nhau. Chỉ hóa đơn thỏa khoảng ngày và lịch hoạt động đang chọn mới xuất hiện.

## Hành vi hoàn chỉnh

- Quản lý mở **Thống kê doanh thu**, chọn khoảng ngày tối đa 92 ngày như Task 2.
- Màn hình lấy snapshot mỗi **20 giây**, cập nhật đồng thời năm chỉ số, biểu đồ và bảng chi tiết. Một ngày giữ biểu đồ theo giờ; nhiều ngày dùng biểu đồ theo ngày, giữ các ngày/giờ bằng 0.
- Snapshot được tính trực tiếp từ SQL Server ở mỗi lần đọc, không dùng cache tổng hợp có thể bị cũ. Trả số liệu tuyệt đối nên nhiều hóa đơn và các lần tải lặp không làm cộng trùng. Hóa đơn hủy cũng được phản ánh ở lần đọc sau.
- `GET /Reports/Today/Snapshot` có cùng quyền **Manager** và quy tắc khoảng ngày với trang. Header `Cache-Control: no-store` và fetch `cache: no-store`; header `X-Report-Snapshot: 1` giúp từ chối trang đăng nhập/HTML khác khi phiên hết hạn.
- Giới hạn xử lý SQL phía controller 10 giây, yêu cầu trình duyệt tối đa 12 giây, không chồng nhiều yêu cầu cùng trang. Chu kỳ 20 giây tạo khoảng dự phòng cho mục tiêu 60 giây khi trang đang hoạt động và kết nối bình thường. Khi mất mạng, máy chủ lỗi hoặc trình duyệt tạm dừng tab, hiển thị cảnh báo và lấy lại dữ liệu ngay khi quay về tab/có mạng; không báo giả đã cập nhật.
- Hiển thị **Cập nhật thành công gần nhất** bằng UTC+7; lỗi làm mới giữ nguyên số liệu và mốc thành công cũ, kèm thông báo dữ liệu có thể đã cũ. Không thay bằng các số 0 giả. Khi lần tải đầu thất bại, không có mốc thành công giả và thử lại ngay.
- Có nút **Làm mới ngay**. Giữ trạng thái mở bảng và vị trí cuộn biểu đồ qua các lần làm mới. Giữ nguyên ngày đang nhập nhưng chưa bấm Xem thống kê.
- Đổi khoảng ngày bằng form tải trang tương ứng, hủy yêu cầu cũ; phản hồi muộn không thể ghi đè trang mới. Nút Hôm nay trở về mặc định; mặc định đi theo ngày kinh doanh mới khi qua giờ đóng cửa.
- Polling dùng `PassiveSessionRead`, không kéo dài thời gian phiên nhàn rỗi. Hết phiên/mất quyền thì dừng cập nhật, báo cần đăng nhập lại; không chèn trang đăng nhập vào phần số liệu.
- Không thêm migration, hóa đơn mẫu hoặc thay đổi tài khoản trong database đang dùng.

## Kiểm thử và bằng chứng

- Build thành công, chỉ còn cảnh báo giấy phép ImageSharp có sẵn.
- **837 kiểm tra hồi quy** đạt. Đã đồng bộ kiểm thử menu Quản lý với mục Thống kê doanh thu và bổ sung action QR có sẵn vào ma trận phân quyền để toàn bộ bộ kiểm thử chạy hết.
- **48 kiểm tra riêng báo cáo** đạt: quy tắc Task 1–2, kiểm tra khoảng ngày cho cả trang/snapshot, dựng Razor ở các trạng thái, chỉ hiển thị mốc thành công khi tải được dữ liệu, endpoint nền không gia hạn phiên.
- **11 kiểm tra JavaScript** đạt: độ trễ theo đồng hồ kiểm thử, nhiều hóa đơn, giữ khoảng đang áp dụng, hủy yêu cầu khi chuyển khoảng, timeout, giữ snapshot cũ khi lỗi/phục hồi, quay lại tab/có mạng, chuyển ngày mặc định, từ trống sang có dữ liệu, dừng khi hết quyền.
- Kiểm thử **trình duyệt Edge thật + SQL Server thật** trên database `RestaurantManagement_Test_<GUID>` riêng. Tạo hóa đơn qua chuỗi nghiệp vụ mở phiên → gửi món → bếp chế biến/xong → phục vụ → `usp_Checkout`. Đã đo ba lần hóa đơn đầu xuất hiện ở chỉ số và biểu đồ sau **19.213 giây**, **19.452 giây** và **19.513 giây**, dưới 60 giây. Không rút ngắn chu kỳ 20 giây trong kiểm thử thực tế.
- Kiểm thử nhiều hóa đơn liên tiếp: 3 hóa đơn, tổng 75.000 VND, không đếm đôi. Bảng chi tiết đang mở cũng đổi đúng tổng và giữ trạng thái mở. Đổi sang ngày không có giao dịch cho số 0, làm mới vẫn dùng khoảng đã áp dụng; đổi sang khoảng hai ngày thấy đúng hai cột và tổng 75.000 VND. Đã kiểm tra thất bại mạng không đổi mốc thành công/số liệu và phục hồi thành công. Kiểm tra trang máy tính/điện thoại không có lỗi JavaScript, nội dung máy tính không bị thanh bên che.
- Database tạm và tiến trình web được dọn sau kiểm thử, kể cả khi lỗi. Mật khẩu thử ngẫu nhiên không in ra nhật ký. Bộ kiểm thử không sử dụng tài khoản/mật khẩu thật của người dùng.

### Chạy lại

```powershell
dotnet run --project tests/RestaurantManagement.AreaTests -- --daily-overview-only
node --test tests/report-live.test.cjs
dotnet run --project tests/RestaurantManagement.AreaTests

# SQL/browser: cần SQL Server có quyền tạo/xóa database kiểm thử,
# sqlcmd, Node, Playwright và Microsoft Edge cài sẵn.
# Nếu Playwright không nằm trong node_modules dự án, đặt NODE_PATH
# tới thư mục node_modules đã cài Playwright trên máy.
$env:RM_CONNECTION_STRING = 'Server=.\SQLEXPRESS;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --project tests/RestaurantManagement.AreaTests -- --daily-overview-live
```

## Demo

1. F5, đăng nhập Quản lý, mở `/Reports/Today`, để màn hình mở.
2. Ở phiên Thu ngân khác, thanh toán một phiên có món đã phục vụ trong ngày/khoảng và giờ hoạt động được chọn.
3. Ghi giờ thanh toán lưu thành công. Trong chu kỳ tiếp theo (20 giây cộng thời gian truy vấn), số hóa đơn/doanh thu/lượt khách/trung bình, cột giờ/ngày và bảng doanh thu đổi cùng snapshot. Mốc cập nhật gần nhất cũng đổi.
4. Chọn khoảng khác rồi thanh toán tiếp: màn hình chỉ thay đổi doanh thu nếu hóa đơn thuộc khoảng đó; số bàn đang phục vụ luôn là số hiện tại.
5. Tắt mạng để thấy cảnh báo và số liệu cũ được giữ nguyên, bật lại để phục hồi.

## Môi trường của máy hiện tại

Database `RestaurantManagement_Dev` còn chủ sở hữu Windows từ tên máy cũ (`LAPTOP-MEJL0B7P\HUY HOANG`). Kiểm thử thực tế phát hiện lỗi 0x534 tại trigger `EXECUTE AS OWNER` khi database tạm mới tạo dùng chủ sở hữu này. Bộ kiểm thử chỉ sửa chủ sở hữu của database tạm sang login SQL tích hợp, không sửa database thật. Người yêu cầu đã chọn **Chỉ hoàn thiện mã và kiểm thử riêng**, vì vậy giữ nguyên môi trường đang dùng; nếu demo thanh toán tại đó gặp lỗi chủ sở hữu thì cần quản trị viên sửa riêng trước.

Phần lịch qua nửa đêm tiếp tục được kiểm thử ở bộ tính báo cáo. Giới hạn cấu hình lịch dùng chung đã ghi ở tài liệu Task 1 vẫn chưa được thay đổi; không tuyên bố đã nghiệm thu đầu-cuối cấu hình đặt bàn/QR qua nửa đêm. Các kiểm thử thời gian cập nhật Task 3 chạy với lịch hợp lệ trên database riêng.
