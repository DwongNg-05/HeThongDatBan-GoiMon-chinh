# S3-08 Task 2 — Món chờ mang ra: cảnh báo món chờ quá 5 phút

Danh sách chờ, thông báo món mới xong và xác nhận mang ra (chống trùng) là S3-08 Task 1: [S3-08-Task1.md](S3-08-Task1.md).

**Mục tiêu:** giúp Phục vụ ưu tiên mang ra trước những món đã nấu xong nhưng chờ lâu ở quầy, để món không bị nguội.

**AC hoàn tất:** danh sách chờ sắp theo thời điểm món hoàn thành, và món chờ quá 5 phút được đánh dấu nổi bật.

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Thời gian chờ tính từ lúc nào? | Từ lúc món được ghi nhận **hoàn thành**: Bếp bấm “Đã xong” trên màn hình bếp, `usp_TransitionOrderItem` ghi `OrderItems.ReadyAt = SYSUTCDATETIME()` |
| “Hiện tại” lấy theo đồng hồ nào? | Đồng hồ của database (`SYSUTCDATETIME()`), cùng nguồn với `ReadyAt`. Trình duyệt chỉ cộng thêm thời gian trôi qua kể từ lúc nhận dữ liệu, nên giờ trên máy điện thoại sai cũng không ảnh hưởng |
| Khi nào đánh dấu? | Khi đã chờ **vượt quá 5 phút** (đúng 5:00 chưa đánh dấu, từ 5:00,001 trở đi thì đánh dấu). Cùng ngưỡng với cột `ServingOverdue` của `vw_KitchenQueue` (004) |
| Đánh dấu thế nào? | Cả dòng tô đỏ (`table-danger`), viền trái đỏ, đồng hồ chờ in đậm màu đỏ, nhãn **“Chờ quá 5 phút”**; dòng vừa vượt ngưỡng nháy nhẹ hai lần. Đầu trang có bộ đếm “Chờ quá 5 phút: N món” |
| Thứ tự danh sách? | Theo thời điểm hoàn thành, **món xong trước đứng trước**; trùng thời điểm thì theo mã món. Cảnh báo **không** đẩy món lên đầu, vì món chờ lâu nhất vốn đã ở trên cùng |
| Món nào có trong danh sách? | Món đang ở trạng thái `Ready` (đã xong, chưa mang ra) của phiên chưa thanh toán. Món đã phục vụ (`Served`), đã huỷ, hoặc chưa nấu xong không có trong danh sách nên không bao giờ bị cảnh báo |
| Ai dùng? | **Phục vụ** và **Quản lý** (quyền `Orders.Serve` trong database). Bếp, Thu ngân bị chặn ở máy chủ |

Câu hỏi cho PO: ngưỡng 5 phút có cần cấu hình theo nhóm món (ví dụ đồ uống lạnh, món nướng) không? Hiện là một hằng số `ServingQueue.OverdueMinutes`.

## Cách hoạt động

1. Màn hình **Món chờ mang ra** (`/Serving`, mục mới trên thanh điều hướng của Phục vụ và Quản lý) liệt kê các món đã xong: bàn, món, số lượng, ghi chú, giờ xong (giờ Việt Nam) và đồng hồ **Đã chờ**.
2. Đồng hồ chạy mỗi giây ngay trên trang. Khi một món vượt 5 phút, dòng của món được đánh dấu **ngay tại chỗ**, không tải lại trang và không xếp lại danh sách.
3. Mỗi 3 giây trang tải lại danh sách (`GET /Serving/Queue`, JSON). Món vừa xong được thêm vào đúng vị trí theo giờ hoàn thành, món đã được người khác mang ra bị gỡ, thời gian chờ được tính lại từ `ReadyAt`. Lần tải tự động này không gia hạn phiên đăng nhập đang nhàn rỗi.
4. Bấm **Đã mang ra** (`POST /Serving/Serve/{id}`): gọi `usp_TransitionOrderItem` (`Ready → Served`, ghi `ServedAt`, `ServedBy`, sự kiện `OrderItemEvents`). Dòng biến mất ngay, bộ đếm cảnh báo giảm. Hai người cùng bấm một món thì chỉ ghi nhận một lần, người bấm sau được báo ai đã mang ra (S3-08 Task 1).
5. Không có JavaScript: trang vẫn hiển thị đúng thứ tự và cảnh báo tại thời điểm mở, nút “Đã mang ra” vẫn hoạt động (gửi form, quay lại danh sách kèm thông báo).

