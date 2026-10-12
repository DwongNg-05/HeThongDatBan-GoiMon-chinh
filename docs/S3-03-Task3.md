# S3-03 Task 3: Hiển thị ghi chú rõ ràng trên màn hình bếp

Giúp bếp đọc chính xác yêu cầu riêng của từng món khi chế biến. Mỗi dòng món trên **Màn hình bếp** (`/Kitchen`) hiển thị các lựa chọn nhanh (S3-03 Task 2) và ghi chú tự do (S3-03 Task 1) **ngay dưới tên món**, chữ to và không bị cắt.

**AC hoàn tất:** ghi chú hiển thị ngay dưới tên món trên màn hình bếp, dễ đọc, không bị cắt chữ.

**Chưa làm trong task này:** giữ ghi chú trên hoá đơn tạm tính.

## Cách hiển thị

- **Ô “Món & yêu cầu riêng”:** gồm tên món, rồi khung nền vàng viền cam chứa:
  - **Lựa chọn nhanh:** mỗi lựa chọn là một nhãn đỏ chữ trắng, ví dụ `Ít cay` `Không hành`.
  - **Ghi chú tự do:** dòng `Ghi chú: …`.
- **Cột “Ghi chú” riêng đã bỏ**, để bếp không phải dò sang cột khác.
- **Cỡ chữ cố định theo px**, vì trang đặt font gốc 14px:

  | Phần | Máy tính | Điện thoại |
  | --- | --- | --- |
  | Tên món | 18px | 18px |
  | Ghi chú | 17px | 16px |
  | Nhãn lựa chọn | 15px | 15px |
- **Không bao giờ cắt chữ:** không dùng `ellipsis`, `line-clamp` hay chiều cao cố định. Ghi chú tự ngắt dòng (`overflow-wrap: anywhere`, `white-space: pre-wrap`), nên đủ 200 ký tự vẫn hiện hết.
- **Dòng không có yêu cầu riêng** thì không có khung ghi chú, để bếp chỉ chú ý tới các món cần lưu ý.
- **Mỗi dòng đơn (`dbo.OrderItems`) là một dòng bếp.** Hai dòng cùng món nhưng khác yêu cầu hiển thị riêng, không gộp số lượng.
- **Nguồn dữ liệu:** màn hình đọc thẳng `OrderItems.Notes` và `OrderItems.QuickNotes` đã lưu trên đơn, nên tải lại trang là thấy đơn mới.

## Thay đổi

| Lớp | File |
| --- | --- |
| Controller | `Controllers/Kitchen/KitchenController.cs`: truy vấn đọc thêm `i.QuickNotes` (cần migration `047_S303QuickNotes.sql`) |
| Model | `Models/Kitchen/KitchenViewModels.cs`: `KitchenOrderItem.QuickNotes`, `QuickNoteLabels`, `FreeNote`, `HasRequests` |
| View/CSS | `Views/Kitchen/Index.cshtml`, `wwwroot/css/kitchen.css` (mới) |
| Test | `tests/RestaurantManagement.AreaTests/TableQrSessionTests.cs`, `tools/RestaurantManagement.DbTool/KitchenCashierVerification.cs` (chạy trong `verify`) |
| Database | Không cần migration mới (dùng cột `QuickNotes` của Task 2) |

## Demo

1. Khách quét QR, thêm “Gỏi cuốn” hai lần:
   - Dòng 1 chọn **Ít cay** và **Không hành**, ghi chú “thêm chanh”.
   - Dòng 2 chọn **Không mỡ**, ghi chú “mang ra sau”.

   Khách xác nhận rồi gửi bếp.
2. Đăng nhập tài khoản **Bếp** và mở **Màn hình bếp**: có hai dòng “Gỏi cuốn” riêng biệt. Mỗi dòng có khung yêu cầu ngay dưới tên món, đúng lựa chọn và ghi chú của dòng đó.
3. Gửi thêm một món với ghi chú dài 200 ký tự: ghi chú tự ngắt dòng và hiện đầy đủ.

## Kiểm thử

| Trường hợp | AreaTests | verify (SQL Server + web thật) |
| --- | --- | --- |
| Ghi chú hiển thị dưới đúng tên món (cùng ô, sau tên món) | – | ✓ |
| Ghi chú 200 ký tự không bị cắt | ✓ | ✓ (nội dung đầy đủ + CSS không cắt chữ) |
| Tiếng Việt có dấu | ✓ | ✓ |
| Hai dòng cùng món hiển thị riêng với yêu cầu tương ứng | – | ✓ |
| Dòng chỉ có lựa chọn nhanh / không có yêu cầu | ✓ | ✓ |

```
dotnet run --project tests/RestaurantManagement.AreaTests
dotnet run --project tools/RestaurantManagement.DbTool -- verify
```
