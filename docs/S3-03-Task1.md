# S3-03 Task 1: Ghi chú riêng cho từng món, lưu trên đơn hàng

Khách nhập ghi chú tự do cho **từng dòng món** trong giỏ (tối đa 200 ký tự), xem lại ghi chú ở bước xác nhận rồi mới gửi bếp. Ghi chú được lưu đúng với dòng món tương ứng. Hai dòng cùng tên món nhưng khác ghi chú được giữ là hai dòng riêng.

**AC hoàn tất:** ô ghi chú tự do tối đa 200 ký tự; hai dòng cùng món nhưng khác ghi chú không bị gộp số lượng.

**Chưa làm trong task này:** lựa chọn nhanh, hiển thị ghi chú trên màn hình bếp và trên hoá đơn tạm tính.

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Khách nhập ghi chú **dài hơn 200 ký tự**? | Ô nhập **không nhận** ký tự thứ 201 trở đi (gõ hay dán đều vậy). Bộ đếm chuyển thành “200/200 – đã đủ 200 ký tự”, kèm dòng nhắc “Ghi chú tối đa 200 ký tự – phần vượt quá không được nhận”. Nếu yêu cầu gửi lên vẫn có ghi chú quá 200 ký tự (ví dụ gửi thẳng không qua trang), máy chủ **từ chối cả lượt gọi** và báo “Ghi chú ở món thứ N … vượt giới hạn 200 ký tự”. Máy chủ **không tự cắt**, để bếp không nhận ghi chú bị mất nửa câu |
| Đếm “ký tự” thế nào? | Theo chữ người đọc thấy: tiếng Việt được chuẩn hoá về dạng dựng sẵn (NFC), nên “ệ” luôn tính 1 ký tự, kể cả khi bộ gõ tạo dạng tổ hợp. Khoảng trắng ở hai đầu không tính. Emoji tính 2 (giống giới hạn của cột `nvarchar(200)`) |
| Ghi chú rỗng? | Lưu là “không có ghi chú” (`NULL`). Ô chỉ có khoảng trắng cũng coi là rỗng |
| Xuống dòng / tab trong ghi chú? | Đổi thành khoảng trắng (ô nhập một dòng) |
| Bấm “+ Thêm vào giỏ” khi món đã có trong giỏ? | Cộng thêm vào dòng **chưa có ghi chú** của món đó. Nếu mọi dòng của món đều đã có ghi chú thì tạo **dòng mới**, để ghi chú “ít cay” của dòng cũ không bị áp cho phần mới gọi thêm |
| Muốn tách một dòng đang có số lượng 2 thành 2 ghi chú? | Bấm **“+ Dòng ghi chú khác”** trên dòng đó: thêm ngay bên dưới một dòng cùng món, 1 phần, ghi chú trống |
| Hai dòng cùng món, **cùng** ghi chú? | Vẫn để là hai dòng như khách đã chọn, không tự gộp |

**Lựa chọn nhanh ban đầu cần PO chốt** (chưa cài, dành cho task sau): *Ít cay · Không cay · Không hành · Không rau thơm · Ít đá · Không đường · Ít ngọt · Mang ra sau*. Đề xuất bấm chọn nhanh sẽ **nối thêm** vào ô ghi chú (cách nhau bằng dấu phẩy) và vẫn tính chung giới hạn 200 ký tự.

## Cách hoạt động

- **Database:** không cần migration. `dbo.OrderItems.Notes nvarchar(200)` đã có. `usp_SubmitOrder` ghi **mỗi phần tử** của `@ItemsJson` thành **một dòng** `OrderItems` (không gộp theo món) và từ chối ghi chú có `LEN > 200`.
- **Trang khách** (`/TableOrder`, `wwwroot/js/table-order.js`):
  - Mỗi dòng giỏ có ô ghi chú `maxlength=200`, bộ đếm `x/200` và cảnh báo khi đủ 200 ký tự.
  - Có nút “+ Dòng ghi chú khác”.
  - Giỏ, kèm ghi chú, lưu tạm trong `sessionStorage`, nên tải lại trang không mất.
  - Bấm **Đặt món** mở bước **“Kiểm tra lại trước khi gửi bếp”**. Bước này liệt kê từng dòng “1 × Gỏi cuốn – Ghi chú: ít cay”, hoặc “Không có ghi chú”. Khách bấm **Sửa lại** để quay về giỏ, hoặc **Xác nhận gửi bếp** để gửi.
