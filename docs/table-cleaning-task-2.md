# Task 2 — Cảnh báo bàn dọn quá 10 phút

PO đã đồng ý: chỉ cảnh báo khi thời gian dọn **lớn hơn 10 phút**. Đúng 10:00.000 chưa cảnh báo; 10:00.001 đủ điều kiện.

- Dùng `DiningTables.StatusChangedAt` theo UTC, được trigger hiện có ghi khi chuyển trạng thái. Chọn lại Cleaning không đặt lại giờ bắt đầu; một lượt Cleaning mới sau khi dọn xong có mốc mới.
- Migration `060_CleaningOverdueWarning.sql` bổ sung hàm kiểm tra biên thời gian. API sử dụng giờ SQL Server, không sử dụng đồng hồ máy nhân viên.
- Hai sơ đồ `/` và `/TableMap` hiển thị khung cảnh báo trên thẻ bàn, tự kiểm tra mỗi 2 giây. Không cần tải lại trang để xuất hiện cảnh báo khi vượt ngưỡng.
- Báo dọn xong xóa cảnh báo; vẫn giữ kiểm tra quyền và chặn phiên phục vụ của Task 1. Nếu mất kết nối, giao diện báo dữ liệu có thể đã cũ và tự thử lại.
- Chưa bổ sung xử lý đặt bàn kế tiếp hoặc cảnh báo khác ngoài phạm vi Task 2.

## Kiểm tra trên web

1. Chạy lại ứng dụng, đăng nhập Phục vụ hoặc Quản lý, mở Sơ đồ bàn ở hai cửa sổ.
2. Chọn một bàn thử không có phiên phục vụ, chuyển sang Đang dọn.
3. Trước và đúng 10 phút chưa có cảnh báo. Sau 10 phút, chờ chu kỳ đồng bộ (tối đa khoảng 5 giây khi kết nối bình thường): thẻ bàn hiện “Cảnh báo: Đang dọn quá 10 phút”.
4. Bấm Báo đã dọn xong: bàn về Trống, cảnh báo và nút biến mất ở cả hai cửa sổ.
5. Chuyển bàn sang Đang dọn lần nữa: bắt đầu tính 10 phút mới, không giữ cảnh báo của lần trước.

## Kiểm thử tự động

Chạy `tests/Verify-Cleaning.ps1` theo hướng dẫn build trong `table-cleaning-task-1.md`. Database tạm được tạo riêng và xóa sau kiểm thử; không sửa giờ hoặc trạng thái bàn thật.

Kiểm tra SQL tại 09:59.999 / 10:00.000 / 10:00.001, trạng thái khác Cleaning; Chrome với ba phiên trên hai sơ đồ, một phiên không dùng được SSE; cảnh báo xuất hiện khi vượt ngưỡng mà không tải lại; máy khách lệch giờ một ngày vẫn đúng; dọn xong xóa cảnh báo; lượt dọn mới không cảnh báo ngay. Các kiểm tra chống gửi lặp, đồng thời, quyền, CSRF và phiên phục vụ của Task 1 tiếp tục được chạy.
