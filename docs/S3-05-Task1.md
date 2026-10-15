# S3-05 Task 1: Phục vụ huỷ món đang chờ bếp với lý do bắt buộc

Phục vụ huỷ một dòng món còn chờ bếp trên **Hoá đơn tạm tính**, bắt buộc chọn lý do. Hàng đợi bếp và tạm tính cập nhật theo.

**Hoàn tất:** AC 1, AC 2 và AC 3 theo phạm vi đã chốt; phần ghi nhật ký của AC 4.

**Chưa làm trong task này:** màn hình báo cáo nhật ký huỷ cuối ca, và thao tác huỷ dành cho Quản lý sau khi bếp bắt đầu làm. Thao tác này đã có sẵn từ trước và vẫn chạy như cũ (món vẫn tính tiền), nhưng không thuộc phạm vi task.

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Huỷ toàn bộ số lượng hay một phần? | **Toàn bộ dòng.** Mỗi lần gọi là một dòng riêng (S3-04). Muốn bớt số lượng thì huỷ dòng rồi gọi lại số mới, nên lịch sử vẫn đầy đủ. |
| Tính tiền món đã huỷ | Huỷ khi món còn **Chờ bếp**: **không tính tiền**. Quản lý huỷ sau khi bếp **đã bắt đầu** (đang nấu, xong, đã mang ra): **vẫn tính tiền**. Quy tắc này có từ migration 002, task này giữ nguyên. |
| Ai huỷ được? | **Phục vụ:** chỉ món còn chờ bếp. **Quản lý:** mọi món chưa huỷ. **Thu ngân:** chỉ xem. **Bếp:** không mở được hoá đơn. |
| Lý do | Bắt buộc, không chọn sẵn: **Khách đổi ý**, **Gọi nhầm món**, **Hết nguyên liệu**. Quản lý có thêm **Quản lý yêu cầu**. Ghi chú thêm là tuỳ chọn (tối đa 500 ký tự). |
| Bếp bắt đầu nấu trong lúc nhân viên chọn lý do | Từ chối. Hoá đơn tải lại với trạng thái mới, và nút của Phục vụ bị vô hiệu. Với Quản lý cũng từ chối, để Quản lý biết món giờ vẫn tính tiền rồi mới xác nhận lại. |
| Bấm xác nhận nhiều lần, hoặc hai người cùng huỷ | Chỉ lần đầu được ghi. Các lần sau báo "đã được hủy trước đó… không trừ tiền thêm". |
| Bàn đã thanh toán | Không còn nút huỷ. Máy chủ cũng từ chối. |

## Cách hoạt động

- **Hoá đơn tạm tính** (`/ProvisionalBill/{phiên}`):
  - Mỗi dòng chờ bếp có nút **Hủy món**. Bấm vào sẽ mở hộp xác nhận, ghi rõ món, số lượng, số tiền và việc món có còn tính tiền không.
  - Ba lý do (Quản lý thấy bốn) không được chọn sẵn. Nút **Xác nhận hủy** chỉ bật khi đã chọn một lý do. Nếu gửi mà chưa chọn (ví dụ bấm Enter), trình duyệt chặn lại và hiện thông báo.
  - Trong lúc gửi, cả hai nút bị khoá, nên bấm nhanh hai lần chỉ gửi một yêu cầu.
  - Với món bếp đã bắt đầu, Phục vụ thấy nút **Hủy món** bị vô hiệu, kèm lý do ngay bên dưới, ví dụ "Bếp đang chế biến món này — chỉ Quản lý được hủy và món vẫn tính tiền."
  - Sau khi xác nhận, hoá đơn tải lại:
    - đầu trang hiện kết quả: thành công (đã trừ bao nhiêu, tạm tính mới), thông tin (đã huỷ trước đó) hoặc lỗi kèm lý do;
    - dòng món hiện "Đã hủy", cột tiền thành "—", tạm tính giảm đúng;
    - mục **Nhật ký hủy món** có thêm một dòng.
- **Máy chủ** (`ProvisionalBillController.CancelItem`):
  - Chỉ Quản lý và Phục vụ, có mã chống giả mạo.
  - Kiểm tra lý do theo vai trò: thiếu lý do, lý do lạ, hay Phục vụ dùng "Quản lý yêu cầu" đều bị từ chối trước khi chạm database.
  - Gửi kèm `expectedStatus`, tức trạng thái dòng món lúc mở hộp xác nhận.