- **Máy chủ** (`GuestTableSessionService.PrepareCart`): chuẩn hoá ghi chú bằng `GuestOrderNotes.Normalize`, rồi kiểm tra 200 ký tự **sau** khi chuẩn hoá. Thứ tự và từng dòng được giữ nguyên khi gửi `usp_SubmitOrder`.
- **Sau khi gửi:** mục “Món bàn đã gọi” hiện mỗi dòng riêng kèm “Ghi chú: …”.

## Thay đổi

| Lớp | File |
| --- | --- |
| Service | `Services/Tables/GuestTableSessionService.cs`: `GuestOrderNotes`, `GuestCartCheck`, `PrepareCart`, `BuildItemsJson` |
| View/JS/CSS | `Views/TableOrder/Index.cshtml` (bước xác nhận), `wwwroot/js/table-order.js` (ghi chú từng dòng, tách dòng, xác nhận), `wwwroot/css/table-order.css` |
| Test | `tests/RestaurantManagement.AreaTests/TableQrSessionTests.cs` (không cần database), `tools/tests/table-order-notes.test.cjs` (Node), `tools/RestaurantManagement.DbTool/QrGuestSessionVerification.cs` (SQL Server, `verify-qr-session`) |
| Database | Không cần migration mới |

## Demo

1. Quét QR một bàn trống. Bấm **+ Thêm vào giỏ** ở “Gỏi cuốn tôm thịt”, nhập ghi chú **ít cay**.
2. Bấm **+ Thêm vào giỏ** ở cùng món lần nữa. Giỏ có dòng thứ hai (vì dòng đầu đã có ghi chú). Nhập **không hành**.
3. Bấm **Đặt món**. Bước xác nhận hiện hai dòng “1 × Gỏi cuốn tôm thịt”, mỗi dòng một ghi chú. Bấm **Xác nhận gửi bếp**.
4. Mục **Món bàn đã gọi** hiện hai dòng riêng: “Ghi chú: ít cay” và “Ghi chú: không hành”.
5. Thử dán một đoạn dài hơn 200 ký tự vào ô ghi chú: ô chỉ giữ 200 ký tự, bộ đếm báo đã đủ.

```sql
SELECT i.Id, i.ItemName, i.Quantity, i.Notes, LEN(i.Notes) AS SoKyTu
FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id = i.BatchId
JOIN dbo.SessionTables st ON st.SessionId = b.SessionId JOIN dbo.DiningTables t ON t.Id = st.TableId
WHERE t.Code = 'A05' ORDER BY i.Id;   -- hai dòng cùng món, mỗi dòng Quantity=1 và ghi chú riêng
```

## Kiểm thử

| Trường hợp | AreaTests | Node | verify-qr-session |
| --- | --- | --- | --- |
| Ghi chú rỗng / chỉ khoảng trắng → `NULL` | ✓ | ✓ | ✓ |
| Ghi chú có dấu tiếng Việt (kể cả gõ dạng tổ hợp NFD) | ✓ | ✓ | ✓ |
| Ghi chú đúng 200 ký tự được lưu nguyên vẹn | ✓ | ✓ | ✓ |
| Từ chối ký tự vượt giới hạn (201 ký tự) | ✓ | ✓ | ✓ |
| Hai dòng cùng món khác ghi chú lưu riêng, không gộp số lượng | ✓ | ✓ | ✓ |
| Hiển thị đúng sau khi xác nhận đơn | ✓ | – | ✓ |

Lệnh chạy:

```
dotnet run --project tests/RestaurantManagement.AreaTests
node --test tools/tests/table-order-notes.test.cjs
dotnet run --project tools/RestaurantManagement.DbTool -- verify-qr-session
```
