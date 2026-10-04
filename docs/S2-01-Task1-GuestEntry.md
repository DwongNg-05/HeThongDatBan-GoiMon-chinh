# S2-01 Task 1: lối vào thực đơn cho khách (không cần đăng nhập)

Trang `/Menu` đã mở được khi chưa đăng nhập, nhưng trước đây khách không có đường nào để tới:
chạy web hoặc gõ địa chỉ quán (`/`) luôn bị chuyển sang màn hình đăng nhập, và màn hình đó không có link tới thực đơn.

## Thay đổi

| Lối vào | Hành vi |
| --- | --- |
| Mở địa chỉ gốc `/` khi chưa đăng nhập | Chuyển thẳng tới **Thực đơn** (`/Menu`) |
| Màn hình đăng nhập | Thêm ô “Bạn là khách hàng? … **Xem thực đơn**” |
| Thanh trên cùng khi chưa đăng nhập | Logo “Bếp Nhà” và link **Thực đơn** dẫn tới `/Menu`; link **Đăng nhập** dẫn tới đăng nhập |

Nhân viên không bị ảnh hưởng:
- đã đăng nhập: `/` vẫn là sơ đồ bàn, vẫn qua bước xác minh email / đổi mật khẩu;
- phiên đăng nhập hết hạn (trình duyệt còn cookie cũ): `/` vẫn đưa về màn hình đăng nhập;
- các trang quản lý khác vẫn bắt đăng nhập.

Cách làm: middleware `GuestMenuEntry` (chạy sau `UseAuthentication`). Không gắn `[AllowAnonymous]` cho trang chủ,
vì middleware xác minh email và bắt đổi mật khẩu bỏ qua các trang cho phép khách.

## Kiểm thử

- `GuestMenuEntryTests` (không cần database): khách mở `/` → `/Menu`; nhân viên đã đăng nhập, cookie hết hạn, POST và các trang quản lý không bị chuyển.
- `PublicMenuVerification` (lệnh `verify`): `GET /` khi chưa đăng nhập trả 302 tới `/Menu`; màn hình đăng nhập có link thực đơn; thanh trên cùng của khách không dẫn về trang nhân viên.

## Demo trên điện thoại

1. Chạy web với `--urls http://0.0.0.0:5105`, điện thoại và máy tính cùng mạng Wi-Fi.
2. Trên điện thoại mở `http://<IP máy tính>:5105` → thấy ngay thực đơn theo nhóm món, có ảnh, tên, mô tả ngắn và giá VND.
