# S3-09 Task 2: Gia hạn giữ bàn 15 phút (tối đa một lần)

Phục vụ (hoặc Quản lý) gia hạn giữ bàn thêm 15 phút cho một lượt đặt đang bị cảnh báo trễ. Mỗi lượt chỉ gia hạn được một lần.

**Hoàn tất:** AC 2, và vẫn giữ AC 1 và AC 3 của Task 1.

**Chưa làm trong task này:** cảnh báo khi số điện thoại có lịch sử không tới đặt bàn lần sau.

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Thời hạn mới tính thế nào? | **Thời điểm nhân viên bấm gia hạn + 15 phút.** Ví dụ hẹn 18:00, cảnh báo lúc 18:15, nhân viên bấm lúc 18:22, thì giữ bàn tới 18:37. Cách này giữ đủ 15 phút kể cả khi nhân viên xử lý muộn. Nếu PO chọn **giờ hẹn + 30 phút** (ví dụ trên sẽ là 18:30), chỉ cần sửa một dòng `SET @holdUntil = ...` trong `usp_ExtendLateReservationHold`, bằng một migration mới. |
| Khi nào được gia hạn? | Khi lượt **đang có cảnh báo trễ** (Chờ xác nhận hoặc Đã xác nhận, khách chưa tới, đã qua mốc) và **chưa gia hạn lần nào**. |
| Gia hạn lần hai? | Không được. Gửi lặp, hai nhân viên cùng bấm, hay bấm lại sau khi hết hạn đều bị từ chối: "Lượt đặt này đã được gia hạn một lần…". |
| Trong thời gian gia hạn | Không cảnh báo. Hai màn hình hiện "Gia hạn giữ bàn tới HH:mm". Bàn vẫn **Đã đặt trước**. |
| Hết thời hạn mà khách chưa tới | Cảnh báo tự hiện lại đúng lúc hết hạn, kèm ghi chú "Đã gia hạn 1 lần, hết hạn lúc HH:mm". Không còn nút gia hạn, chỉ còn **Khách không tới**. |
| Khách tới trong thời gian gia hạn | Không cảnh báo lại. Không gia hạn hay đánh dấu không tới được, nên bàn không bị giải phóng nhầm. |
| Đánh dấu không tới **trong lúc** đang gia hạn? | Chưa cho phép: nút chỉ có khi cảnh báo hiện lại. Nếu PO muốn cho phép, cần nới điều kiện trong `usp_MarkLateReservationNoShow`. |
| Ai được gia hạn? | Quản lý và Phục vụ (quyền `Reservations.Manage`). Bếp và Thu ngân bị chặn. |

## Cách hoạt động

- **Lưu trữ.** Bảng `Reservations` đã có sẵn `ExtensionCount` (ràng buộc 0–1) và `HoldExtendedUntil` từ migration 001. Migration `054_S309ExtendReservationHold.sql` (chạy `migrate`) thêm:
  - **`usp_ExtendLateReservationHold`**: làm trong **một giao dịch**, có khoá thao tác chung (`usp_LockOperations`):
    - kiểm tra trạng thái lượt đặt (đã tới, đã không tới, đã huỷ hay từ chối đều bị từ chối);
    - kiểm tra số lần gia hạn (`ExtensionCount`);
    - kiểm tra lượt đang có cảnh báo;
    - lưu `ExtensionCount = 1` và `HoldExtendedUntil`;
    - ghi sự kiện (ai gia hạn, tới mấy giờ).

    Câu `UPDATE` còn có điều kiện `ExtensionCount = 0`, nên không thể gia hạn hai lần kể cả khi nhiều yêu cầu tới cùng lúc.
  - **`fn_ExtendedReservationHolds(@NowUtc)`**: các lượt đang trong thời gian gia hạn, dùng để hiển thị thời hạn mới.
  - **`fn_ReservationWarnAt` và `fn_LateReservationAlerts`** trả thêm `ExtensionCount` và `HoldExtendedUntil`. Mốc cảnh báo của lượt đã gia hạn là `HoldExtendedUntil` (quy tắc có từ Task 1), nên cảnh báo tự tắt khi gia hạn và tự bật lại khi hết hạn.
- **API** (`ReservationAlertsController`):
  - `POST /api/reservation-alerts/{id}/extend`: gia hạn, có mã chống giả mạo. Trả 200 kèm thời hạn mới, hoặc 409 kèm lý do.
  - `GET /api/reservation-alerts` trả thêm:
    - mỗi cảnh báo có `canExtend` và `extensionLabel`;
    - danh sách `holds` là các lượt đang gia hạn.

    `nextWarnAtUtc` đã tính cả thời điểm hết hạn gia hạn, nên màn hình tải lại đúng lúc đó.
- **Sơ đồ bàn** (`/`):
  - Khung đỏ cảnh báo và thẻ bàn có nút **Gia hạn 15 phút** (nếu chưa gia hạn), cạnh nút **Khách không tới**.
  - Khung xanh **Đang gia hạn giữ bàn** liệt kê bàn, khách, giờ hẹn và "Gia hạn giữ bàn tới HH:mm". Thẻ bàn có viền xanh và dòng "Giữ bàn tới HH:mm · tên khách".
