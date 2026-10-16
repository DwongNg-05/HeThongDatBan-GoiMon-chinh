# Task 3 — Bàn dọn xong và khách đặt kế tiếp

## Quy tắc PO đã đồng ý

Chọn lượt `Confirmed`, đã gán đúng bàn, `ArrivedAt IS NULL`, có giờ hẹn nằm trong `[hiện tại, hiện tại + 30 phút]` theo giờ SQL Server. Hai đầu mút được tính. Nhiều lượt: sắp theo giờ hẹn tăng dần, rồi Id tăng dần. Bỏ qua Pending, Cancelled, Rejected, Arrived, NoShow và lượt quá giờ.

## Hoạt động

- Migration 061 bổ sung `fn_NextTableReservation`, dùng chung trong thao tác báo dọn xong, hai sơ đồ và chi tiết bàn.
- `usp_CompleteTableCleaning` khóa thao tác, bàn và phạm vi đặt bàn trong một giao dịch. Kiểm tra quyền và phiên phục vụ của Task 1 vẫn được giữ. Có lượt phù hợp: ghi trực tiếp `Reserved`; không có: `Available`. Không ghi bước trung gian Available khi đã có khách sắp tới.
- Trên thẻ Đã đặt trước, hiển thị “Khách sắp tới: tên · giờ hẹn” theo giờ Việt Nam. Hai sơ đồ đồng bộ tên ngay cả khi chỉ thông tin khách thay đổi mà trạng thái bàn không đổi.
- Cảnh báo dọn quá 10 phút được gỡ khi bàn không còn Cleaning. Không tự tạo lượt đặt hoặc thay đổi trạng thái của lượt đặt.

## Demo

1. Tạo và xác nhận một lượt hẹn trong 15–20 phút tới, gán đúng bàn thử không có phiên phục vụ. Chuyển bàn sang Đang dọn.
2. Mở Sơ đồ bàn ở hai cửa sổ, bấm Báo đã dọn xong tại một cửa sổ.
3. Cả hai phải hiện Đã đặt trước + đúng tên khách và giờ hẹn, không cần F5; nút báo dọn xong và cảnh báo dọn biến mất.
4. Với bàn không có lượt phù hợp (hoặc lượt đã hủy), báo dọn xong phải về Trống, không hiện tên khách.
5. Nhiều lượt hợp lệ không trùng khung giữ bàn: phải chọn lượt sớm nhất. Quy tắc chống trùng khung và thời gian dọn giữa các lượt của hệ thống vẫn áp dụng.

## Kiểm thử

Build web rồi chạy `tests/Verify-Cleaning.ps1` theo hướng dẫn Task 1. Bộ test dùng database tạm, có fixture `cleaning-next-reservation.sql` và ba phiên Chrome trên `/` và `/TableMap` (một phiên chặn SSE để kiểm tra đồng bộ dự phòng).

Đã kiểm tra mốc đúng hiện tại, đúng +30 phút và ngoài biên 1 ms; chọn lượt sớm nhất; bỏ qua lượt đã hủy/chờ xác nhận/đã tới/quá giờ/ngoài 30 phút; lưu Reserved thật trong database, không qua Available; đồng bộ tên khách khi trạng thái giữ nguyên. Các kiểm thử Task 1–2 vẫn chạy cùng bộ test.

Kiểm thử nhiều phiên trình duyệt trên cùng máy không thay thế kiểm tra đường truyền giữa các máy thật. Khi demo nhiều máy, tất cả cần truy cập cùng ứng dụng và database.
