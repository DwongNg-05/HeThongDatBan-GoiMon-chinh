# Task 3 — Xong cả phiếu (AC2)

## Phương án triển khai cần PO xác nhận

- Mỗi phiếu đang ở hàng đợi bếp có nút **Xong cả phiếu**. Nút bị vô hiệu hóa kèm hướng dẫn nếu phiếu còn dòng Pending. Chỉ cho dùng khi còn ít nhất một dòng Preparing và không còn Pending.
- Chỉ Preparing → Ready. Không tự chuyển Pending qua hai bước và không bỏ qua quy tắc Task 1.
- Ready đã hoàn thành trước đó giữ nguyên dữ liệu, RowVersion, ReadyAt, PreparingAt và lịch sử. Served/Cancelled không bị tác động.
- Có xác nhận trước khi hoàn tất, giữ nguyên quy tắc không chuyển lùi.

Đây là mặc định triển khai, chưa ghi nhận PO đã duyệt. Các lựa chọn PO ở Task 1 và Task 2 cũng vẫn cần xác nhận trước khi nghiệm thu toàn bộ story.

## Thực hiện

Migration mới `045_KitchenBatchComplete.sql` thêm `usp_KitchenBatchComplete`. Cả phiếu chạy trong một giao dịch, sử dụng cùng khóa nghiệp vụ với Task 1/2 và gọi `usp_KitchenLineTransition` cho từng dòng Preparing. Mỗi món có thời điểm UTC, đúng một sự kiện Preparing → Ready và thời gian chế biến riêng theo Task 2. Lỗi tại bất kỳ món nào hoàn tác tất cả cập nhật và sự kiện của lần bấm này.

Màn hình nhóm món theo phiếu. Đọc snapshot cũng giữ khóa nghiệp vụ trong giao dịch ngắn, để không đọc được phiếu giữa lúc đang hoàn tất. Sau thao tác trang đọc lại ngay, tất cả món bị tác động cùng rời hàng đợi và vào Chờ mang ra. Phục vụ tự cập nhật mỗi giây. Phiếu đã hoàn tất không còn nằm trong hàng đợi; gửi lại yêu cầu trả changed=0, không ghi thêm thời điểm/sự kiện.

API `POST /Kitchen/CompleteBatch` nhận batchId và antiforgery token. Chỉ Bếp được gọi, SQL kiểm tra lại Kitchen.Manage; phục vụ/quản lý không được chuyển trạng thái. Phiếu không tồn tại hoặc phiên đóng nhận 409; còn Pending nhận 409 với thông báo rõ ràng. Nút vô hiệu hóa chỉ là hỗ trợ giao diện; máy chủ luôn kiểm tra lại trạng thái hiện tại.

## Demo

1. Tạo một phiếu có ít nhất ba dòng món khác ghi chú qua Gọi món.
2. Bếp mở `/Kitchen`; khi còn Chờ bếp, nút Xong cả phiếu bị vô hiệu hóa.
3. Bấm Bắt đầu trên từng dòng. Có thể bấm Xong trước trên một món để thử giữ nguyên thời điểm món đó.
4. Bấm Xong cả phiếu và xác nhận: mọi món đang chế biến chuyển sang Đã xong, rời hàng đợi và xuất hiện trong Chờ mang ra với thời gian chế biến thực tế. Món đã xong trước đó không đổi.
5. Phục vụ mở `/Kitchen/Ready` ở thiết bị khác để thấy các món cập nhật không tải lại trang.

```sql
DECLARE @BatchId bigint=1; -- thay bằng phiếu demo
SELECT i.Id,i.ItemName,i.Status,i.PreparingAt,i.ReadyAt,
 DATEDIFF_BIG(millisecond,i.PreparingAt,i.ReadyAt) AS ActualCookingMilliseconds
FROM dbo.OrderItems i WHERE i.BatchId=@BatchId;
SELECT e.OrderItemId,e.FromStatus,e.ToStatus,e.OccurredAt
FROM dbo.OrderItemEvents e JOIN dbo.OrderItems i ON i.Id=e.OrderItemId
WHERE i.BatchId=@BatchId AND e.FromStatus IS NOT NULL
ORDER BY e.OrderItemId,e.OccurredAt,e.Id;
```

## Kiểm tra

`node tools/tests/kitchen.test.cjs` kiểm tra điều kiện nút, xác nhận/hủy, chỉ tài khoản bếp có nút, chuyển cả phiếu và món hiện một lần ở Chờ mang ra.

`dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-kitchen` (cấu hình RM_CONNECTION_STRING trước) chạy trên database tạm riêng: giữ nguyên món Ready, chặn Pending, hai yêu cầu đồng thời và yêu cầu HTTP lặp, đủ hai sự kiện từng món, thời lượng, quyền và antiforgery. Một trigger chỉ trong database kiểm thử gây lỗi ở món cuối sau khi món trước đã chuyển; kiểm tra tất cả trạng thái, thời điểm và lịch sử đều được hoàn tác. Trigger và database kiểm thử được dọn sau khi chạy.

Các chức năng AC1/AC3 (Task 1), AC4 (Task 2) và AC2 (Task 3) đã được triển khai; nghiệm thu PO là bước còn cần xác nhận.
