# Cập nhật database để chạy mã hiện tại và Task 1

**Trạng thái: đã hoàn tất ngày 15/10/2026 theo yêu cầu người dùng.** Đã áp dụng đủ 18 migration vào `RestaurantManagement_Dev` trên `lpc:.\SQLEXPRESS`; kiểm tra sau cập nhật: 0 migration còn thiếu, 0 migration đã áp dụng bị đổi; `QuickNotes`, `OriginSessionId` và `TableAdjacencies` đều có. Danh sách bên dưới ghi lại tình trạng trước cập nhật.

Đã kiểm tra chỉ đọc: `lpc:.\SQLEXPRESS`, database `RestaurantManagement_Dev`.

Kết quả: thiếu `OrderItems.QuickNotes`, `OrderBatches.OriginSessionId`, bảng `TableAdjacencies`; 18 migration chưa áp dụng, không có migration đã áp dụng bị đổi nội dung.

Các bản cập nhật cần áp dụng theo thứ tự công cụ hiện có:

- 038_S302OrderQuantityMaximum.sql
- 043_S301QrGuestSession.sql
- 044_S301QrOpeningHours.sql
- 045_S301JoinOpenTableSession.sql
- 046_S301QrVersionAndCleaning.sql
- 047_S303QuickNotes.sql
- 048_S304KeepOrderNotes.sql
- 049_S309LateReservationNoShow.sql
- 050_S209EmailAfterStaffConfirm.sql
- 051_S302QuantityLimitProcedure.sql
- 052_EnglishOrderingCartTableNames.sql
- 053_DropUnusedTables.sql
- 054_S309ExtendReservationHold.sql
- 055_S309PhoneNoShowWarning.sql
- 056_S305CancelOrderItem.sql
- 057_S305ShiftCancellationReport.sql
- 058_S305ChargedCancellation.sql
- 059_MergeTablesTask1.sql

Thay đổi đáng chú ý: thêm cột và bảng còn thiếu, đổi tên bảng giỏ gọi món cũ, cập nhật thủ tục nghiệp vụ và quyền, chuyển các đợt gọi món của nhóm cũ về phiên chính. Migration 053 chỉ xoá ba bảng cũ nếu chúng trống; nếu còn dữ liệu thì giữ lại. Không seed lại, xoá database, đặt lại tài khoản hay mật khẩu. Migration 059 giữ ID dòng món, giá, số lượng và bàn gốc; chỉ đổi phiên sở hữu và số thứ tự đợt của các nhóm cũ.

Mỗi migration chạy trong giao dịch riêng và kiểm tra mã băm trước khi áp dụng. Nếu một bước lỗi, bước đó được rollback; các bước đã thành công trước đó vẫn giữ nguyên và công cụ tiếp tục từ đó khi chạy lại.

Lệnh chỉ đọc để kiểm tra:

```powershell
$env:RM_CONNECTION_STRING = 'Server=lpc:.\SQLEXPRESS;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet tools/RestaurantManagement.DbTool/bin/Debug/net10.0/RestaurantManagement.DbTool.dll migration-status
```

Sau khi được người dùng cho phép, dừng phiên debug và dùng cùng chuỗi kết nối để chạy công cụ `migrate`. Chạy lại `migration-status`, xác nhận không còn migration thiếu, rồi kiểm tra màn hình Bếp và toàn bộ luồng gộp bàn.

Việc cập nhật từng bị kiểm duyệt tự động chặn ở lượt trước. Trong lượt hoàn tất hai yêu cầu còn lại, người dùng đã yêu cầu thực hiện việc cập nhật database; lần chạy được cho phép và đã áp dụng thành công. Không seed lại dữ liệu hoặc thay tài khoản/mật khẩu.

## Kết quả rà soát bổ sung

- Build hiện tại: 0 lỗi; còn 1 cảnh báo giấy phép ImageSharp.
- Bộ hồi quy: 799 kiểm thử đạt.
- Bộ nghiệm thu gộp bàn trên SQL/web thật: đạt, gồm gọi thêm bằng QR, gộp đồng thời và thanh toán chung.
- Bộ nâng cấp từ schema cũ thiếu QuickNotes có sẵn nhóm bàn và món: đạt, không mất dòng món, giá, số lượng, ghi chú hay bàn gốc; màn hình Bếp và hai bàn mở được sau nâng cấp.
- Thêm kiểm tra schema chỉ đọc cho các màn hình phụ thuộc. Database chưa đủ cấu trúc sẽ hiện thông báo cần cập nhật (HTTP 503), không tự chạy migration và không che giấu bằng dữ liệu mẫu.