- **Danh sách đặt bàn** (`/ReservationManagement`):
  - Nhóm mới **Đang gia hạn giữ bàn – chờ khách tới** nằm ngay sau nhóm đang trễ, có nhãn thời hạn mới.
  - Lượt trễ chưa gia hạn có nút **Gia hạn 15 phút**. Lượt hết hạn có ghi chú "Đã gia hạn 1 lần…" và chỉ còn nút **Khách không tới**.
- **Giao diện chống bấm trùng.** Trong lúc gửi, mọi nút của cùng lượt đặt (gia hạn và không tới) bị khoá. Máy chủ vẫn tự chặn lần thứ hai.

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/054_S309ExtendReservationHold.sql` (mới) |
| Model / dữ liệu | `Models/Reservations/LateReservations.cs` (thêm `ExtendedReservationHold`, `HoldExtensionResult`, `ExtendHoldAsync`, các cột gia hạn trong cảnh báo), `Models/Reservations/DailyReservations.cs` (nhóm đang gia hạn, `CanExtend`, `HoldLabel`) |
| Controller | `Controllers/Reservations/ReservationAlertsController.cs` (`POST {id}/extend`) |
| View / JS / CSS | `Views/Home/Index.cshtml`, `Views/ReservationManagement/Index.cshtml`, `wwwroot/js/reservation-no-show.js` (`extend()`), `wwwroot/js/table-map.js`, `wwwroot/js/daily-reservations.js`, `wwwroot/css/site.css`, `wwwroot/css/staff-theme.css` |
| Phân quyền | `tools/RestaurantManagement.DbTool/ApiAccessMatrix.cs` |
| Test | `tests/RestaurantManagement.AreaTests/HoldExtensionTests.cs` (mới), `tools/tests/daily-reservations.test.cjs`, `tools/RestaurantManagement.DbTool/HoldExtensionVerification.cs` (mới, chạy trong `verify`, `verify-late-reservations` và lệnh mới `verify-hold-extension`) |

## Demo

1. Chạy `dotnet run --project tools/RestaurantManagement.DbTool -- migrate`, rồi chạy lại web.
2. Tạo một lượt đặt đã xác nhận cho bàn (ví dụ `LAU01`), giờ hẹn 16 phút trước, và để bàn ở trạng thái **Đã đặt trước**. Mở **Sơ đồ bàn** và **Danh sách đặt bàn**: cả hai đang cảnh báo đỏ, có nút **Gia hạn 15 phút**.
3. Bấm **Gia hạn 15 phút** rồi xác nhận. Cảnh báo đỏ biến mất. Sơ đồ bàn hiện khung xanh "Gia hạn giữ bàn tới HH:mm" và thẻ `LAU01` viền xanh. Danh sách đặt bàn chuyển lượt đó sang nhóm "Đang gia hạn giữ bàn".
4. Để xem lúc hết hạn mà không phải chờ 15 phút, cho thời hạn lùi về quá khứ:

   ```sql
   UPDATE dbo.Reservations SET HoldExtendedUntil = DATEADD(second, -1, SYSUTCDATETIME()) WHERE Code = '<mã lượt>';
   ```

   Chậm nhất sau 30 giây, hoặc ngay khi quay lại thẻ trình duyệt, cảnh báo đỏ hiện lại kèm "Đã gia hạn 1 lần…". Lúc này không còn nút gia hạn. Gửi gia hạn lần hai, ví dụ từ máy khác, sẽ bị từ chối.
5. Bấm **Khách không tới**: bàn `LAU01` hiện **Trống**, lịch sử số điện thoại có thêm đúng một lần không tới.

```sql
SELECT Code, Status, ExtensionCount, DATEADD(hour, 7, HoldExtendedUntil) AS GiuBanToiVN FROM dbo.Reservations WHERE Code = '<mã lượt>';
SELECT ReservationId, Reason, ActorUserId, OccurredAt FROM dbo.ReservationEvents WHERE Reason LIKE N'Gia hạn giữ bàn%' ORDER BY Id DESC;
```

## Kiểm thử

| Trường hợp | AreaTests | verify (SQL Server + web thật) | node |
| --- | --- | --- | --- |
| Thời hạn mới = lúc bấm + 15 phút, lưu `ExtensionCount = 1`, ghi sự kiện | ✓ (quy tắc) | ✓ | – |
| Cảnh báo tắt sau gia hạn; trước thời hạn 1 ms chưa cảnh báo, đúng thời hạn bật lại | – | ✓ | – |
| Hai màn hình hiện thời hạn mới (API sơ đồ bàn, danh sách đặt bàn) | ✓ | ✓ | ✓ |
| Hết hạn: cảnh báo lại, không còn nút gia hạn, gia hạn lần hai bị từ chối | ✓ | ✓ | ✓ |
| Khách tới trong thời gian gia hạn: không cảnh báo, không giải phóng bàn | – | ✓ | – |
| Gửi lặp và 6 yêu cầu song song: đúng một lần gia hạn | ✓ (SQL) | ✓ | – |
| Đánh dấu không tới sau gia hạn: giải phóng bàn, lịch sử đúng một lần | – | ✓ | – |
| Chưa tới mốc thì không gia hạn được; Bếp bị chặn; lượt chưa xếp bàn gia hạn được | – | ✓ | – |

```
dotnet run --project tests/RestaurantManagement.AreaTests
node --test tools/tests/daily-reservations.test.cjs
dotnet run --project tools/RestaurantManagement.DbTool -- verify-hold-extension
```
