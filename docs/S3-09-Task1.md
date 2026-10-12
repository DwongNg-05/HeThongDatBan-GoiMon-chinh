# S3-09 Task 1: Cảnh báo đặt bàn trễ 15 phút và đánh dấu khách không tới

Cảnh báo khi khách đặt bàn trễ 15 phút mà chưa tới, và cho phép Phục vụ (hoặc Quản lý) đánh dấu **khách không tới** để giải phóng bàn ngay.

**Hoàn tất:** AC 1, AC 3, và nhánh "đánh dấu khách không tới" của AC 2.

**Chưa làm trong task này:** gia hạn giữ bàn; cảnh báo khi số điện thoại có lịch sử không tới đặt bàn lần sau. Lịch sử đã được lưu sẵn cho task đó.

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Cảnh báo tại mốc 15 phút hay sau mốc? | **Tại mốc**: trễ đúng 15:00 là có cảnh báo, 14:59 thì chưa. Mốc = giờ hẹn + `RestaurantSettings.NoShowGraceMinutes` (mặc định 15). Nếu lượt đã được gia hạn giữ bàn (`HoldExtendedUntil`, task sau), mốc là thời điểm gia hạn đó. |
| Trạng thái đặt bàn nào được cảnh báo? | **Chờ xác nhận** và **Đã xác nhận**, khi khách chưa tới (`ArrivedAt` trống). Khách đã tới, đã huỷ, bị từ chối, đã đánh dấu không tới: không cảnh báo. |
| Cảnh báo kéo dài bao lâu? | Tới khi nhân viên xử lý, tối đa 12 giờ sau giờ hẹn, để cảnh báo cũ không tồn sang ngày sau. |
| Ai được đánh dấu? | Quản lý và Phục vụ (quyền `Reservations.Manage`). Bếp và Thu ngân bị chặn. |
| Đánh dấu khi chưa tới mốc? | Máy chủ từ chối: "Lượt đặt chưa trễ tới mốc cảnh báo…". |
| Giải phóng bàn nào? | Bàn đã xếp cho lượt đặt, nếu bàn đang **Đã đặt trước** và không có phiên phục vụ đang mở hay lượt đã xác nhận khác sắp tới trong 30 phút. Bàn đang có khách không bị đổi. Lượt chưa xếp bàn vẫn đánh dấu được. |
| Chuẩn hoá số điện thoại | Bỏ khoảng trắng, dấu chấm, gạch nối, dấu ngoặc. Đổi `+84`, `0084`, `84` (11 chữ số) thành `0`. Kết quả phải là 10 chữ số bắt đầu bằng 0. Ví dụ `0901 234 567`, `090-123-4567`, `+84 901 234 567`, `84901234567` đều thành `0901234567`. Lượt đặt lưu số đã chuẩn hoá nên lịch sử của cùng khách gom về một chuỗi. |

## Cách hoạt động

- **Lưu trữ.** Mỗi lượt đặt đã có giờ hẹn (`StartsAt`), trạng thái và thời điểm khách tới (`Status`, `ArrivedAt`), và số điện thoại đã chuẩn hoá (`Phone`). Migration `049_S309LateReservationNoShow.sql` (chạy `migrate`) thêm:
  - **`dbo.ReservationNoShowHistory`**: lịch sử không tới theo số điện thoại (số, mã lượt, giờ hẹn, thời điểm không tới, bàn).
    - Khoá duy nhất theo lượt đặt, nên mỗi lượt chỉ được tính một lần.
    - Không khoá ngoại tới `Reservations`, để lịch sử còn nguyên khi Quản lý xoá hẳn lượt đã kết thúc.
    - Các lượt đã "không tới" từ trước được đưa vào lịch sử.
  - **`tr_Reservations_NoShowHistory`**: mọi lần chuyển sang "Khách không tới" đều ghi lịch sử trong cùng giao dịch. Áp dụng cả với nút cũ "Khách không tới" ở lịch bàn của Quản lý.
  - **`fn_ReservationWarnAt` và `fn_LateReservationAlerts(@NowUtc)`**: một nguồn quy tắc duy nhất cho cả hai màn hình và cho thao tác đánh dấu.
  - **`usp_MarkLateReservationNoShow`**: làm trong **một giao dịch** các việc sau:
    - đổi lượt sang `NoShow` và lưu `NoShowAt`;
    - ghi lịch sử số điện thoại;
    - ghi sự kiện (ai đánh dấu);
    - giải phóng bàn. Trigger trạng thái bàn có sẵn sẽ đẩy thay đổi lên mọi sơ đồ đang mở.
- **Chống ghi trùng.** Thủ tục giữ khoá thao tác chung (`usp_LockOperations`), nên các lần gửi cùng lúc chạy lần lượt.
  - Lần đầu ghi nhận.
  - Các lần sau nhận "đã được đánh dấu trước đó, không ghi thêm", số lần không tới không tăng.
  - Giao diện cũng khoá nút trong lúc đang gửi.
- **Khách đã tới trước khi xác nhận.** Nếu lượt đã chuyển "Khách đã tới", thao tác bị từ chối ("Khách đã tới nên không đánh dấu…") và bàn không bị giải phóng.
- **API** (`ReservationAlertsController`, Quản lý và Phục vụ):
  - `GET /api/reservation-alerts`: cảnh báo hiện tại, số điện thoại đã che (`090****123`), và `nextWarnAtUtc` là mốc cảnh báo kế tiếp. Gọi định kỳ nên không kéo dài phiên đăng nhập đang để yên.
  - `POST /api/reservation-alerts/{id}/no-show`: đánh dấu khách không tới, có mã chống giả mạo.
