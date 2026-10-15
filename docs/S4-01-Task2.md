# S4-01 Task 2: phần đầu hoá đơn tạm tính

Màn hình **Tạm tính** (Task 1) có thêm phần đầu nằm phía trên bảng món, để khách và quầy thu ngân nhận ra đây là hoá đơn nào. Phần đầu gồm:

- dòng chữ **TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN**
- tên quán
- mã bàn
- thời điểm bắt đầu phục vụ
- thời điểm in
- tên người in

**Hoàn tất:** AC 2. **Chưa làm:** in ra giấy, xuất PDF.

## Quy tắc cần PO chốt (đề xuất, đã cài đặt)

Các quy tắc nằm cùng một chỗ, `BillHeaderRules` trong `Models/Orders/TableBillHeader.cs`.

| Câu hỏi | Đề xuất (đã cài đặt) | Lý do / cách đổi |
| --- | --- | --- |
| “Thời điểm bắt đầu phục vụ” tính từ lúc nào? | **Lúc mở bàn**, tức lúc phiên phục vụ được mở. Với nhóm bàn đã gộp, lấy phiên mở **sớm nhất** trong nhóm. | Khách bắt đầu được phục vụ từ lúc ngồi vào bàn. Thời gian này cũng khớp với “Bắt đầu phục vụ” và “Đã phục vụ N phút” trên chi tiết bàn. Nếu PO chọn **lúc gọi món đầu tiên**, đặt `ServiceStartsAt = ServiceStartDefinition.FirstOrder`. Dữ liệu đã có sẵn (`FirstOrderAtUtc`, tính cả món sau đó bị huỷ); bàn chưa gọi món thì hiện “Chưa gọi món”. |
| Tên quán lấy từ đâu? | Từ **cấu hình chung** `dbo.RestaurantSettings.Name`. | Hệ thống hiện chỉ có một quán: bảng này chỉ có một dòng (`Id = 1`) và chưa có khái niệm chi nhánh. Nếu sau này có nhiều chi nhánh, cần thêm bảng chi nhánh và gắn bàn với chi nhánh. Đó là một task riêng. Nếu tên quán trong cấu hình trống, màn hình hiện “Nhà hàng”. **Lưu ý:** dữ liệu mẫu đang ghi `Nhà hàng Demo`. Muốn hiện “Bếp Nhà” thì chạy: `UPDATE dbo.RestaurantSettings SET Name=N'Bếp Nhà' WHERE Id=1;` |
| Có dòng “TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN” không? | **Có**, nằm ở đầu phần đầu hoá đơn. | Tránh để khách nhầm tạm tính là hoá đơn đã thanh toán, nhất là khi tạm tính được in ra ở task sau. Muốn tắt, đặt `ShowProvisionalNotice = false`. |
| Định dạng ngày giờ | `dd/MM/yyyy HH:mm` theo giờ Việt Nam, ví dụ `15/10/2026 19:05`. | Theo mô tả task. Định dạng không phụ thuộc cấu hình ngôn ngữ của máy chủ. |

## Cách hoạt động

- **Dữ liệu tạm tính** (`TableBillPreviewStore`, vẫn chỉ đọc) có thêm ba trường:
  - **tên quán**, đọc từ `RestaurantSettings`;
  - **lúc mở bàn**, là `MIN(OpenedAt)` của các phiên trong nhóm thanh toán;
  - **lúc gọi món đầu tiên**.
- **Mã bàn** là các bàn **đang giữ** (`SessionTables.ReleasedAt IS NULL`) của cả nhóm thanh toán. Nhờ vậy:
  - Bàn được **chuyển** thì phần đầu hiện mã bàn mới, và bàn cũ không còn tạm tính.
  - Bàn được **gộp** thì hiện mọi mã bàn của nhóm, ví dụ `A03, A04`, dù mở từ bàn nào trong nhóm.
