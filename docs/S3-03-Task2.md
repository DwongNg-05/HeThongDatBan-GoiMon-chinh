# S3-03 Task 2: Lựa chọn ghi chú nhanh cho từng món

Khách thêm nhanh các yêu cầu khẩu vị phổ biến cho **từng dòng món** mà không phải gõ tay: **Ít cay**, **Không hành**, **Không mỡ**. Khách chọn được nhiều lựa chọn trên một dòng, và các lựa chọn này đi kèm ghi chú tự do của chính dòng đó (tối đa 200 ký tự, xem [S3-03-Task1.md](S3-03-Task1.md)).

**AC hoàn tất:** mỗi dòng món có ô ghi chú tự do tối đa 200 ký tự kèm các lựa chọn nhanh.

**Chưa làm trong task này:** hiển thị ghi chú trên màn hình bếp và trên hoá đơn tạm tính.

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Lưu ở đâu? | Cột riêng `dbo.OrderItems.QuickNotes nvarchar(100)`, ví dụ `Ít cay, Không hành`. Ghi chú tự do vẫn ở `Notes` (200 ký tự). Lựa chọn nhanh **không tính** vào 200 ký tự của ghi chú tự do |
| Thứ tự lưu? | Luôn theo thứ tự hiển thị (Ít cay → Không hành → Không mỡ), không phụ thuộc thứ tự khách bấm, và không lưu trùng |
| Bỏ chọn hết? | Lưu `NULL`, như dòng không có lựa chọn nhanh |
| Lựa chọn lạ (không có trong danh sách)? | Máy chủ từ chối lượt gọi và báo “Lựa chọn nhanh ở món thứ N… không hợp lệ” |
| Bấm “+ Thêm vào giỏ” khi món đã có dòng có lựa chọn nhanh? | Coi như dòng đó đã có yêu cầu riêng: tạo **dòng mới** không có lựa chọn nào, giống quy tắc ghi chú của Task 1 |
| Đổi danh sách lựa chọn? | Sửa `GuestOrderNotes.QuickOptions` ở một chỗ duy nhất; trang khách nhận danh sách qua `data-quick-notes` |

## Cách hoạt động

- **Database** (`047_S303QuickNotes.sql`, chạy `migrate`):
  - Thêm cột `OrderItems.QuickNotes`.
  - `usp_SubmitOrder` nhận thêm thuộc tính `QuickNotes` cho mỗi phần tử `@ItemsJson`, chuỗi tối đa 100 ký tự.
  - Giỏ không gửi thuộc tính này, ví dụ giỏ của nhân viên ở `/Ordering`, vẫn chạy như cũ.
- **Trang khách** (`/TableOrder`):
  - Mỗi dòng giỏ có 3 nút bật/tắt (`aria-pressed`), bấm lại để bỏ chọn. Bên dưới là ô ghi chú tự do của chính dòng đó.
  - Bước xác nhận hiện “Ít cay · Không hành” và “Ghi chú: …” cho từng dòng.
  - Giỏ gửi `quickNotes: ["it-cay", "khong-hanh"]` kèm `notes` của từng dòng.
- **Máy chủ:** `GuestOrderNotes.NormalizeQuickNotes` nhận mã hoặc nhãn, bỏ trùng, xếp theo thứ tự chuẩn. Bước này nằm trong `PrepareCart`, và `BuildItemsJson` ghép thành chuỗi để lưu.
- **Sau khi gửi:** mục “Món bàn đã gọi” hiện lựa chọn nhanh (chữ đậm), rồi đến “Ghi chú: …” của từng dòng.

## Demo

1. Chạy `dotnet run --project tools/RestaurantManagement.DbTool -- migrate` để áp dụng `047_S303QuickNotes.sql`, rồi chạy lại web.
2. Quét QR một bàn. Thêm “Gỏi cuốn”, bấm **Ít cay** và **Không hành**, nhập ghi chú “thêm chanh”.
3. Thêm “Gỏi cuốn” lần nữa: giỏ có dòng thứ hai, chưa chọn gì. Bấm **Không mỡ**, nhập “mang ra sau”.
4. Bấm **Đặt món**. Bước xác nhận hiện:
   - Dòng 1: “Ít cay · Không hành”, “Ghi chú: thêm chanh”.
   - Dòng 2: “Không mỡ”, “Ghi chú: mang ra sau”.
5. Bấm **Xác nhận gửi bếp**. Mục “Món bàn đã gọi” giữ đúng thông tin theo từng dòng.

```sql
SELECT i.Id, i.ItemName, i.Quantity, i.QuickNotes, i.Notes
FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id = i.BatchId
ORDER BY i.Id DESC;   -- mỗi dòng có QuickNotes và Notes riêng
```

## Kiểm thử

| Trường hợp | AreaTests | Node | verify-qr-session |
| --- | --- | --- | --- |
| Chọn từng lựa chọn nhanh | ✓ | ✓ | ✓ |
| Chọn nhiều lựa chọn trên một dòng | ✓ | ✓ | ✓ |
| Kết hợp lựa chọn nhanh với ghi chú tự do | ✓ | ✓ | ✓ |
| Bỏ chọn lựa chọn | ✓ | ✓ | ✓ (dòng không chọn → `NULL`) |
| Hai dòng cùng món không lẫn lựa chọn và ghi chú | ✓ | ✓ | ✓ |
| Lựa chọn lạ bị từ chối | ✓ | ✓ | ✓ |
| Hiển thị sau khi gửi | ✓ | – | ✓ |

```
dotnet run --project tests/RestaurantManagement.AreaTests
node --test tools/tests/table-order-notes.test.cjs
dotnet run --project tools/RestaurantManagement.DbTool -- verify-qr-session
```
