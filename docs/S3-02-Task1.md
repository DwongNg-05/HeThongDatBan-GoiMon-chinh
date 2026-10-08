# S3-02 Task 1 — Gọi món theo nhóm và gửi order

## Quy ước đã chốt để triển khai

- Khách đi từ mã QR của bàn. QR mở phiên gọi món của bàn đã được nhân viên mở phục vụ; khách không tự nhập mã bàn.
- Thực đơn giữ thứ tự nhóm đã cấu hình. Món ngừng bán không xuất hiện; món tạm hết trong ngày hiển thị mờ và không thể thêm vào giỏ.
- Một order được lưu vào `OrderBatches`; mỗi món là một `OrderItems` có trạng thái ban đầu `Pending` (chờ bếp).

## Demo

1. Quét QR của bàn đang phục vụ, bấm **Mở thực đơn gọi món**.
2. Thêm một hoặc nhiều món vào giỏ, rồi bấm **Gửi order cho bếp**.
3. Màn hình hiển thị mã `ORD-xxxxxx`; giỏ cũ không còn trong phiên QR.
4. Trong SQL: `OrderBatches` có một bản ghi mới và `OrderItems` của batch đó đều có `Status = 'Pending'`.

Nếu món bị ngừng bán sau khi khách đã thêm vào giỏ, máy chủ từ chối order và giữ giỏ để khách chỉnh lại.
