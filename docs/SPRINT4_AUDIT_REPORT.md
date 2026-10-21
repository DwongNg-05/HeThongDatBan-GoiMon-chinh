# Sprint 4 — báo cáo kiểm tra và hoàn thiện

Ngày kiểm tra: 20-10-2026. Phạm vi là S4-01 đến S4-10 trên nhánh hiện tại. Không chạy `reset-demo` trên database demo đang dùng, không xóa dữ liệu thật, không commit/push/triển khai.

## Kết quả trước khi sửa

| Mã | Chức năng | Trạng thái trước sửa | Tiêu chí/chứng cứ còn thiếu | Lỗi quyền | Vị trí chính |
|---|---|---|---|---|---|
| S4-01 | In/xuất tạm tính | Một phần | Bàn dữ liệu demo đang phục vụ bị ẩn lối vào xem/in; test giao diện còn theo nhãn cũ. | Không phát hiện từ nguồn. | `ProvisionalBillController`, `TableBillPreviewStore`, `Views/ProvisionalBill` |
| S4-02 | Thanh toán, đóng bàn | Chưa xác minh | Có code/procedure nhưng chưa chạy lại luồng SQL tích hợp trong lượt audit. | Chưa xác minh HTTP. | `CashierController`, `usp_Checkout` |
| S4-03 | Báo bàn đã dọn | Chưa xác minh | Cần chạy luồng trạng thái và đồng thời. | Chưa xác minh HTTP. | `TableCleaningController`, migration 059–061 |
| S4-04 | Chốt ca | Chưa xác minh | Cần kiểm thử chênh lệch, khóa ca và mở lại. | Chưa xác minh HTTP. | `CashierController`, `ShiftClosingController` |
| S4-05 | Báo cáo doanh thu | Chưa xác minh | Cần chạy quy tắc khoảng ngày/giờ và quyền. | Chưa xác minh HTTP. | `Reports/DailyOverviewController` |
| S4-06 | Báo cáo món | Chưa xác minh | Cần kiểm thử CSV, số liệu hủy và quyền. | Chưa xác minh HTTP. | `Reports/MenuPerformanceController` |
| S4-07 | Gộp/hủy gộp bàn | Chưa xác minh | Cần chạy transaction gộp/hủy gộp thật. | Chưa xác minh HTTP. | migrations 059–061, `Ordering/Unmerge` |
| S4-08 | Email nhắc đặt bàn | Một phần | Logic có sẵn; chưa có bằng chứng gửi qua SMTP/staging thực trong lượt audit. | Chưa xác minh worker thật. | `BookingReminderWorker`, `BookingEmailDispatcher` |
| S4-09 | Ma trận quyền | Một phần | Ma trận thiếu `About.Index` và `Cashier.InvoiceDetails`; test phản chiếu phát hiện. | Có thiếu khai báo matrix, không phải endpoint mở quyền. | `ApiAccessMatrix`, `ApiAuthorizationCoverageTests` |
| S4-10 | Staging và demo | Một phần | Seed/reset/kịch bản có sẵn; chưa có staging Internet được cung cấp để kiểm tra. | Không áp dụng. | `DbTool`, `S4-10-Task3-acceptance.md` |

## Sửa trong lượt này

| Thay đổi | Lý do | Kết quả |
|---|---|---|
| Bổ sung `About.Index GET` và `Cashier.InvoiceDetails GET` vào `ApiAccessMatrix`. | Ma trận phải liệt kê đủ mọi MVC action để kiểm thử quyền không có lỗ hổng do endpoint mới. | Kiểm thử 401/403 theo ma trận không còn thiếu route. |
| Thu hẹp `AuditReadOnlyTests` vào các lớp Audit/PriceHistory. | `EmailBranding.RemoveLogo` bị regex hiểu sai là xóa log. | Giữ kiểm tra append-only của log, loại false-positive không liên quan. |
| Đồng bộ test giỏ QR từ 99 về 20 món/dòng. | Backend và JavaScript đều quy định `MaximumQuantity = 20`; test cũ sai kỳ vọng. | Test kiểm tra đúng cùng giới hạn client/server. |
| Đồng bộ test S4-01 với nút `Xem / in tạm tính`, cho phép bàn demo đang phục vụ xem/in. | Lối vào mới thay nhãn cũ, thao tác chỉ đọc và không đổi trạng thái bàn. | Bàn demo có thể demo in/PDF. |
| Sửa URL clone và trạng thái tính năng trong README. | README cũ sai repository và nói chưa có SMTP/PDF dù hệ thống đã có. | Hướng dẫn không mâu thuẫn nguồn hiện tại. |

## Kết quả sau sửa

