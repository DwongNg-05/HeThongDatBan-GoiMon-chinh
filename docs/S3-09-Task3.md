# S3-09 Task 3: Cảnh báo số điện thoại có ít nhất 3 lần không tới trong 90 ngày

Khi nhân viên tạo đặt bàn, hệ thống cảnh báo nếu số điện thoại đã có ít nhất 3 lần khách không tới trong 90 ngày.

**Hoàn tất:** AC 4. Cùng với Task 1 và Task 2, story S3-09 hoàn tất toàn bộ AC.

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Cửa sổ 90 ngày tính thế nào? | **Trượt**: tính ngược đúng 90 × 24 giờ từ thời điểm kiểm tra (lúc nhân viên nhập số, và lúc bấm tạo). Không làm tròn theo ngày lịch. |
| Mốc biên | **Có tính.** Lần không tới đúng 90 ngày trước vẫn được đếm. Sớm hơn 1 ms thì không. Thời điểm dùng để so là `NoShowAt`, tức lúc nhân viên đánh dấu không tới. |
| Ngưỡng | Ít nhất **3** lần. |
| Đếm theo gì? | Số điện thoại đã chuẩn hoá (10 chữ số, đầu 0). Nhập `0985 309 303`, `+84 985 309 303`, `84985309303` hay `098.530.9303` đều cho cùng kết quả. Form cũng tự đổi về `0985309303`. |
| Có cho đặt tiếp không? | **Có, nhưng phải xác nhận.** Cảnh báo hiện kèm ô "Tôi đã trao đổi với khách và vẫn tạo đặt bàn". Chưa đánh dấu thì máy chủ không tạo lượt đặt. |
| Ai thấy cảnh báo? | **Chỉ Quản lý và Phục vụ** trên form đặt hộ khách (`/Reservations/Create` khi đã đăng nhập) và form tạo lượt thủ công (`/TableReservations/Create`). **Khách tự đặt không thấy gì và không bị chặn.** Nếu không, bất kỳ ai cũng có thể gõ số điện thoại của người khác để xem lịch sử không tới của họ. |

## Cách hoạt động

- **Nguồn dữ liệu:** `dbo.ReservationNoShowHistory` từ Task 1. Mỗi lượt đặt có tối đa một dòng, và lịch sử vẫn còn khi lượt đặt bị xoá.
- **Database:** migration `055_S309PhoneNoShowWarning.sql` (chạy `migrate`) thêm `dbo.fn_PhoneNoShowCount(@Phone, @NowUtc)`, trả số lần và lần gần nhất trong cửa sổ.
- **Tra cứu khi nhập hoặc đổi số điện thoại** (`phone-no-show-warning.js`):
  - Gọi `POST /api/reservation-alerts/phone-history` sau 350 ms kể từ phím cuối, và mỗi khi rời ô. Số điện thoại nằm trong thân yêu cầu, không nằm trên URL. Chỉ Quản lý và Phục vụ gọi được.
  - Chỉ kết quả của lần tra cứu mới nhất được hiển thị.
  - Đạt ngưỡng thì hiện khung vàng, ví dụ "Số điện thoại này có 3 lần khách không tới trong 90 ngày gần nhất (gần nhất ngày dd/MM/yyyy)…", kèm ô xác nhận.
  - Đổi sang số chưa đạt ngưỡng thì khung ẩn đi và ô xác nhận được bỏ đánh dấu.
