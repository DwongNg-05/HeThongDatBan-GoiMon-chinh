# S4-01 Task 1: xem hoá đơn tạm tính của bàn đang phục vụ

Nhân viên mở màn hình **Tạm tính** cho một bàn đang phục vụ. Màn hình có danh sách món, số lượng, đơn giá, thành tiền, ghi chú và tổng cộng đúng. Xem tạm tính chỉ đọc dữ liệu: bàn vẫn ở trạng thái đang phục vụ và vẫn gọi thêm món bình thường.

**Hoàn tất:** AC 1, AC 4, AC 5. **Chưa làm (task sau):** phần đầu hoá đơn (tên quán, mã bàn, thời điểm, người in), in, xuất PDF.

## Quy tắc cần PO chốt (đề xuất, đã cài đặt)

Hai quy tắc dưới đây nằm cùng một chỗ, `BillRules` trong `Models/Orders/TableBillPreview.cs`. Đổi ở đó thì API, màn hình và kiểm thử đơn vị cùng đổi theo.

| Câu hỏi | Đề xuất (đã cài đặt) | Lý do |
| --- | --- | --- |
| Món huỷ **sau khi** bếp đã chế biến có cộng vào tổng cộng không? | **Có.** Món nằm ở nhóm riêng có nhãn **“Đã huỷ – có tính tiền”**, có dòng cộng riêng, và được tính vào **Tổng cộng**. | Giống quy tắc đang chạy ở S3-05 Task 3: `dbo.vw_SessionTotals`, hoá đơn khi thanh toán (`InvoiceLines.IsChargedCancellation`) và “Tạm tính hiện tại” trên chi tiết bàn đều tính món này. Nếu tạm tính không cộng mà hoá đơn lại cộng, khách sẽ thấy hai con số khác nhau. Nếu PO chọn “không cộng”, chỉ cần đặt `ChargedCancellationCountsInTotal = false`. Khi đó cần đổi cả `vw_SessionTotals` và phần thanh toán để mọi nơi khớp nhau. |
| Món huỷ **trước khi** bếp chế biến | Không xuất hiện trên tạm tính. | Theo mô tả task. |
| Làm tròn | Tiền là số nguyên đồng. Thành tiền = đơn giá × số lượng, làm tròn tới 1 đồng (nửa đồng làm tròn lên). Tổng cộng = tổng các thành tiền. **Không** làm tròn tới nghìn đồng. | Giá trong database là `decimal(18,0)`, nên trên thực tế không phát sinh số lẻ. Phép làm tròn chỉ để phòng khi sau này có giảm giá hoặc phụ thu theo phần trăm. |
| Hiển thị số tiền | Dấu **chấm** phân cách hàng nghìn, ký hiệu **“đ”** phía sau, cách một khoảng trắng: `1.250.000 đ`, `0 đ`. | Đây là cách ghi quen thuộc trên hoá đơn ở Việt Nam. Chữ “đ” in được trên mọi máy in nhiệt (ký hiệu “₫” thì không phải máy nào cũng in được, cần lưu ý cho task in sau này). Kết quả không phụ thuộc cấu hình ngôn ngữ của máy chủ. Lưu ý: các màn hình cũ (thực đơn, hoá đơn S3-04, thu ngân) vẫn dùng `Vnd.Format` → `1.250.000 ₫`. Nếu PO chọn “đ”, nên đổi cả những màn hình đó cho đồng bộ. Nếu chọn “₫” hoặc “VND”, chỉ cần đổi `BillRules.CurrencySymbol`. |

## Cách hoạt động

- **Điểm truy vấn** `GET /api/tables/{mã bàn}/provisional-bill` (Quản lý, Phục vụ) trả về JSON gồm:
  - `lines`: mỗi dòng có `id`, `itemName`, `unit`, `quantity`, `unitPrice`, `unitPriceText`, `amount`, `amountText`, `note`, `status`, `statusLabel`.
  - `chargedCancelled`: `{ label: "Đã huỷ – có tính tiền", lines, subtotal, subtotalText }`.
  - Các trường chung: `total`, `totalText`, `isEmpty`, `emptyMessage`, `chargedCancellationCountsInTotal`, `sessionId`, `tableStatus`.

  Bàn không tồn tại, hoặc bàn không có khách đang phục vụ, thì trả 404 kèm `message`.