| Mã | Trạng thái sau sửa | Tiêu chí đã xác minh | Test/bằng chứng | Quyền đã xác minh | Ghi chú |
|---|---|---|---|---|---|
| S4-01 | Đạt | Dòng món, ghi chú, VND, hủy miễn phí/hủy tính tiền, đầu hóa đơn, in 80 mm, PDF, chỉ đọc và gọi thêm món sau khi in. | 991 test .NET; 69 test Node; `verify` SQL. | Manager/Waiter xem-preview/PDF; Kitchen/Cashier bị chặn. | Kiểm tra layout Playwright chưa chạy, xem phần tồn tại. |
| S4-02 | Đạt | Tổng tiền server-owned, giảm giá tối đa 50%, tiền thối, reference chuyển khoản, idempotency 10 request đồng thời, hóa đơn/phiên/bàn cùng transaction, void theo Manager. | `verify` SQL chạy thành công, bao gồm checkout đồng thời và void. | Cashier/Manager thanh toán; chỉ Manager void. | Không tự xác nhận giao dịch ngân hàng; chỉ lưu reference. |
| S4-03 | Đạt | Cleaning chỉ hoàn tất đúng trạng thái, cảnh báo quá 10 phút, ưu tiên reservation gần, trạng thái bàn có event/polling. | 991 test .NET và `verify` SQL. | Manager/Waiter. | Cập nhật đa máy là polling/SSE, không phải SignalR. |
| S4-04 | Đạt | Tổng hợp ca, đối soát tiền mặt, bắt buộc giải trình lệch lớn, chặn ca còn phiên, khóa ca, Manager mở lại, PDF. | 991 test .NET và `verify` SQL. | Cashier/Manager chốt; Manager mở lại. | Múi giờ dùng Việt Nam trong model/procedure. |
| S4-05 | Đạt | Chỉ số ngày, biểu đồ giờ, khoảng tối đa 92 ngày, ngày trống, loại hóa đơn void và kiểm quyền. | 991 test .NET. | Chỉ Manager. | Snapshot tự động không gia hạn phiên đăng nhập. |
| S4-06 | Đạt | Top 10 số lượng/doanh thu, món hủy, lý do phổ biến, CSV UTF-8 và món ngừng bán lịch sử. | 991 test .NET. | Chỉ Manager. | Khoảng ngày dùng chung cho các bảng. |
| S4-07 | Đạt | Cùng khu vực, không chờ thanh toán, giữ bàn gốc, nhãn nhóm, hủy gộp và transaction. | `verify` SQL gọi gộp/hủy gộp thật. | Manager/Waiter theo phạm vi FrontOfHouse. | Demo data có cặp S4D01/S4D02 để trình diễn. |
| S4-08 | Một phần | Quy tắc 2 giờ, skip Cancelled/Arrived, một reminder/lượt, retry, link hủy >60 phút, chống worker trùng. | 991 test .NET và fake SMTP/SQL trong `verify`. | Customer dùng link công khai có token; Manager chạy thủ công; Waiter xem trạng thái. | Chưa kiểm chứng delivery với SMTP/staging thực trong lượt này. |
| S4-09 | Đạt | Ma trận role/API, reflection toàn bộ action, 401/403 và quyền backend. | 991 test .NET; `verify` chạy HTTP theo vai trò. | Xem [ma trận chi tiết](SPRINT4_PERMISSION_MATRIX.md). | Endpoint mới đã được thêm vào matrix. |
| S4-10 | Một phần | 4 vai trò, 3 khu vực/25 bàn/5 nhóm/60 món/20 đặt bàn, reset idempotent <2 phút và rehearsal. | `verify` tạo database riêng; reset lần đo được 1.30 giây và 1.06 giây. | Theo role demo. | Chưa có URL staging Internet được cấp để kiểm tra. |

## Lệnh đã chạy

| Lệnh | Kết quả thực tế |
|---|---|
| `dotnet build tests/RestaurantManagement.AreaTests/RestaurantManagement.AreaTests.csproj --no-restore -p:TreatWarningsAsErrors=false -p:NoWarn=NU1902;NU1903` | Thành công, 1 cảnh báo license ImageSharp. |
| `tests/RestaurantManagement.AreaTests/bin/Debug/net10.0/RestaurantManagement.AreaTests.exe` | **991 tests passed**. |
| `node --test tools/tests/*.test.cjs` | **69 pass, 0 fail**. |
| `dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify` (kết nối SQL Server cục bộ) | Thành công; tạo/migrate/seed/xác minh/xóa database `RestaurantManagement_Test_<guid>` riêng. |

## Kiểm tra bị chặn hoặc chưa xác minh

1. `tools/tests/bill-print-layout.check.cjs` và `tools/tests/menu-mobile.check.cjs` cần `playwright`/`playwright-core`; dependency này chưa được cài trong repository. Hai kiểm tra browser-layout vì vậy **chưa chạy**, không được suy diễn là pass.
2. ImageSharp 4.1.2 báo chưa có license. Đây là điểm tuân thủ dependency cần chủ dự án xử lý, không phải kết quả lỗi nghiệp vụ Sprint 4.
3. Chưa có hạ tầng/staging Internet và SMTP production do người dùng cấp; do đó không xác nhận được URL công khai hoặc email thực được nhà cung cấp nhận. Logic SMTP được kiểm tra bằng fake SMTP; không đưa mật khẩu/token vào tài liệu.
4. `reset-demo` có chủ đích từ chối khi database có phiên phục vụ/hóa đơn. Đây là cơ chế bảo vệ dữ liệu kế toán; demo lặp lại phải dùng database rehearsal riêng hoặc reset trước khi có giao dịch.

## Kết luận an toàn

Không phát hiện endpoint S4 mới nào không có khai báo quyền sau khi đã cập nhật ma trận. Việc UI ẩn nút không được dùng làm cơ chế bảo vệ: controller/page đều kiểm tra `[Authorize]` và các luồng tiền/trạng thái tiếp tục kiểm tra role, version/idempotency và transaction tại backend/database. Mọi kết luận “Đạt” ở trên đều có ít nhất kiểm thử source hoặc SQL/HTTP tương ứng; các phần phụ thuộc hạ tầng ngoài được đánh dấu “Một phần”.
