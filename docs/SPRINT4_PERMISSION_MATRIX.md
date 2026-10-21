# Sprint 4 — ma trận phân quyền

Nguồn thực thi: `src/RestaurantManagement.Web/Security/AppRoles.cs`, các `[Authorize]` tại controller/page, `tools/RestaurantManagement.DbTool/ApiAccessMatrix.cs` và các stored procedure. Màn hình chỉ hỗ trợ thao tác; quyết định quyền luôn ở backend/database.

| Module Sprint 4 | Phục vụ | Thu ngân | Quản lý | Bếp | Khách |
|---|---:|---:|---:|---:|---:|
| S4-01 xem/in/PDF tạm tính | Có | Không | Có | Không | Không |
| S4-02 thanh toán, giảm giá, receipt, lịch sử hóa đơn | Không | Có | Có | Không | Không |
| S4-02 hủy hóa đơn đã chốt | Không | Không | Có | Không | Không |
| S4-03 báo đã dọn/cảnh báo dọn | Có | Không | Có | Không | Không |
| S4-04 chốt ca/PDF chốt ca | Không | Có | Có | Không | Không |
| S4-04 mở lại ca | Không | Không | Có | Không | Không |
| S4-05 báo cáo doanh thu | Không | Không | Có | Không | Không |
| S4-06 báo cáo món/CSV | Không | Không | Có | Không | Không |
| S4-07 gộp/hủy gộp bàn | Có | Không | Có | Không | Không |
| S4-08 xem trạng thái reminder | Có | Không | Có | Không | Không |
| S4-08 chạy reminder thủ công | Không | Không | Có | Không | Không |
| S4-08 tự đặt/tra cứu/hủy link an toàn | Không bắt buộc đăng nhập | Không bắt buộc đăng nhập | Không bắt buộc đăng nhập | Không bắt buộc đăng nhập | Có |
| S4-09 xem ma trận quyền | Không | Không | Có | Không | Không |

## Quy tắc bắt buộc được xác minh

- Chưa đăng nhập: endpoint private trả 401/chuyển login theo cơ chế cookie; API permission test kiểm tra chính sách thật.
- Đã đăng nhập sai vai trò: backend trả 403; không chỉ ẩn menu.
- Tổng tiền, đơn giá, giảm giá, tiền thối, invoice number và trạng thái bàn do SQL/backend tính; client không được gửi giá trị quyết định.
- Thanh toán nhận `RequestId` và `RowVersion`; stored procedure dùng transaction để chống checkout trùng.
- Hủy hóa đơn và mở lại ca là quyền Manager riêng; record cũ được giữ, không xóa lịch sử.
- Reminder public chỉ dùng link/token đúng lượt đặt; token sai, link từ máy chủ khác, Cancelled/Arrived hoặc còn <=60 phút bị từ chối.
- `ApiAuthorizationCoverageTests` phản chiếu toàn bộ MVC action/Razor Page và so sánh với `ApiAccessMatrix`; hiện kiểm tra **991 pass**.

## Lưu ý chính sách

`Manager` được chính sách hiện tại cho phép các màn hình giám sát/thu ngân (hằng `AppRoles.Cashiers` bao gồm Manager). Đây là quyền hiện có của dự án, không phải quyền được thêm trong lượt audit. Các quyền nhạy cảm hơn vẫn tách riêng: void invoice và reopen shift chỉ Manager; Kitchen không thanh toán; Waiter không được sửa giá/tổng/paid state; Cashier không void invoice.
