# S3-05 Task 2: Quản lý xem lại các lần huỷ món trong báo cáo cuối ca

Quản lý mở **Báo cáo huỷ món** của một ca và xem mọi lần huỷ món: huỷ thành công và yêu cầu huỷ bị từ chối. Mỗi lần huỷ có đơn, dòng món, người thực hiện, thời điểm, lý do và cách tính tiền.

**Hoàn tất:** AC 4. AC 1, AC 2, AC 3 (S3-05 Task 1) giữ nguyên: thao tác huỷ, tạm tính, màn hình bếp và chống gửi lặp không đổi; bộ kiểm thử `verify-cancel-item` vẫn chạy.

**Chưa làm trong task này:** thao tác mới cho Quản lý huỷ món đã bắt đầu chế biến. Thao tác cũ (món vẫn tính tiền) vẫn chạy như trước và chỉ được *hiển thị* trong báo cáo.

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Một lần huỷ thuộc ca nào? | **Ca của phiên phục vụ** chứa dòng món (`DiningSessions.ShiftId`, gán khi mở bàn). Hệ thống không cho chốt ca khi còn bàn chưa thanh toán (lỗi 51050), nên mọi lần huỷ của một bàn đều xảy ra trong lúc chính ca đó đang mở. Không dùng khung giờ, nên không lệch khi ca kéo dài qua nửa đêm. |
| Yêu cầu bị từ chối không có dòng món hợp lệ thuộc ca nào? | Ca của dòng món nếu dòng tồn tại, nếu không thì ca của hoá đơn đang mở trên màn hình, nếu vẫn không có thì ca đang mở. |
| "Mọi lần huỷ" gồm những gì? | **Huỷ thành công** (mỗi dòng đúng một mục) **và yêu cầu bị từ chối hoặc thất bại**: thiếu hoặc sai lý do, lý do chỉ dành cho Quản lý, trạng thái giả, bếp đã bắt đầu, bếp bắt đầu trong lúc chọn lý do, dòng của hoá đơn khác, hoá đơn đã đóng, hệ thống bận. |
| Bấm xác nhận lặp cho dòng đã huỷ | **Không ghi thêm.** Đây là cùng một lần huỷ, đã báo "đã được hủy trước đó", và hoá đơn không đổi. |
| Ai xem được? | **Chỉ Quản lý.** Web chặn bằng `[Authorize(Roles = Manager)]` cho cả trang lẫn API dữ liệu. Database kiểm tra quyền `Audit.Read` (chỉ Quản lý có) trong `usp_ShiftCancellationReport`. |
| Cách tính tiền hiển thị | **Không tính tiền:** huỷ khi món còn chờ bếp, đã trừ khỏi hoá đơn. **Vẫn tính tiền:** Quản lý huỷ sau khi bếp đã bắt đầu. **Không đổi:** yêu cầu bị từ chối. Đầu báo cáo có tổng của hai loại tiền. |
| Sửa hoặc xoá nhật ký | Không được. Huỷ thành công nằm trong `OrderItemEvents`, vốn chỉ ghi thêm. Bảng mới `OrderCancelRejections` có trigger chặn sửa hoặc xoá (51140). |

## Cách hoạt động

- **Migration `057_S305ShiftCancellationReport.sql`**:
  - Bảng `dbo.OrderCancelRejections` lưu ca, hoá đơn, dòng món, trạng thái dòng lúc bị từ chối, người gửi, lý do đã gửi, mã lỗi, thông báo và thời điểm. Bảng chỉ cho ghi thêm.
  - `dbo.usp_LogOrderCancelRejection` cần quyền `Orders.Manage` và tự xác định ca.
  - `dbo.usp_ShiftCancellationReport` cần quyền `Audit.Read`; ca không tồn tại trả 51141. Thủ tục trả về thông tin ca và các mục huỷ (thành công và bị từ chối) theo thứ tự thời gian.
- **Ghi nhận yêu cầu bị từ chối** (`ProvisionalBillController.CancelItem`):
  - Mọi lần từ chối đều được ghi, dù web chặn trước (51032 thiếu/sai lý do, 51036 lý do của Quản lý, 51037 yêu cầu không hợp lệ) hay database từ chối (51030–51035, 51001…).
  - Nếu không ghi được nhật ký (ví dụ chưa chạy migrate), nhân viên vẫn nhận đúng thông báo từ chối.
  - Huỷ thành công đã có nhật ký sẵn. Lần xác nhận lặp không ghi gì.