- **Database:** migration `056_S305CancelOrderItem.sql` viết lại `dbo.usp_CancelOrderItem`. Các lời gọi cũ vẫn chạy.
  - Mọi bước nằm trong **một giao dịch có khoá thao tác chung** (`usp_LockOperations`). Bếp chuyển trạng thái món cũng dùng khoá này, nên "bếp bắt đầu nấu" và "phục vụ huỷ" của cùng một món không bao giờ cùng thành công.
  - Kiểm tra theo thứ tự: quyền `Orders.Manage` → dòng món thuộc đúng hoá đơn đang mở (51033) → lý do hợp lệ (51032) → "Quản lý yêu cầu" chỉ cho Quản lý (51036) → bàn chưa thanh toán (51031) → dòng đã huỷ thì trả `AlreadyCancelled`, không ghi gì thêm → trạng thái đổi so với lúc mở hộp (51034) → Phục vụ chỉ huỷ món còn chờ bếp (51035).
  - Ghi trên dòng món: `Status = Cancelled`, `CancelledBy`, `CancelledAt`, `CancelReason`, `CancellationNote`, và `ChargeWhenCancelled = 0` khi món còn chờ bếp.
  - **Nhật ký:** mỗi lần huỷ thành công ghi đúng một dòng `dbo.OrderItemEvents` (dòng món, người thực hiện, thời điểm, lý do + ghi chú).
  - Trả về kết quả gồm số tiền của dòng và **tạm tính mới** của cả hoá đơn (phiên gốc và các phiên gộp vào).
- **Màn hình bếp** (`/Kitchen`):
  - Truy vấn bỏ món đã huỷ, nên món biến mất ở lần tự tải lại kế tiếp (mỗi 3 giây, tức trong vòng 5 giây).
  - Trước đây màn hình không tải lại khi một nút trong danh sách còn giữ focus. Nay chỉ hoãn khi đầu bếp **đang bấm**; sau khi cập nhật, focus trở lại nút của cùng dòng món.
  - Màn hình tải lại ngay khi quay lại thẻ trình duyệt.
  - Nếu bếp bấm "Bắt đầu nấu" vào món vừa bị huỷ (màn hình chưa kịp tải lại), máy chủ từ chối với thông báo "Món này vừa được phục vụ hủy nên không cần chế biến nữa".

### Thay đổi về mã lỗi

Phục vụ huỷ món bếp đã bắt đầu nay nhận **51035** kèm thông báo rõ ràng, thay vì lỗi quyền chung 51001. Huỷ lặp nay trả kết quả `AlreadyCancelled`, thay vì lỗi 51031. Kiểm thử cũ trong `Verification.cs` đã cập nhật theo.

### Sửa thêm

Bảng phân quyền API (`ApiAccessMatrix`) trước đây thiếu `ProvisionalBill.CancelItem POST`, khiến bài kiểm thử "mọi API đều có trong bảng phân quyền" của AreaTests báo thiếu. Đã bổ sung (Quản lý, Phục vụ).

## Thay đổi

| Lớp | File |
| --- | --- |
| Database | `database/migrations/056_S305CancelOrderItem.sql` (mới) |
| Model / quy tắc | `Models/Orders/OrderCancellation.cs` (mới: `OrderCancelRules`, `OrderCancelResult`, `OrderCancelStore`), `Models/Orders/ProvisionalBillViewModels.cs`, `Models/Kitchen/KitchenViewModels.cs` |
| Controller | `Controllers/Orders/ProvisionalBillController.cs` (`CancelItem`), `Controllers/Kitchen/KitchenController.cs` |
| View / JS / CSS | `Views/ProvisionalBill/Index.cshtml` (nút huỷ, nút vô hiệu, hộp xác nhận), `wwwroot/js/provisional-bill-cancel.js` (mới), `wwwroot/css/provisional-bill.css`, `Views/Kitchen/Index.cshtml` (tải lại 3 giây kể cả khi nút còn giữ focus) |
| Phân quyền | `tools/RestaurantManagement.DbTool/ApiAccessMatrix.cs` |
| Test | `tests/RestaurantManagement.AreaTests/OrderCancellationTests.cs` (mới), `tools/tests/provisional-bill-cancel.test.cjs` (mới), `tools/RestaurantManagement.DbTool/OrderCancellationVerification.cs` (mới, chạy trong `verify` và lệnh mới `verify-cancel-item`), `tools/RestaurantManagement.DbTool/Verification.cs` |