- **Phạm vi dữ liệu:** lấy phiên chưa đóng đang giữ bàn, rồi lên phiên thanh toán gốc (`BillingSessionId`). Món được lấy từ phiên gốc và mọi phiên đã gộp vào, giống phạm vi của hoá đơn khi thanh toán.
- **Mỗi dòng đơn là một dòng.** Hai lần gọi cùng một món không bị gộp, vì ghi chú có thể khác nhau (giống S3-04 Task 4). Ghi chú gồm lựa chọn nhanh và ghi chú tự do, đọc qua `OrderItemRequests` giống màn hình bếp. Dòng được sắp theo giờ gọi, nên món gọi thêm nằm cuối.
- **Chỉ đọc** (`TableBillPreviewStore`):
  - chỉ có câu `SELECT`;
  - chạy trong một giao dịch `ReadCommitted` luôn bị `ROLLBACK` ở cuối;
  - kết nối khai báo `ApplicationIntent=ReadOnly`.

  Xem tạm tính không đổi trạng thái bàn, không đổi phiên, không đổi dòng món và không ghi nhật ký món.
- **Giao diện:**
  - Chi tiết bàn (sơ đồ bàn) có nút **Tạm tính**. Nút chỉ hiện khi bàn đang phục vụ và có phiên thật.
  - Nút mở `/ProvisionalBill/Preview/{mã bàn}`. Màn hình có bảng món (#, Món, Ghi chú, SL, Đơn giá, Thành tiền), nhóm riêng **“Đã huỷ – có tính tiền”**, dòng **Tổng cộng**, và thông báo **“Bàn chưa có món nào.”** khi bàn chưa gọi món.
  - Nút **← Quay lại gọi món** đưa về `/?table={mã}`: sơ đồ bàn tự mở lại chi tiết của đúng bàn đó, nơi có nút **Gọi thêm món**.
- Nút **Xem hoá đơn tạm tính** cũ (S3-04/S3-05, có thao tác huỷ món) vẫn giữ nguyên.

## Demo

1. Chạy web. Mở ca nếu chưa có. Phục vụ mở một bàn và gọi vài món với số lượng khác nhau, có một món có ghi chú.
2. Phục vụ mở **Xem hoá đơn tạm tính** và huỷ một món còn chờ bếp (huỷ trước khi nấu). Bếp bấm **Bắt đầu nấu** một món khác, rồi Quản lý **Hủy có tính tiền** món đó (huỷ sau khi nấu).
3. Trên **Sơ đồ bàn**, bấm vào bàn rồi bấm **Tạm tính**. Màn hình hiện:
   - bảng món với số lượng, đơn giá, thành tiền và ghi chú đúng;
   - món huỷ trước khi nấu **không có**;
   - món huỷ sau khi nấu nằm ở nhóm **Đã huỷ – có tính tiền**;
   - **Tổng cộng** bằng VND, ví dụ `545.000 đ`.
4. Bấm **← Quay lại gọi món**. Chi tiết bàn mở lại. Bấm **Gọi thêm món** và gửi một món.
5. Bấm **Tạm tính** lần nữa: món mới xuất hiện ở cuối và tổng cộng tăng đúng. Bàn vẫn **Đang phục vụ**.

## Kiểm thử

- **Không cần database** (`dotnet run --project tests/RestaurantManagement.AreaTests`, file `BillPreviewTests.cs`):
  - nhiều món với số lượng khác nhau, thành tiền và tổng cộng;
  - ghi chú hiển thị đúng, hai lần gọi cùng món không bị gộp;
  - món huỷ trước khi nấu bị loại;
  - món huỷ sau khi nấu nằm ở nhóm riêng có nhãn và được cộng vào tổng;
  - tổng khớp hoá đơn S3-04;
  - bàn chưa có món;
  - định dạng số tiền lớn (`1.234.567.890 đ`), làm tròn;
  - thứ tự dòng;
  - truy vấn chỉ `SELECT` và luôn rollback;
  - quyền (Quản lý, Phục vụ; Bếp và Thu ngân bị chặn);
  - nút **Tạm tính** và **Quay lại gọi món**.

  Bảng phân quyền (`ApiAccessMatrix`) và kiểm thử phạm vi Phục vụ (`RoleNavigationTests`) đã thêm hai API mới.
- **SQL Server thật + web thật:** `dotnet run --project tools/RestaurantManagement.DbTool -- verify-bill-preview` (database tạm, cần build Debug trước; cũng chạy trong `verify`). Kiểm thử này dựng đúng kịch bản demo, rồi kiểm tra:
  - API và màn hình đúng từng dòng;
  - tổng khớp `vw_SessionTotals` và “Tạm tính hiện tại” trên chi tiết bàn;
  - sau nhiều lần xem, trạng thái bàn, phiên và dòng món **không đổi** (so cả rowversion);
  - Bếp, Thu ngân và khách bị chặn;
  - gọi thêm món sau khi xem vẫn thành công, xem lại thấy món mới và bàn vẫn đang phục vụ;
  - bàn chưa có món thì hiện thông báo trống;
  - bàn không có khách hoặc mã bàn sai thì trả 404 kèm lý do.
