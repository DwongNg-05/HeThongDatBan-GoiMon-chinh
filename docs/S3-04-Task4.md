# S3-04 Task 4: Giữ ghi chú trên hoá đơn tạm tính

Giúp nhân viên phục vụ đối chiếu yêu cầu của khách khi mang món ra bàn. Mỗi dòng món trên **hoá đơn tạm tính** (`/ProvisionalBill/{mã phiên}`) có lựa chọn nhanh (S3-03 Task 2) và ghi chú tự do (S3-03 Task 1) ngay dưới tên món. Nội dung này đúng như bếp đã nhận (S3-03 Task 3).

**AC hoàn tất:** ghi chú được giữ nguyên trên hoá đơn tạm tính để phục vụ đối chiếu khi mang món ra. Không còn phần nào chưa làm.

## Mở hoá đơn tạm tính

| Ai | Từ đâu |
| --- | --- |
| Phục vụ, Quản lý | **Sơ đồ bàn**: bấm bàn đang có khách, rồi bấm **Xem hoá đơn tạm tính** (`/ProvisionalBill/Table/{mã bàn}` chuyển tới phiên đang phục vụ của bàn) |
| Thu ngân, Quản lý | **Thanh toán**: bấm liên kết **Xem hoá đơn tạm tính** dưới số tiền của từng phiên |
| Bếp | Không được mở (máy chủ chặn); bếp đã có màn hình bếp |

Trang chỉ để xem, có nút **In tạm tính**. Khi in, trang ẩn thanh điều hướng và các nút.

## Cách hiển thị

- **Mỗi dòng `dbo.OrderItems` là một dòng hoá đơn:**
  - Hai dòng cùng món nhưng khác yêu cầu, hoặc gọi ở hai lượt khác nhau, hiển thị riêng, không gộp số lượng.
  - Các dòng xếp theo thời điểm gọi.
- **Ô “Món & yêu cầu riêng”:** gồm tên món, rồi khung nền vàng chứa:
  - các lựa chọn nhanh, dạng nhãn đỏ (ví dụ `Ít cay` `Không hành`);
  - dòng `Ghi chú: …`.

  Bên dưới khung là bàn (khi đã gộp bàn), giờ gọi và trạng thái chế biến của dòng.
- **Không cắt chữ:** ghi chú đủ 200 ký tự tiếng Việt vẫn hiện hết và tự ngắt dòng. Trang không dùng `ellipsis`, `line-clamp` hay chiều cao cố định.
- **Trên điện thoại:** mỗi dòng thành một khối. Tên món và ghi chú chiếm cả chiều ngang, số lượng và tiền nằm ở dòng dưới, nên không phải cuộn ngang.
- **Món đã huỷ:** vẫn giữ dòng và ghi chú, gạch tên món, ghi “Đã huỷ”, không tính tiền. Nếu huỷ sau khi bếp đã làm, dòng ghi “Đã huỷ (vẫn tính tiền)”, theo đúng quy tắc của `vw_SessionTotals`.
- **Tổng tạm tính:** chưa trừ giảm giá. Bàn đã gộp hiển thị chung một hoá đơn của phiên thanh toán.

## Giữ nguyên và khớp với bếp