- **Kiểm tra lại khi gửi form** (`PhoneNoShowGuard`, dùng chung cho cả hai form):
  - Máy chủ đếm lại ngay lúc gửi.
  - Form gửi kèm `NoShowCountAcknowledged`, là số lần nhân viên đã thấy khi xác nhận. Nếu lịch sử tăng trong lúc nhân viên điền form (vừa có lượt khác bị đánh dấu không tới), số hiện tại lớn hơn số đã xác nhận. Khi đó form hiện lại với số mới và yêu cầu xác nhận lại.
  - Không đọc được lịch sử (database lỗi hoặc chưa chạy migration) thì không chặn đặt bàn.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/055_S309PhoneNoShowWarning.sql` (mới) |
| Model / quy tắc | `Models/Reservations/PhoneNoShowWarning.cs` (mới: `PhoneNoShowSummary`, `PhoneNoShowRules`, `PhoneNoShowStore`, `IPhoneNoShowForm`, `PhoneNoShowGuard`), `Models/Reservations/ReservationViewModels.cs`, `Models/Reservations/TableReservationScheduleViewModels.cs` |
| Controller | `ReservationAlertsController.cs` (`POST phone-history`), `ReservationsController.cs`, `TableReservationScheduleController.cs` |
| View / JS | `Views/Shared/_PhoneNoShowWarning.cshtml` (mới), `Views/Reservations/Create.cshtml`, `Views/TableReservationSchedule/Create.cshtml`, `wwwroot/js/phone-no-show-warning.js` (mới) |
| Phân quyền | `tools/RestaurantManagement.DbTool/ApiAccessMatrix.cs` |
| Test | `tests/RestaurantManagement.AreaTests/PhoneNoShowWarningTests.cs` (mới), `tools/tests/phone-no-show-warning.test.cjs` (mới), `tools/RestaurantManagement.DbTool/PhoneNoShowVerification.cs` (mới, chạy trong `verify`, `verify-late-reservations`, `verify-hold-extension` và lệnh mới `verify-phone-no-show`) |

## Demo

1. Chạy `dotnet run --project tools/RestaurantManagement.DbTool -- migrate`, rồi chạy lại web.
2. Tạo nhanh lịch sử 3 lần không tới cho một số điện thoại (dữ liệu demo, có thể xoá sau):

   ```sql
   INSERT dbo.ReservationNoShowHistory(Phone, ReservationId, ReservationCode, AppointmentAt, NoShowAt, TableId) VALUES
    ('0985309303', -1, 'DEMO01', DATEADD(day,-2,SYSUTCDATETIME()),  DATEADD(day,-2,SYSUTCDATETIME()),  NULL),
    ('0985309303', -2, 'DEMO02', DATEADD(day,-30,SYSUTCDATETIME()), DATEADD(day,-30,SYSUTCDATETIME()), NULL),
    ('0985309303', -3, 'DEMO03', DATEADD(day,-60,SYSUTCDATETIME()), DATEADD(day,-60,SYSUTCDATETIME()), NULL);
   -- Xoá dữ liệu demo: DELETE dbo.ReservationNoShowHistory WHERE ReservationId IN (-1,-2,-3);
   ```

3. Đăng nhập bằng tài khoản Phục vụ hoặc Quản lý, mở **Đặt bàn**. Gõ `0985 309 303`: khung vàng hiện "có 3 lần khách không tới trong 90 ngày gần nhất". Rời ô thì số được đổi thành `0985309303`.
4. Đổi sang một số chưa đạt ngưỡng, ví dụ `0985309300`: khung cảnh báo biến mất.
5. Nhập lại số có cảnh báo và bấm **Đặt bàn** mà không đánh dấu ô xác nhận: form hiện lại và yêu cầu xác nhận. Đánh dấu rồi bấm lại: lượt đặt được tạo.
6. Đăng xuất rồi đặt bàn bằng chính số đó với vai trò khách: không có cảnh báo, đặt bình thường.

## Kiểm thử

| Trường hợp | AreaTests | node | verify (SQL Server + web thật) |
| --- | --- | --- | --- |
| 0, 2, 3, hơn 3 lần | ✓ (quy tắc) | ✓ | ✓ |
| Lần ngoài 90 ngày (91, 120 ngày) không tính | – | – | ✓ |
| Mốc biên: đúng 90 ngày tính, sớm hơn 1 ms không tính; 1 ms sau mốc thì lần đó rơi khỏi cửa sổ; lần sau thời điểm kiểm tra không tính | ✓ (SQL) | – | ✓ |
| Cách nhập tương đương cho cùng kết quả; số được lưu ở dạng chuẩn | ✓ | ✓ | ✓ |
| Đổi số điện thoại cập nhật cảnh báo; chỉ kết quả mới nhất được hiển thị | – | ✓ | – |
| Lịch sử tăng trong lúc tạo đặt bàn vẫn bị phát hiện khi gửi; xác nhận số cũ không đủ | ✓ | ✓ | ✓ |
| Đạt ngưỡng: phải xác nhận mới tạo được (cả form của Quản lý) | ✓ | – | ✓ |
| Khách tự đặt không thấy lịch sử, không bị chặn; Bếp và khách không gọi được API tra cứu | ✓ | – | ✓ |

```
dotnet run --project tests/RestaurantManagement.AreaTests
node --test tools/tests/phone-no-show-warning.test.cjs tools/tests/daily-reservations.test.cjs
dotnet run --project tools/RestaurantManagement.DbTool -- verify-phone-no-show
```