- **Thời điểm in** và **người in** được ghi nhận ở **mỗi lần mở** tạm tính, tức là ở chính yêu cầu đó:
  - Thời điểm in là giờ máy chủ lúc mở.
  - Người in là họ tên nhân viên đang đăng nhập (lấy từ claim `FullName` lúc đăng nhập; nếu không có thì dùng tên đăng nhập).

  Trang không lưu cache (`no-store`), nên mở lại là giờ in cập nhật, và nhân viên khác mở thì tên đổi theo. Hai giá trị này **không ghi vào database**, vì tạm tính vẫn chỉ đọc như Task 1. Khi làm chức năng in, nên lưu lại lần in (ai in, lúc nào) ở task in.
- **API** `GET /api/tables/{mã bàn}/provisional-bill` có thêm `header`, gồm các trường: `restaurantName`, `tables`, `serviceStartedAtUtc`, `serviceStartedAtText`, `serviceStartDefinition`, `printedAtUtc`, `printedAtText`, `printedBy`, `notice`.
- **Màn hình:** phần đầu nằm trên bảng món, kể cả khi bàn chưa gọi món. Các ô thời gian có thêm thuộc tính `data-preview-printed-at` và `data-preview-service-start`, chứa giờ UTC đầy đủ, để kiểm thử so sánh chính xác.

## Demo

1. Đặt tên quán: `UPDATE dbo.RestaurantSettings SET Name=N'Bếp Nhà' WHERE Id=1;`
2. Đăng nhập **nhân viên A** (Phục vụ). Mở một bàn đang phục vụ trên **Sơ đồ bàn** rồi bấm **Tạm tính**. Phía trên bảng món hiện:
   - dòng **TẠM TÍNH – KHÔNG PHẢI HOÁ ĐƠN THANH TOÁN**;
   - tên quán **Bếp Nhà**;
   - mã bàn;
   - **Bắt đầu phục vụ** (giờ mở bàn);
   - **Thời điểm in** (bây giờ);
   - **Người in**: tên của A.
3. Đăng xuất. Đăng nhập **nhân viên B** (Quản lý hoặc Phục vụ khác) và mở **Tạm tính** của cùng bàn. **Người in** đổi thành tên của B, **Thời điểm in** cập nhật, các thông tin khác giữ nguyên.

## Kiểm thử

- **Không cần database** (`BillHeaderTests.cs`):
  - tên quán theo cấu hình, có tên dự phòng khi cấu hình trống;
  - mã bàn, kể cả nhóm bàn đã gộp;
  - bắt đầu phục vụ theo lúc mở bàn, và có sẵn lúc gọi món đầu tiên cho định nghĩa còn lại;
  - thời điểm in đổi theo mỗi lần mở;
  - định dạng `dd/MM/yyyy HH:mm`, không phụ thuộc cấu hình ngôn ngữ của máy chủ;
  - người in theo nhân viên đang đăng nhập (đổi nhân viên thì đổi tên);
  - dòng chữ tạm tính;
  - phần đầu có trong JSON;
  - phần đầu nằm trên bảng món.
- **SQL Server thật + web thật:** `dotnet run --project tools/RestaurantManagement.DbTool -- verify-bill-header` (chạy cùng bộ `verify-bill-preview`; database tạm, cần build Debug trước). Kiểm thử này:
  1. đặt tên quán trong cấu hình;
  2. Phục vụ mở tạm tính, kiểm tra tên quán, mã bàn, giờ mở bàn (khác giờ gọi món đầu), giờ in, tên Phục vụ;
  3. mở lại, kiểm tra giờ in tăng;
  4. Quản lý mở, kiểm tra tên Quản lý và giờ in tăng;
  5. kiểm tra API;
  6. **chuyển** phiên sang bàn khác: phần đầu hiện mã bàn mới, bàn cũ trả 404;
  7. **gộp** với một bàn cùng khu vực: mở từ bàn nào cũng hiện đủ hai mã bàn.

  Hiện hệ thống **chưa có chức năng chuyển bàn**, nên kiểm thử mô phỏng việc chuyển trực tiếp trong database: nhả bàn cũ, giữ bàn mới. Đây cũng là cách một chức năng chuyển bàn sau này sẽ ghi dữ liệu.