- **Sơ đồ bàn** (`/`):
  - Khung đỏ "Đặt bàn trễ quá 15 phút, khách chưa tới" liệt kê bàn (hoặc "Chưa xếp bàn"), tên khách, số khách, số điện thoại đã che, giờ hẹn, số phút trễ và nút **Đánh dấu khách không tới**.
  - Thẻ bàn tương ứng có viền đỏ, dòng "Trễ … phút · tên khách" và nút **Khách không tới**.
  - Đánh dấu xong, bàn hiện **Trống** ngay và cảnh báo được gỡ.
- **Danh sách đặt bàn** (`/ReservationManagement`):
  - Nhóm "Đang trễ quá 15 phút – khách chưa tới" lên đầu, dòng tô đỏ.
  - Ô trạng thái có nhãn "Trễ … phút" và nút **Khách không tới**.
  - Đánh dấu xong, danh sách tải lại, lượt đó thành "Khách không tới".
- **Tự cập nhật, không cần tải lại trang.** Hai màn hình tải lại cảnh báo:
  - mỗi 30 giây;
  - đúng lúc lượt kế tiếp đạt mốc (hẹn giờ theo `nextWarnAtUtc` và đồng hồ máy chủ, nên không lệch khi đồng hồ máy khách sai);
  - khi quay lại thẻ trình duyệt.

  Sơ đồ bàn còn tải lại khi nhận thay đổi trạng thái bàn. Vì vậy khi một nhân viên đánh dấu, màn hình của nhân viên khác cập nhật bàn ngay, và cảnh báo được gỡ chậm nhất sau 30 giây.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/049_S309LateReservationNoShow.sql` |
| Model / dữ liệu | `Models/Reservations/LateReservations.cs` (mới), `Models/Reservations/DailyReservations.cs` (thêm `Id`, thông tin trễ, `NextWarnAtUtc`), `Services/ReservationPhoneNormalizer.cs` |
| Controller | `Controllers/Reservations/ReservationAlertsController.cs` (mới), `Controllers/Reservations/ReservationManagementController.cs` |
| View / JS / CSS | `Views/Home/Index.cshtml`, `Views/ReservationManagement/Index.cshtml`, `wwwroot/js/reservation-no-show.js` (mới), `wwwroot/js/table-map.js`, `wwwroot/js/daily-reservations.js`, `wwwroot/css/site.css` |
| Phân quyền | `tools/RestaurantManagement.DbTool/ApiAccessMatrix.cs` |
| Test | `tests/RestaurantManagement.AreaTests/LateReservationTests.cs`, `tools/RestaurantManagement.DbTool/LateReservationVerification.cs` (chạy trong `verify` và lệnh riêng `verify-late-reservations`) |

## Demo

1. Chạy `dotnet run --project tools/RestaurantManagement.DbTool -- migrate`, rồi chạy lại web.
2. Tạo một lượt đặt đã xác nhận cho bàn (ví dụ `LAU01`), giờ hẹn 14 phút trước, và để bàn ở trạng thái **Đã đặt trước**.
3. Mở **Sơ đồ bàn** và **Danh sách đặt bàn**: chưa có cảnh báo. Khoảng 1 phút sau, khi trễ đúng 15 phút, cả hai màn hình tự hiện cảnh báo mà không phải tải lại.
4. Bấm **Khách không tới** rồi xác nhận. Bàn `LAU01` lập tức hiện **Trống**, cảnh báo biến mất, thông báo "Số điện thoại này đã có 1 lần không tới".
5. Kiểm tra lịch sử:

```sql
SELECT Phone, ReservationCode, AppointmentAt, NoShowAt FROM dbo.ReservationNoShowHistory ORDER BY NoShowAt DESC;
SELECT Phone, COUNT(*) AS SoLanKhongToi FROM dbo.ReservationNoShowHistory GROUP BY Phone;
```

## Kiểm thử

| Trường hợp | AreaTests | verify (SQL Server + web thật) | Giao diện (Chromium) |
| --- | --- | --- | --- |
| Trước mốc (−1 ms), tại mốc, sau mốc | ✓ (quy tắc) | ✓ | ✓ |
| Khách đã tới, lượt đã huỷ không bị cảnh báo | ✓ | ✓ | ✓ |
| Cảnh báo xuất hiện trên cả hai màn hình mà không tải lại | – | ✓ (API + danh sách) | ✓ |
| Đánh dấu không tới giải phóng bàn, gỡ cảnh báo, lưu đúng lịch sử | ✓ (thông báo) | ✓ | ✓ |
| Gửi lặp không tăng số lần không tới | ✓ | ✓ | ✓ |
| Nhiều nhân viên cùng bấm (6 yêu cầu song song) chỉ ghi một lần | – | ✓ | ✓ (khoá nút) |
| Khách vừa được đánh dấu đã tới thì không bị giải phóng bàn nhầm | – | ✓ | ✓ |
| Chưa tới mốc thì từ chối; Bếp bị chặn | – | ✓ | – |
| Chuẩn hoá số điện thoại gom lịch sử của cùng khách | ✓ | ✓ (2 lần cùng số) | – |

```
dotnet run --project tests/RestaurantManagement.AreaTests
node --test tools/tests/daily-reservations.test.cjs
dotnet run --project tools/RestaurantManagement.DbTool -- verify-late-reservations
```
