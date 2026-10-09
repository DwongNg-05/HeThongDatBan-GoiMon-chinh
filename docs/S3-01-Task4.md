# S3-01 Task 4: Thanh toán đóng phiên, quét lại QR mở phiên mới

Khi bàn được thanh toán, phiên gọi món cũ kết thúc. Lần quét QR tiếp theo của **cùng mã QR** tạo một phiên mới hoàn toàn, không mang theo món nào của phiên đã thanh toán.

**AC hoàn tất:** phiên tự đóng khi thanh toán, và quét lại QR sau đó mở phiên mới.

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Khi nào bàn được coi là **đã thanh toán**? | Đúng lúc thu ngân bấm **Thanh toán** thành công (`usp_Checkout` tạo hoá đơn `Paid`), bằng tiền mặt hay chuyển khoản. Thanh toán chỉ được làm khi mọi món đã phục vụ hoặc đã huỷ |
| Khi nào phiên được **đóng**? | Ngay trong giao dịch thanh toán: phiên → `Closed` (ghi `ClosedAt`, `ClosedBy`), trả bàn (`SessionTables.ReleasedAt`), thu hồi mọi phiên khách của phiên đó. Bàn gộp: đóng cả các phiên con |
| Bàn về **“trống”** khi nào? | Thanh toán xong bàn chuyển **Đang dọn**. Nhân viên báo **dọn xong** (`usp_CleanTable` hoặc đổi trạng thái trên Sơ đồ bàn) thì bàn về **Trống**; nếu có lượt đặt đã xác nhận trong 30 phút tới thì về **Đã đặt trước**. Trong lúc đang dọn, quét QR bị chặn (Task 3) |
| Mã QR sau thanh toán? | **Giữ nguyên**: thanh toán không đụng tới `TableQrCodes`, không cần in lại |
| Huỷ hoá đơn (`usp_VoidInvoice`)? | Không mở lại phiên cũ. Phiên đã đóng vẫn đóng |

Câu hỏi cho PO: có muốn bỏ bước **Đang dọn**, cho bàn về **Trống** ngay sau khi thanh toán không? Nếu đồng ý, chỉ cần sửa `usp_Checkout` ở một migration mới. Hiện giữ bước dọn để bàn kế tiếp luôn sạch.

## Cách hoạt động

Phần đóng phiên đã có sẵn trong `usp_Checkout` (003). Task này kiểm chứng toàn bộ chuỗi và bổ sung phía khách:

1. **Thanh toán** (`/Cashier/Checkout`, gọi `usp_Checkout`): hoá đơn `Paid`, phiên `Closed`, bàn được trả, phiên khách bị thu hồi, bàn chuyển **Đang dọn**, mã QR giữ nguyên.
2. **Điện thoại khách** đang mở `/TableOrder` sẽ thấy trang mới **“Bàn A05 đã thanh toán — phiên gọi món đã kết thúc lúc HH:mm. Cảm ơn quý khách!”** khi tải lại trang (`Views/TableOrder/Ended.cshtml`). Trang này **không** hiện lại món của phiên đã thanh toán.
3. **Dọn xong → bàn Trống → quét lại cùng QR**: `usp_StartQrGuestSession` thấy bàn trống, không có phiên mở, nên tạo **đúng một** phiên mới (nhánh Task 1), cấp cookie mới và chuyển bàn **Trống → Đang phục vụ** (có sự kiện cho Sơ đồ bàn). Cookie cũ đã bị thu hồi nên không thể “nối lại” phiên cũ.
4. **Món của phiên mới**: danh sách “Món bàn đã gọi” đọc theo mã phiên mới, nên trống cho tới khi khách gọi món. Món và hoá đơn của phiên cũ vẫn nằm trong lịch sử của nhà hàng.
5. Khách khác quét cùng QR sau đó sẽ vào **chung phiên mới** (Task 2).

## Thay đổi

| Lớp | File |
| --- | --- |
| Service | `GuestTableSessionService.GetEndedSessionAsync`: nhận ra phiên của điện thoại đã đóng do thanh toán |
| Controller/View | `TableOrderController.Index` hiển thị `Views/TableOrder/Ended.cshtml` thay cho trang trống |
| Test | `QrGuestSessionVerification.PaidThenRescan` (chạy bằng `verify-qr-session` / `verify`) |
| Database | Không cần migration mới |

## Demo

1. Khách quét QR bàn trống, gọi món.
2. **Bếp** chuyển món sang Đang nấu rồi Xong, **Phục vụ** mang ra, **Thu ngân** mở ca (nếu chưa mở) rồi **Thanh toán** bàn.
3. Điện thoại khách tải lại trang: thấy “Bàn … đã thanh toán”, không còn món cũ.
4. Trên **Sơ đồ bàn**, bàn đang ở **Đang dọn**. Quét QR lúc này nhận “Bàn đang được dọn”. Chuyển bàn sang **Trống** (dọn xong).
5. Quét lại **cùng mã QR**: mở trang gọi món mới, mục “Món bàn đã gọi” trống, bàn chuyển **Đang phục vụ**.

```sql
SELECT s.Id, s.Status, s.OpenedAt, s.ClosedAt, s.OpenedByQrCodeId
FROM dbo.DiningSessions s JOIN dbo.SessionTables st ON st.SessionId = s.Id
JOIN dbo.DiningTables t ON t.Id = st.TableId
WHERE t.Code = 'A05' ORDER BY s.Id;   -- phiên cũ Closed, phiên mới Open, cùng OpenedByQrCodeId (mã QR không đổi)
```

## Kiểm thử

`PaidThenRescan` chạy các bước:

1. Mở phiên bằng QR, gọi món.
2. Bếp nấu, phục vụ mang ra, thu ngân thanh toán.
3. Kiểm tra: phiên cũ `Closed`, có hoá đơn `Paid`, bàn đã trả và **Đang dọn**, phiên khách bị thu hồi, mã QR vẫn hiệu lực.
4. Trang khách báo “đã thanh toán”, không còn món cũ; quét QR khi đang dọn bị chặn.
5. Dọn xong → bàn Trống → quét lại cùng QR: tạo **đúng một** phiên mới khác phiên cũ, cùng mã QR, chưa có món; bàn **Đang phục vụ** với đúng một sự kiện Trống → Đang phục vụ mới.
6. Trang mới không hiện món của phiên cũ. Khách thứ hai quét thì vào chung phiên mới.