| Yêu cầu | Cách làm |
| --- | --- |
| Khớp với màn hình bếp | Hoá đơn và màn hình bếp cùng đọc hai cột `OrderItems.QuickNotes` và `OrderItems.Notes` của cùng một dòng. Cả hai tách nhãn và ghi chú qua cùng lớp `Models/Orders/OrderItemRequests`, nên hai nơi luôn hiện cùng nội dung |
| Mở lại hoá đơn | Không lưu bản sao hay bộ nhớ đệm: mỗi lần mở, hoá đơn đọc lại từ database |
| Đơn được cập nhật | Gọi thêm, đổi trạng thái chế biến, mang ra hay huỷ món đều không đụng tới hai cột ghi chú |
| Không ai sửa được ghi chú đã gửi bếp | Trigger `tr_OrderItems_KeepNotes` (migration `048_S304KeepOrderNotes.sql`) từ chối mọi `UPDATE` làm đổi ghi chú hoặc lựa chọn nhanh, kể cả chỉ đổi hoa/thường hay dấu, với lỗi 51113. Muốn đổi yêu cầu thì huỷ dòng cũ và gọi dòng mới, để lịch sử vẫn còn |

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `048_S304KeepOrderNotes.sql`: trigger giữ ghi chú. Chạy `migrate` |
| Controller | `Controllers/Orders/ProvisionalBillController.cs` (mới): `GET /ProvisionalBill/{sessionId}`, `GET /ProvisionalBill/Table/{code}` |
| Model | `Models/Orders/OrderItemRequests.cs` (mới, dùng chung với bếp), `Models/Orders/ProvisionalBillViewModels.cs` (mới), `Models/Kitchen/KitchenViewModels.cs` (bếp đọc qua `OrderItemRequests`) |
| View/CSS/JS | `Views/ProvisionalBill/Index.cshtml`, `Missing.cshtml`, `wwwroot/css/provisional-bill.css` (mới). Nút mở hoá đơn được thêm ở `Views/Home/Index.cshtml` + `wwwroot/js/table-map.js` + `wwwroot/css/site.css`, và ở `Views/Cashier/Index.cshtml` |
| Phân quyền | `Security/AppRoles.cs` (`BillReaders` = Quản lý, Phục vụ, Thu ngân), `tools/RestaurantManagement.DbTool/ApiAccessMatrix.cs` |
| Test | `tests/RestaurantManagement.AreaTests/TableQrSessionTests.cs`, `RoleNavigationTests.cs`, `tools/RestaurantManagement.DbTool/KitchenCashierVerification.cs` (chạy trong `verify`) |

## Demo

1. Chạy `dotnet run --project tools/RestaurantManagement.DbTool -- migrate` để áp dụng migration 048, rồi chạy lại web.
2. Khách quét QR bàn, gọi “Gỏi cuốn” hai dòng:
   - Dòng 1 chọn **Ít cay**, **Không hành**, ghi chú “thêm chanh”.
   - Dòng 2 chọn **Không mỡ**, ghi chú “mang ra sau”.

   Khách xác nhận gửi bếp.
3. Mở **Màn hình bếp** bằng tài khoản Bếp: có hai dòng “Gỏi cuốn” với yêu cầu riêng.
4. Đăng nhập **Phục vụ** và mở **Sơ đồ bàn**:
   - Bấm bàn đó, rồi bấm **Xem hoá đơn tạm tính**.
   - Hoá đơn có hai dòng “Gỏi cuốn” riêng. Dưới mỗi tên món là đúng lựa chọn nhanh và ghi chú như trên màn hình bếp.
5. Bếp bấm “Bắt đầu nấu” dòng 1, rồi khách gọi thêm “Gỏi cuốn” với **Ít cay**. Mở lại hoá đơn:
   - Ghi chú các dòng cũ không đổi.
   - Dòng mới là dòng thứ ba, có yêu cầu riêng của nó.

## Kiểm thử

| Trường hợp | AreaTests | verify (SQL Server + web thật) |
| --- | --- | --- |
| Ghi chú và lựa chọn nhanh hiển thị đúng, ngay dưới tên món | ✓ | ✓ |
| Ghi chú 200 ký tự tiếng Việt không bị cắt | ✓ | ✓ |
| Mở lại hoá đơn (Phục vụ, Thu ngân) ra đúng nội dung cũ | – | ✓ |
| Đơn cập nhật (nấu, gọi thêm, huỷ món), ghi chú cũ giữ nguyên | ✓ | ✓ |
| Database từ chối sửa ghi chú hoặc lựa chọn nhanh đã gửi bếp | ✓ (migration) | ✓ (lỗi 51113) |
| Hai/ba dòng cùng món không bị gộp | ✓ | ✓ |
| Ghi chú trên hoá đơn khớp màn hình bếp từng dòng | ✓ | ✓ (trước và sau khi cập nhật đơn) |
| Bếp không mở được hoá đơn; Phục vụ, Thu ngân, Quản lý mở được | ✓ | ✓ |

```
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet run --project tests/RestaurantManagement.AreaTests
dotnet run --project tools/RestaurantManagement.DbTool -- verify
```
