# Task 2 — Thời điểm và thời gian chế biến (AC4)

**Cập nhật Task 3:** đã bổ sung nút Xong cả phiếu theo [Kitchen-Task3.md](Kitchen-Task3.md). Các mô tả “không có nút cả phiếu” bên dưới thuộc phạm vi Task 2 trước đó.

## Quyết định triển khai chờ PO xác nhận

- Chế biến thực tế = ReadyAt − PreparingAt, không tính thời gian chờ bếp.
- Chờ bếp = PreparingAt − SubmittedAt; với món Pending, thời gian chờ tăng đến hiện tại.
- Lấy thời gian từ SQL Server, lưu UTC; hiển thị thời điểm theo Asia/Ho_Chi_Minh (UTC+7), kèm thời lượng ở bếp và khu vực chờ mang ra.

Các lựa chọn trên là mặc định đề xuất, chưa được xác nhận là PO đã duyệt.

## Lưu dữ liệu

Migration mới `044_KitchenLineTiming.sql` thay thủ tục `usp_KitchenLineTransition`; không sửa migration đã áp dụng. Cùng một giao dịch kiểm tra quyền, trạng thái và RowVersion, cập nhật Status và PreparingAt/ReadyAt rồi thêm đúng một dòng OrderItemEvents gồm OrderItemId, FromStatus, ToStatus, ActorUserId, OccurredAt. Các yêu cầu chuyển lùi, nhảy cóc, sai quyền, phiên đóng hoặc phiên bản cũ không ghi thời điểm và không thêm lịch sử.

Thời điểm có độ chính xác mili giây. Nếu hai bước cùng mili giây hoặc đồng hồ máy chủ lùi, thời điểm sau được nâng lên ít nhất 1 ms so với sự kiện trước/SubmittedAt để giữ thứ tự tăng nghiêm ngặt. Phép tính giữ chính xác mili giây, giao diện hiển thị phút:giây. Dữ liệu thời lượng chưa hoàn tất hoặc dữ liệu cũ thiếu mốc trả null, hiển thị “Chưa có dữ liệu”; không suy đoán hay điền lịch sử giả cho các món Task 1 đã chuyển trước migration.

## Hiển thị và demo

1. Tạo phiếu có nhiều món, đăng nhập Bếp và mở `/Kitchen`.
2. Bấm **Bắt đầu**, xác nhận. Bộ đếm “Đã chế biến” chạy tăng dần; thời gian chờ được cố định ở mốc bắt đầu.
3. Bấm **Xong**, xác nhận. Món rời hàng đợi; khu vực Chờ mang ra hiển thị thời gian chế biến thực tế cố định. Phục vụ mở `/Kitchen/Ready` để xem.
4. Kiểm tra lịch sử bằng truy vấn bên dưới. Có hai bản ghi chuyển: Pending → Preparing và Preparing → Ready. Sự kiện tạo dòng ban đầu (FromStatus NULL → Pending) của luồng gọi món được giữ riêng.

```sql
DECLARE @OrderItemId bigint = 1; -- thay bằng mã dòng vừa demo
SELECT FromStatus, ToStatus, OccurredAt, ActorUserId
FROM dbo.OrderItemEvents
WHERE OrderItemId=@OrderItemId AND FromStatus IS NOT NULL
ORDER BY OccurredAt, Id;

SELECT SubmittedAt, PreparingAt, ReadyAt,
  CASE WHEN Status='Ready' AND PreparingAt IS NOT NULL AND ReadyAt IS NOT NULL
    THEN DATEDIFF_BIG(millisecond,PreparingAt,ReadyAt) END AS ActualCookingMilliseconds
FROM dbo.OrderItems WHERE Id=@OrderItemId;
```

Bộ đếm lấy thời lượng ban đầu từ giờ máy chủ và tăng bằng đồng hồ đơn điệu của trình duyệt, không dùng giờ hệ thống thiết bị để ghi dữ liệu. Snapshot mỗi giây đồng bộ lại với máy chủ; mất kết nối vẫn hiện thông báo và thử lại. Hàng đợi món xong sắp theo ReadyAt rồi mã dòng, không còn phụ thuộc RowVersion.

Không có nút đánh dấu cả phiếu. Task 2 nối tiếp chức năng và các quy tắc chuyển tiến của Task 1.

## Kiểm tra

```powershell
node tools/tests/kitchen.test.cjs
dotnet build RestaurantManagement.sln --no-restore
dotnet run --no-build --project tests/RestaurantManagement.AreaTests
$env:RM_CONNECTION_STRING = 'Server=.\SQLEXPRESS;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-kitchen
```

Kiểm tra SQL trên database tạm riêng: hai bước cạnh tranh chỉ hai sự kiện, mỗi sự kiện khớp mốc lưu trên dòng; các thao tác từ chối không thêm sự kiện; ReadyAt > PreparingAt. Kiểm tra phép tính: chuyển nhanh 1 ms, món chậm 90 phút, chưa xong trả null, loại trừ thời gian chờ, không phụ thuộc múi giờ. Kiểm tra giao diện: bộ đếm tăng khi không lấy snapshot, thời lượng Ready dừng tăng, thao tác từng dòng và khu vực phục vụ tiếp tục hoạt động.