- **Trang `/Reports/Shift/Cancellations`** (menu **Báo cáo huỷ món**, và liên kết **Xem báo cáo huỷ món của ca** trên màn hình **Chốt ca** của Quản lý):
  - Chọn ca trong 30 ca gần nhất. Mặc định là ca đang mở.
  - Phần tổng gồm số lần huỷ thành công, số yêu cầu bị từ chối, tiền đã trừ (huỷ không tính tiền) và tiền món huỷ vẫn tính.
  - Bảng có các cột:
    - **Thời điểm** (giờ Việt Nam);
    - **Kết quả** ("Đã hủy" hoặc "Bị từ chối", kèm lý do từ chối);
    - **Đơn** (hoá đơn, bàn, đợt gọi);
    - **Dòng món** (số lượng, đơn vị, tên món);
    - **Người thực hiện** (tên và vai trò);
    - **Lý do** (kèm ghi chú);
    - **Tính tiền**.
  - Thông báo:
    - ca không có lần huỷ nào: "Ca này không có lần huỷ món nào.";
    - chưa có ca nào: "Chưa có ca nào.";
    - ca không tồn tại: "Không tìm thấy ca này.";
    - lỗi database: "Không tải được báo cáo huỷ món…" kèm nút **Tải lại**. Trang không hiển thị như một ca trống.
- **API `GET /api/reports/shift-cancellations?shiftId=`** trả cùng dữ liệu dạng JSON: `200` khi thành công, `404` khi ca không tồn tại, `403` khi database từ chối quyền, `503` khi không tải được. Phục vụ, Bếp và Thu ngân bị chặn; khách chưa đăng nhập phải đăng nhập.

## Demo

1. Chạy `migrate` (áp dụng 057), rồi chạy web.
2. Mở ca nếu chưa có (Quản lý hoặc Thu ngân, màn hình **Chốt ca**).
3. Phục vụ mở bàn, gọi vài món, rồi trên **Hoá đơn tạm tính**:
   - huỷ một món đang chờ bếp;
   - gửi một yêu cầu huỷ món bếp đã bắt đầu (bị từ chối).
4. Quản lý huỷ món bếp đã bắt đầu (lý do "Quản lý yêu cầu"; món vẫn tính tiền).
5. Quản lý mở **Báo cáo huỷ món**. Các lần huỷ hiện với người thực hiện, thời điểm, lý do và cách tính tiền. Chọn một ca khác không có lần huỷ nào để thấy thông báo ca trống.
6. Đăng nhập Phục vụ hoặc Thu ngân: không có mục menu, mở thẳng `/Reports/Shift/Cancellations` hoặc `/api/reports/shift-cancellations` đều bị chặn.

## Kiểm thử

- **`tests/RestaurantManagement.AreaTests` → `ShiftCancellationReportTests`** (không cần database):
  - nhãn đơn, dòng, người, thời điểm, lý do;
  - huỷ không tính tiền, vẫn tính tiền và bị từ chối;
  - tổng tiền và ca trống;
  - chỉ Quản lý (menu, trang, API);
  - thông báo ca trống và lỗi tải;
  - controller ghi mọi lần từ chối;
  - quy tắc trong migration 057.
- **`dotnet run --project tools/RestaurantManagement.DbTool -- verify-cancel-report`** (SQL Server thật và web thật, database tạm; cần build Debug trước). Lệnh `verify` cũng chạy bộ này. Bộ kiểm thử xác nhận:
  - **Đúng ca:** số mục báo cáo khớp database; ca khác không chứa mục nào của ca này.
  - **Đủ thông tin:** hoá đơn, bàn, đợt, dòng, người và vai trò, thời điểm, lý do và ghi chú, cách tính tiền, xem trên cả trang lẫn JSON.
  - **Không trùng:** bấm xác nhận lặp hai lần (Phục vụ và Quản lý) chỉ để lại một mục, không có mục bị từ chối.
  - **Loại sự kiện:** huỷ không tính tiền; huỷ vẫn tính tiền; từ chối 51035, 51032, 51036, 51037, 51033. Mỗi mục được ghi đúng một lần, đúng ca, đúng người.
  - **Ca không có lần huỷ, ca không tồn tại, lỗi tải:** thủ tục tạm thời không dùng được thì trang hiện lỗi và API trả 503.
  - **Phân quyền:** Phục vụ, Bếp, Thu ngân và khách bị chặn cả trang lẫn API. Gọi thẳng `usp_ShiftCancellationReport` bằng tài khoản Phục vụ trả 51001. Bếp không ghi được nhật ký. Không xoá được nhật ký.
- `verify-cancel-item` (Task 1) và `verify-api-permissions` vẫn phải đạt. Mục menu mới đã có trong `ApiAccessMatrix` và danh sách menu mong đợi của Quản lý.
