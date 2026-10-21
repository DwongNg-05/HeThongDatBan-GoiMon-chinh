# Sprint 4 — hướng dẫn demo

Tài liệu này dùng cho buổi demo cục bộ. Không ghi mật khẩu, token SMTP hoặc chuỗi kết nối thật vào file.

## 1. Chuẩn bị

1. Cài .NET SDK theo `global.json`, SQL Server/SQL Express và Node.js (chỉ cần Node khi chạy test JavaScript).
2. Trong PowerShell tại thư mục repository, đặt biến môi trường `RM_CONNECTION_STRING` trỏ tới database demo có hậu tố `_Dev`, `_Demo`, `_Staging` hoặc `_Test_`.
3. Với database rehearsal mới, migrate và seed bằng lệnh trong README. Với database demo đang có dữ liệu nghiệp vụ, **không chạy `reset-demo`** nếu đã có phiên/hóa đơn; lệnh sẽ bảo vệ và từ chối.
4. Khởi động web; mở URL localhost mà console in ra. Với mỗi vai trò dùng tài khoản mẫu `manager`, `waiter`, `kitchen`, `cashier`; mật khẩu là giá trị do người vận hành đã đặt trong `RM_DEMO_PASSWORD`, không phải giá trị hard-code trong tài liệu.
5. Nếu demo email thật, cấu hình Email/SMTP qua user-secrets hoặc biến môi trường. Nếu không có SMTP, email được lưu ở pickup directory; vẫn demo được nội dung, retry và trạng thái nhưng không chứng minh delivery bên ngoài.

## 2. Dữ liệu chuẩn bị

`seed-demo` tạo baseline: 3 khu vực, 25 bàn, 5 nhóm món, 60 món, 20 lượt đặt bàn trong 7 ngày và 4 tài khoản nội bộ. Dữ liệu S4 bổ sung có bàn/phiên S4D01 và S4D02 để trình bày gộp bàn; chỉ dùng chúng khi còn trạng thái đang phục vụ và chưa chờ thanh toán.

## 3. Kịch bản theo chức năng

| Mã | Vai trò/thao tác | Kết quả cần thấy |
|---|---|---|
| S4-01 | Đăng nhập `waiter` → Sơ đồ bàn → chọn bàn đang phục vụ → **Xem / in tạm tính** → In hoặc Xuất PDF. | Từng món, ghi chú, VND, tổng, đầu hóa đơn và nhãn món hủy có tính tiền. Quay lại gọi thêm món vẫn được; bàn không bị đóng. |
| S4-02 | `cashier` → Thanh toán → chọn phiên đã phục vụ → thử giảm giá >50%, rồi giảm hợp lệ có lý do → tiền mặt/chuyển khoản → chốt. | >50% bị từ chối; tiền thối chính xác; reference chuyển khoản bắt buộc; invoice duy nhất, bàn thành Đang dọn. Vào Hoá đơn → Xem chi tiết. |
| S4-03 | `waiter` → Sơ đồ bàn → chọn bàn Đang dọn → Báo đã dọn. | Nếu có reservation phù hợp trong 30 phút, bàn thành Đã đặt trước và thấy khách; nếu không, bàn Trống. Mở tab khác thấy cập nhật trong tối đa 5 giây. |
| S4-04 | `cashier` → Chốt ca. Thử chốt khi còn bàn phục vụ, sau đó hoàn tất thanh toán/dọn và nhập tiền kiểm đếm lệch >50.000. | Bị chặn kèm danh sách còn vướng; lệch lớn bắt buộc giải trình; sau chốt tải PDF. `manager` mới thấy Mở lại ca. |
| S4-05 | `manager` → Báo cáo doanh thu. Thử ngày trống, khoảng ngày hợp lệ, rồi >92 ngày. | Chỉ số, biểu đồ giờ, trạng thái không dữ liệu; >92 ngày bị từ chối. |
| S4-06 | `manager` → Báo cáo món. Chọn cùng khoảng ngày và xuất CSV. | Top số lượng/doanh thu, hủy nhiều/lý do, CSV UTF-8 mở đúng tiếng Việt. |
| S4-07 | `waiter` → mở hai bàn cùng khu vực đang phục vụ (S4D01/S4D02 nếu baseline còn phù hợp) → Gộp bàn → xem sơ đồ/hoá đơn → Hủy gộp trước thanh toán. | Nhãn nhóm hiển thị; món giữ bàn gốc; hủy gộp trả dòng món về bàn ban đầu. Khác khu vực/chờ thanh toán bị từ chối. |
| S4-08 | `manager` xác nhận một đặt bàn đã có email, đặt giờ hẹn đúng sau thời điểm hiện tại 2 giờ; vào danh sách Reminder và chạy tác vụ khi đến ngưỡng. | Một email reminder/lượt, có mã/giờ/bàn; đã hủy/đã tới không nhận; link hủy chỉ có khi còn >60 phút. Đây khác với email xác nhận gửi ngay khi nhân viên xác nhận. |
| S4-09 | Đăng xuất, mở URL private; đăng nhập `waiter` thử URL báo cáo/void; đăng nhập `cashier` thử void; `manager` mở Ma trận phân quyền. | Guest bị 401/login; sai vai trò bị 403; Manager xem matrix; quyền nhạy cảm không chỉ bị ẩn menu. |
| S4-10 | Dùng database rehearsal riêng → chạy `seed-demo`, làm một luồng đặt → xác nhận → phục vụ → gọi món → bếp → thanh toán → dọn → chạy `reset-demo` trước khi có hóa đơn/hoặc dùng database mới. | Basline đủ dữ liệu; reset <2 phút, lặp không nhân đôi. Kịch bản chi tiết: `S4-10-Task3-acceptance.md`. |

## 4. Kiểm tra lại nhanh

```powershell
dotnet build tests/RestaurantManagement.AreaTests/RestaurantManagement.AreaTests.csproj --no-restore -p:TreatWarningsAsErrors=false -p:NoWarn=NU1902%3BNU1903
tests/RestaurantManagement.AreaTests/bin/Debug/net10.0/RestaurantManagement.AreaTests.exe
$tests = Get-ChildItem tools/tests -Filter '*.test.cjs' | ForEach-Object FullName
node --test $tests
```

Để chạy rehearsal SQL không đụng database demo: `dotnet run --project tools/RestaurantManagement.DbTool -- verify-s410-task3`. Lệnh tự tạo/xóa `RestaurantManagement_Test_<guid>`; không chạy ở production.

## 5. Phần phụ thuộc hạ tầng

- Staging Internet: chưa được cấp URL/hạ tầng trong repository, nên phải cấu hình hosting, connection string và SMTP riêng trước khi nghiệm thu từ Internet.
- Email delivery thật: kiểm tra inbox/spam và sender đã được Gmail/SMTP provider xác thực. Không dùng mật khẩu Gmail thường; dùng App Password/secret manager theo nhà cung cấp.
- Kiểm tra responsive/browser thật cần cài Playwright theo ghi chú trong `tools/tests/*.check.cjs`.
