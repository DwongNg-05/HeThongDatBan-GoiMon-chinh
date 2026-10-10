# S3-09 Task 2 — Gia hạn giữ bàn một lần

## Quy tắc PO đã chọn

- Hạn mới = **giờ hẹn +30 phút**, không tính từ thời điểm bấm nút. Hẹn 18:00, bấm 18:20 thì giữ đến 18:30.
- Chỉ gia hạn lượt đã xác nhận, khách chưa tới, đang cảnh báo, chưa gia hạn và hiện tại chưa tới giờ hẹn +30 phút.
- Đúng mốc +30 phút thì không thể gia hạn. Gia hạn lúc này sẽ tạo một hạn đã hết nên nút bị khóa và máy chủ cũng từ chối.
- Số lần gia hạn tối đa 1, lưu `ExtensionCount=1` cùng `HoldExtendedUntil` trong một transaction. Khung giờ đặt bàn (`StartsAt`/`EndsAt`) không thay đổi.
- Trong thời gian gia hạn: giữ bàn, tắt cảnh báo, hiện thời hạn mới. Không được đánh dấu khách không tới trước hạn mới, kể cả từ màn hình cũ.
- Khi hiện tại >= hạn mới: cảnh báo bật lại, gia hạn lần hai bị chặn; có thể đánh dấu khách không tới, giải phóng lượt giữ bàn và ghi một dòng lịch sử theo số điện thoại.
- Khách đã tới không còn nằm trong cảnh báo. Chưa có cảnh báo số điện thoại khi đặt bàn lần sau.

## Demo trên web

1. Đăng nhập Manager hoặc Waiter, mở **Danh sách đặt bàn** (`/ReservationManagement`) và **Sơ đồ bàn** (`/`) ở hai tab. `/TableMap` cũng hỗ trợ cùng chức năng.
2. Dùng một lượt đã xác nhận, có bàn, chưa đón khách, trễ từ 15 phút đến dưới 30 phút. Các lượt cũ đã quá 30 phút không dùng để demo gia hạn.
3. Ở cảnh báo, bấm **Gia hạn 15 phút**; hộp xác nhận ghi rõ hạn mới.
4. Hai tab tự cập nhật trong tối đa một chu kỳ 5 giây: cảnh báo được gỡ, xuất hiện **Đang gia hạn · Giữ đến…**, nút **Đã gia hạn 1/1 lần** bị khóa. Thẻ bàn vẫn giữ trạng thái đã đặt trước và hiển thị hạn mới.
5. Đến giờ hẹn +30 phút, cảnh báo xuất hiện lại mà không cần tải trang. Nút gia hạn vẫn khóa.
6. Bấm **Đánh dấu khách không tới**, kiểm tra bàn trống (nếu không có lượt giữ khác), trạng thái không tới trong danh sách và lịch sử số điện thoại thêm đúng một dòng cho lượt đó.

## Migration và kiểm thử

Thêm `044_ReservationHoldExtension.sql`, dùng lại hai cột có sẵn. Không chỉnh migration 043 đã áp dụng. Trên máy khác: cấu hình `RM_CONNECTION_STRING`, chạy DbTool `migrate`, rồi build và chạy lại web.

```powershell
dotnet build tests/RestaurantManagement.AreaTests -p:UseAppHost=false -o .local/hold-extension-check -m:1
dotnet .local/hold-extension-check/RestaurantManagement.AreaTests.dll --hold-extension
dotnet .local/hold-extension-check/RestaurantManagement.AreaTests.dll --no-show
```

Kiểm thử tạo database `RestaurantHoldTest_<GUID>` riêng, chạy SQL thật và xóa database đó khi xong. Bao gồm deadline cố định, trước/đúng/sau hạn, trạng thái, phân quyền, yêu cầu lặp, hai nhân viên gia hạn đồng thời, gia hạn cạnh tranh với không tới, khách đã tới, rollback và lịch sử duy nhất.

Kiểm thử trình duyệt: có Chrome và module Node `playwright` (dùng `NODE_PATH` nếu cần), đặt `$env:HOLD_BROWSER_TEST='1'` rồi chạy `--hold-extension`. Nó kiểm tra cả trang chủ, `/TableMap` và danh sách; đợi đồng hồ máy chủ đi qua hạn thật, không tua giờ của ứng dụng. Website thử dùng cổng ngẫu nhiên, database riêng và tắt gửi email.

## Kết quả trên máy phát triển (10/10/2026)

- Build thành công; 39 kiểm tra Task 2 (gồm Chrome thật) và 31 kiểm tra hồi quy Task 1 đạt.
- Kiểm thử Chrome xác nhận không tải lại trang, hạn mới thống nhất, nhãn trên thẻ bàn không tràn chữ, cảnh báo trở lại đúng hạn và đánh dấu không tới ghi một lần.
- Đã sao lưu `RestaurantManagement_Dev` và áp dụng migration 044. Các tiến trình/database kiểm thử đã được dọn; không chiếm cổng web đang dùng.
- Khởi động lại ứng dụng trong Visual Studio để dùng mã mới.