## Thay đổi

| Lớp | File |
| --- | --- |
| Quy tắc | `Models/Serving/ServingQueue.cs`: tính thời gian chờ, ngưỡng 5 phút, thứ tự, lọc món đã phục vụ, JSON tải lại |
| Controller | `Controllers/Serving/ServingController.cs`: `Index`, `Queue` (JSON, `[PassiveSessionRead]`), `Serve` |
| Giao diện | `Views/Serving/Index.cshtml`, `wwwroot/js/serving-queue.js`, `wwwroot/css/serving-queue.css` |
| Điều hướng, quyền | `Security/RoleNavigation.cs` (mục `serving-queue`), `tools/.../ApiAccessMatrix.cs` (3 API mới, `Manager,Waiter`) |
| Database | Không cần migration mới: dùng sẵn `ReadyAt`, `ServedAt`, `usp_TransitionOrderItem` và quyền `Orders.Serve` |

Phục vụ nay thấy 4 mục trên thanh điều hướng (Đặt bàn, Sơ đồ bàn, Gọi món, **Món chờ mang ra**); các bài kiểm thử phân quyền S1-04 đã cập nhật theo.

## Demo

1. Đăng nhập `kitchen`, mở **Màn hình bếp**, chuyển một món sang **Đang nấu** rồi **Đã xong**.
2. Đăng nhập `waiter` (trình duyệt khác), mở **Món chờ mang ra**: món hiện trong danh sách, đồng hồ bắt đầu từ `0:00`, chưa tô đỏ.
3. Chờ (hoặc để demo nhanh, lùi giờ xong trong database: `UPDATE dbo.OrderItems SET ReadyAt=DATEADD(second,-290,SYSUTCDATETIME()) WHERE Id=<mã món>;`). Khi đồng hồ qua `5:00`, dòng tự tô đỏ, có nhãn “Chờ quá 5 phút”, thứ tự không đổi.
4. Bếp xong thêm một món: trong tối đa 5 giây món mới xuất hiện ở cuối danh sách (xong sau).
5. Bấm **Đã mang ra** ở món đang cảnh báo: dòng biến mất, bộ đếm “Chờ quá 5 phút” về 0.

## Kiểm thử

| Bài | Cách chạy | Nội dung |
| --- | --- | --- |
| `ServingQueueTests` (không cần database) | `dotnet run --project tests/RestaurantManagement.AreaTests` | Chờ 4:59 / đúng 5:00 không đánh dấu; 5:01 đánh dấu; giữ thứ tự theo giờ hoàn thành khi cảnh báo thay đổi; món đã phục vụ, đã huỷ, chưa xong không có trong danh sách và không bị đếm cảnh báo; thời gian chờ tính lại đúng ở mỗi lần cập nhật (2:00 → 2:05 → 5:01); JSON tải lại; quyền Phục vụ/Quản lý |
| `serving-queue.test.cjs` (trình duyệt, không cần cài gói) | `node --test tools/tests/serving-queue.test.cjs` | Đồng hồ chạy và dòng tự đánh dấu khi vượt 5 phút mà không đổi thứ tự; tải lại thêm món mới, gỡ món đã mang ra, sắp đúng thứ tự, tính lại thời gian chờ theo máy chủ; bấm “Đã mang ra” gỡ dòng ngay và lần tải cũ không đưa món quay lại; lỗi mạng giữ danh sách; hết phiên đăng nhập thì tải lại trang |
| `KitchenCashierVerification` (web thật + SQL Server thật) | `dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions` | Bếp nấu xong → Phục vụ thấy món chưa cảnh báo; chờ 4:50 không cảnh báo, quá 5 phút được đánh dấu (JSON và trang); Bếp/Thu ngân bị chặn; “Đã mang ra” ghi `Served` đúng người, bấm lần hai không ghi thêm; món rời danh sách |
| `RoleNavigationTests`, `ApiAuthorizationCoverageTests`, `ApiAuthorizationVerification` | như trên | Mục điều hướng mới và 3 API mới có trong bảng phân quyền, đúng vai trò |
