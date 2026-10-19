# S2-03 — Chống trùng bàn và 15 phút dọn bàn

Mục tiêu: một bàn không thể được đặt cho hai lượt khách có thời gian sử dụng giao nhau, kể cả 15 phút dọn bàn.

## 1. Quy tắc tính giờ (cần PO chốt)

| Câu hỏi | Đề xuất đang áp dụng | Ghi chú |
| --- | --- | --- |
| Giờ kết thúc lượt đặt tính thế nào? | **Giờ bắt đầu + thời lượng mặc định** của nhà hàng (`RestaurantSettings.DefaultBookingMinutes`, mặc định **90 phút**, sửa ở màn hình *Giờ hoạt động*). Lưu vào `Reservations.EndsAt` lúc tạo lượt đặt. | Mọi lượt có cùng thời lượng, chưa cho chọn thời lượng riêng từng lượt. |
| 15 phút dọn bàn đặt ở đâu? | **Sau** giờ kết thúc của mỗi lượt. Lượt giữ bàn trong khoảng `[bắt đầu, kết thúc + 15 phút)`. | Đặt sau là đủ: hai lượt liền nhau luôn có 15 phút giữa lượt trước và lượt sau. |
| Khi nào hai lượt cùng bàn bị coi là trùng? | `A.bắt đầu < B.kết thúc + 15` **và** `B.bắt đầu < A.kết thúc + 15`. | |
| Bắt đầu đúng mốc hết 15 phút dọn bàn thì sao? | **Được nhận**. Ví dụ lượt 19:00–20:30: lượt sau bắt đầu 20:45 được, 20:40 không được. | |
| Lượt ở trạng thái nào giữ bàn? | **Chờ xác nhận** và **Đã xác nhận**. Kiểm tra trước khi ghi ở luồng khách đặt bàn còn tính thêm lượt **Khách đã tới**. | **Đã huỷ**, **Không tới**, **Từ chối** không giữ bàn. |

PO chốt (ký tên / ngày): ……………………………

## 2. Các lớp bảo vệ

1. **Ràng buộc database** (chốt chặn cuối): trigger `trg_Reservations_PreventTableHoldOverlap` (migration 028). Mọi câu lệnh `INSERT`/`UPDATE` vào `dbo.Reservations` tạo ra hai lượt Chờ xác nhận / Đã xác nhận cùng bàn giao nhau (tính 15 phút dọn bàn) đều bị từ chối với lỗi **51060**. Áp dụng cả khi ghi SQL trực tiếp, bỏ qua web và thủ tục.
2. **Kiểm tra trước khi ghi** trong từng thủ tục, với cùng quy tắc như trên:
   - `usp_CreateManagedTableReservation`: Quản lý tạo lượt đặt từ *Lịch đặt bàn*. Thủ tục này dựa thẳng vào trigger (lỗi 51060), trong lúc giữ khoá `usp_LockOperations`.
   - `usp_ConfirmReservation`: xác nhận và xếp bàn (lỗi 51009).
   - Đổi bàn sau khi xác nhận (migration 034).
   - `usp_CreateReservation`: khách chọn bàn (lỗi 51414). **Migration 064** bổ sung 15 phút dọn bàn; trước đó chỉ so khung giờ.
3. **Danh sách bàn trống cho khách** (`usp_AvailableTables`): **migration 064** ẩn bàn còn trong 15 phút dọn bàn. Trước đó khách vẫn thấy và chọn được một bàn mà khi lưu sẽ bị trigger từ chối.

## 3. Thông báo khi trùng lịch

| Màn hình | Thông báo | Dữ liệu đã nhập |
| --- | --- | --- |
| *Lịch đặt bàn → Tạo lượt đặt bàn* (Quản lý) | "**Bàn đã có người đặt** trong khung giờ này (tính cả 15 phút dọn bàn). Vui lòng chọn giờ hoặc bàn khác." Kèm các giờ gần nhất còn trống để bấm chọn. | Giữ nguyên: bàn, ngày, giờ, tên khách, điện thoại, trạng thái. |
| *Đặt bàn* (khách chọn bàn) | "Bàn bạn chọn vừa có khách khác đặt trong khung giờ này (tính cả 15 phút dọn bàn)…", hiện ngay dưới ô chọn bàn. | Giữ nguyên toàn bộ form. |
| *Xác nhận đặt bàn* (nhân viên xếp bàn) | "Bàn vừa được giữ trong khung giờ này. Vui lòng chọn bàn khác." | Giữ nguyên lượt đặt ở trạng thái Chờ xác nhận. |

Lượt bị từ chối **không ghi gì** vào database.

## 4. Kiểm thử

Tự động: `dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify`. Bộ này tạo database riêng, chạy rồi xoá.

| Trường hợp | Ca kiểm thử |
| --- | --- |
| Cùng bàn, hai khung giờ giao nhau → từ chối | `Managed table reservation rejects full overlap`, `… overlap at start` |
| Cách nhau **đúng 15 phút** → được nhận | 20:45 sau lượt 19:00–20:30; `Customer booking exactly 15 minutes after a confirmed reservation is accepted`; `Direct SQL insert at the exact end of the cleaning buffer is accepted` |
| Nghỉ **dưới 15 phút** → từ chối | `Managed table reservation rejects cleanup interval` (20:40); `Customer booking with a break under 15 minutes…` |
| Trạng thái Chờ xác nhận và Đã xác nhận | `Confirmed reservation blocks pending reservation`; `Pending reservation blocks an overlapping confirmed reservation` |
| Bỏ qua giao diện vẫn bị chặn | `Direct SQL insert cannot bypass table hold`; `Direct SQL insert inside the cleaning buffer…`; `Direct SQL update moving a reservation into the cleaning buffer…`; `Direct SQL overlap guard` |
| Danh sách bàn trống tính 15 phút dọn bàn | `Free-table list hides a table inside the 15-minute cleaning buffer…` |
| Lượt huỷ / không tới không giữ bàn | phần cuối `VerifyTableReservationSchedule` |

Thủ công (SSMS): chạy `docs/S2-03-KiemTraChongTrung.sql`. Script in PASS/FAIL cho 7 bước và tự xoá dữ liệu thử.

## 5. Demo

1. Chạy `migrate` (phải thấy `Applied: 064_S203TableHoldCleaningBuffer.sql`).
2. Đăng nhập **Quản lý** → *Lịch đặt bàn* → chọn ngày → **Tạo lượt đặt bàn**: bàn LAU01, 19:00 → thành công (lịch hiện 19:00–20:30, dọn bàn tới 20:45).
3. Tạo lại LAU01 lúc **20:00**, rồi lúc **20:40** → báo *Bàn đã có người đặt*, form giữ nguyên dữ liệu, gợi ý giờ trống.
4. Tạo LAU01 lúc **20:45** → thành công. Tạo **bàn khác** lúc 19:00 → thành công.

## 6. Ngoài phạm vi task này

- Hai yêu cầu gửi **cùng lúc**: đã có khoá `usp_LockOperations` và kiểm thử đồng thời trong `verify`, nhưng xử lý trọn vẹn (thông báo, thử lại) làm ở task sau.
- **Giải phóng khung giờ** khi huỷ hoặc khách không tới: lượt Đã huỷ / Không tới đã không giữ bàn, nhưng luồng đầy đủ (trả trạng thái bàn, thông báo) làm ở task sau.
