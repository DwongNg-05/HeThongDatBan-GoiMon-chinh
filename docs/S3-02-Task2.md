# S3-02 Task 2 — Số lượng và tạm tính

- Mỗi dòng món từ 1 đến 20 phần; giảm từ 1 về 0 thì xóa dòng khỏi giỏ.
- Giá hiển thị theo VND với dấu chấm hàng nghìn và ký hiệu `₫`; tạm tính chưa gồm thuế hoặc phí phục vụ.
- Trình duyệt cập nhật đơn giá, thành tiền và tạm tính ngay sau mỗi lần tăng/giảm.
- Máy chủ kiểm tra lại dữ liệu; trigger `tr_OrderItems_QuantityMaximum` cũng từ chối ghi thẳng số lượng ngoài 1–20 vào cơ sở dữ liệu.
