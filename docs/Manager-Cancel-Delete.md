# Quản lý: huỷ đặt bàn và xoá bàn

Hai chức năng này chỉ dành cho vai trò **Quản lý** (`[Authorize(Roles = "Manager")]`). Ở tầng database, thủ tục còn kiểm tra quyền `Reservations.Manage` và `Catalog.Manage`. Migration: `028_ManagerCancelAndDeleteTable.sql`, cần migration 027 chạy trước.

## Huỷ đặt bàn

**Cách thao tác:** vào **Đặt bàn → bấm mã đặt bàn → khu vực “Huỷ đặt bàn”**, nhập **lý do** (bắt buộc), bấm **Huỷ đặt bàn**, rồi xác nhận.

**Điều kiện:** chỉ huỷ được lượt đang *Chờ xác nhận* hoặc *Đã xác nhận*. Lượt đã huỷ, bị từ chối, khách đã đến hoặc khách không đến thì không còn nút huỷ.

**Thủ tục `usp_StaffCancelReservation`:**
- Chuyển lượt đặt sang *Đã huỷ*, lưu lý do, thời điểm huỷ và người huỷ (trong `ReservationEvents`).
- Huỷ email nhắc lịch.
- Xếp email báo huỷ vào hàng đợi.
- Nếu bàn đang ở trạng thái *Đã đặt trước* thì trả bàn về *Trống*.

**Sau khi huỷ:**
- Bàn xuất hiện lại trong danh sách bàn trống của khung giờ đó, khách khác đặt được ngay.
- **Email báo huỷ** được gửi ngay, gồm tiêu đề “Đã huỷ đặt bàn A05 – Bếp Nhà”, mã đặt bàn, ngày giờ và lý do huỷ. Kết quả gửi hiện trong khu vực *Email xác nhận* của trang chi tiết.
- Nếu khách không có email hoặc email gửi lỗi, trang báo cho quản lý gọi điện cho khách, kèm số điện thoại.

## Xoá lượt đặt bàn đã kết thúc

Huỷ đặt bàn chỉ đổi trạng thái sang *Đã huỷ*, lượt đặt vẫn nằm trong danh sách để tra cứu. Muốn dọn khỏi danh sách thì quản lý **xoá** lượt đó (migration `029_ManagerDeleteReservation.sql`).

**Cách thao tác:** ở **Danh sách đặt bàn**, nút **“Xoá”** nằm cạnh trạng thái của lượt đã kết thúc; hoặc vào **chi tiết đặt bàn → “Xoá lượt đặt bàn”**. Bấm rồi xác nhận.

**Thủ tục `usp_DeleteReservation`:**

| Tình huống | Kết quả |
| --- | --- |
| Lượt *Đã huỷ*, *Bị từ chối*, *Khách không đến* | Xoá hẳn lượt đặt cùng lịch sử trạng thái và email của lượt đó |
| Lượt *Chờ xác nhận*, *Đã xác nhận*, *Khách đã đến* | Không xoá: “Hãy huỷ lượt đặt bàn trước khi xoá.” (khách đã đến thì không có nút xoá) |
| Lượt gắn với phiên phục vụ chưa đóng | Không xoá |
| Lượt gắn với phiên phục vụ đã đóng | Xoá lượt đặt, phiên phục vụ và hoá đơn vẫn giữ (chỉ bỏ liên kết) |

Xoá không thể hoàn tác. Chỉ **Quản lý** thấy nút xoá; thủ tục kiểm tra thêm quyền `Reservations.Manage`.

## Xoá bàn

**Cách thao tác:** vào **Khu vực & bàn → nút “Xoá” trên thẻ bàn**, hoặc vào trang **Mã QR** của bàn → **Xoá bàn**, rồi xác nhận.

**Thủ tục `usp_DeleteTable`:**

| Tình huống | Kết quả |
| --- | --- |
| Bàn đang phục vụ khách | Không xoá: “Bàn đang phục vụ khách, chưa thể xoá.” |
| Bàn còn lượt đặt sắp tới (*Chờ xác nhận* hoặc *Đã xác nhận*) | Không xoá. Thông báo ghi giờ của lượt gần nhất và nhắc huỷ hoặc chuyển bàn trước. |
| Bàn chưa từng dùng (không có đặt bàn, phiên phục vụ hay món đã gọi) | **Xoá hẳn** bàn cùng mã QR. |
| Bàn đã có lịch sử | **Chuyển sang “Ngừng sử dụng”** và thu hồi mã QR. Lịch sử đặt bàn và hoá đơn cũ vẫn giữ nguyên, bàn không còn nhận đặt bàn mới. |

Lý do không xoá hẳn bàn đã có lịch sử: đặt bàn, phiên phục vụ và món đã gọi đều trỏ tới bàn đó. Nếu xoá sẽ mất lịch sử và báo cáo.

## Kiểm thử

- **`BookingConfirmationEmailTests`** (không cần database):
  - email báo huỷ có mã đặt bàn và lý do, không có hướng dẫn đến quán;
  - email báo huỷ được gửi ngay;
  - chỉ lượt *Chờ xác nhận* / *Đã xác nhận* mới huỷ được;
  - `Cancel` và `Delete` chỉ dành cho Quản lý;
  - chỉ lượt *Đã huỷ* / *Bị từ chối* / *Khách không đến* mới xoá được.
- **`BookingConfirmationVerification`** (lệnh `verify`, HTTP thật, SQL Server thật):
  - Không xoá được bàn còn lượt đặt sắp tới.
  - Bếp không thấy nút huỷ và gửi yêu cầu huỷ trực tiếp cũng bị từ chối.
  - Quản lý huỷ mà để trống lý do thì bị từ chối. Huỷ có lý do thì:
    - trạng thái, lý do và người huỷ được lưu;
    - email báo huỷ được gửi;
    - bàn trống trở lại.
  - Xoá bàn đã có lịch sử thì bàn chuyển sang *Ngừng sử dụng*. Xoá bàn mới chưa dùng thì bàn bị xoá hẳn.
