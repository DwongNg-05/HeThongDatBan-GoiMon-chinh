# Gộp "Sửa giá món" vào "Quản lý món"

- Mục **Sửa giá món** trên thanh bên đã được bỏ. Giá món được sửa trực tiếp ở nút **Sửa** của từng món trong **Quản lý món** (`/Dishes/Edit/{id}`). Mỗi lần đổi giá vẫn đi qua `dbo.usp_UpdateMenuPrice`, nên vẫn ghi `MenuPriceHistory`, `AuditLogs` và Nhật ký hệ thống như trước.
- **Quản lý món** (`/Dishes`, giữ nguyên tên) có thêm hai phần, chỉ hiện với Quản lý:
  - cột **Hôm nay** với nút **Báo hết / Mở bán lại** (trước đây nằm ở màn hình Sửa giá món);
  - mục **Nhật ký thay đổi giá**: 20 lần đổi giá gần nhất của mọi món, chỉ xem. Nút **Lịch sử giá** của từng món vẫn giữ.
- Đường dẫn cũ `/Management` chuyển về `/Dishes`. Hành động `POST /Management/Price` đã bị xoá; chỉ còn `POST /Management/Availability` cho nút Báo hết.
- Không có migration mới.

Kiểm thử: `LoginVerification` và `SecurityAuditVerification` giờ đổi giá qua màn hình Sửa món (`DishPriceEdit.cs`), kiểm tra `/Management` chuyển hướng, `/Management/Price` không còn nhận ghi, và nhật ký giá trong Quản lý món hiển thị đúng người sửa. `SecurityAuditAccessVerification` bỏ `/Management` khỏi danh sách trang rà soát.