## Demo

1. Chạy `dotnet run --project tools/RestaurantManagement.DbTool -- migrate`, rồi chạy lại web.
2. Đăng nhập Phục vụ, đón khách vào một bàn và gọi 2–3 món. Mở **Màn hình bếp** bằng tài khoản Bếp (hoặc Quản lý) ở một cửa sổ khác: các món hiện "Chờ nấu".
3. Ở **Sơ đồ bàn**, bấm bàn đó rồi bấm **Xem hoá đơn tạm tính**. Ghi lại số tạm tính.
4. Bấm **Hủy món** ở một dòng. Nút **Xác nhận hủy** đang tắt. Chọn "Gọi nhầm món" thì nút bật; bấm xác nhận.
   - Hoá đơn báo "Đã hủy … trừ … khỏi tạm tính", dòng hiện "Đã hủy" và tạm tính giảm đúng số tiền dòng đó.
   - Trong vòng 3 giây, món biến mất khỏi màn hình bếp mà không phải tải lại trang.
   - Mục **Nhật ký hủy món** ghi món, lý do, tên Phục vụ và giờ huỷ.
5. Ở màn hình bếp, bấm **Bắt đầu nấu** cho một món khác rồi tải lại hoá đơn: nút **Hủy món** của dòng đó bị vô hiệu, kèm lý do "Bếp đang chế biến món này…".
6. Thử tình huống đồng thời: mở hộp huỷ của một món còn chờ bếp, chưa xác nhận. Ở màn hình bếp bấm **Bắt đầu nấu** món đó, rồi quay lại bấm **Xác nhận hủy**. Hoá đơn báo "Bếp đã bắt đầu chế biến món này trong lúc bạn chọn lý do…", món không bị huỷ và tạm tính không đổi.

```sql
-- Nhật ký huỷ món của một phiên
SELECT i.ItemName, i.Quantity, i.LineTotal, i.ChargeWhenCancelled, i.CancelReason, u.FullName AS NguoiHuy,
       DATEADD(hour, 7, i.CancelledAt) AS LucHuyVN, e.Reason AS NhatKy
FROM dbo.OrderItems i
JOIN dbo.OrderBatches b ON b.Id = i.BatchId
JOIN dbo.Users u ON u.Id = i.CancelledBy
JOIN dbo.OrderItemEvents e ON e.OrderItemId = i.Id AND e.ToStatus = 'Cancelled'
WHERE b.SessionId = <mã phiên> AND i.Status = 'Cancelled';
```

## Kiểm thử

| Trường hợp | AreaTests | node | verify (SQL Server + web thật) |
| --- | --- | --- | --- |
| Huỷ món chờ bếp với từng lý do: không tính tiền, tạm tính giảm đúng | ✓ (quy tắc, thông báo) | ✓ (bật nút theo từng lý do) | ✓ |
| Thiếu lý do / lý do lạ / Phục vụ dùng "Quản lý yêu cầu" | ✓ | ✓ (không gửi được khi chưa chọn) | ✓ (gửi thẳng tới máy chủ và gọi thẳng thủ tục) |
| Nút huỷ của Phục vụ bị vô hiệu khi món đang chế biến, có lý do | ✓ | – | ✓ (cả gửi thẳng cũng bị từ chối) |
| Yêu cầu không hợp lệ gửi trực tiếp: dòng của bàn khác, trạng thái giả, Bếp / Thu ngân / chưa đăng nhập | ✓ (SQL) | – | ✓ |
| Bếp bắt đầu nấu trong lúc chọn lý do (Phục vụ và Quản lý); 8 cặp "nấu / huỷ" gửi đồng thời | ✓ (SQL) | – | ✓ |
| Món biến mất khỏi màn hình bếp trong 5 giây; bếp bấm nấu món đã huỷ bị từ chối | ✓ (lọc, tải lại 3 giây) | – | ✓ (đo thời gian) |
| Nhật ký đủ: dòng món, người thực hiện, thời điểm, lý do, ghi chú | ✓ (SQL) | – | ✓ |
| Xác nhận lặp, hai người cùng huỷ, 6 yêu cầu song song: không trừ tiền thêm | ✓ | ✓ (khoá nút khi đang gửi) | ✓ |

```
dotnet run --project tests/RestaurantManagement.AreaTests
node --test tools/tests/provisional-bill-cancel.test.cjs
dotnet run --project tools/RestaurantManagement.DbTool -- verify-cancel-item
```
