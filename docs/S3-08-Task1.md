# S3-08 Task 1: Món vừa xong hiện ở danh sách chờ, Phục vụ xác nhận đã mang ra

**Mục tiêu:** Phục vụ thấy ngay món vừa nấu xong và món đó thuộc bàn nào, rồi xác nhận đã mang ra, để không có hai người cùng mang một món.

**AC hoàn tất:** món mới xong được thông báo trong 5 giây; xác nhận chuyển món sang Đã phục vụ và gỡ món khỏi danh sách chờ; hai người xác nhận trùng thì chỉ một người được ghi nhận.

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Món **hoàn thành** lúc nào? | Lúc Bếp bấm **Đã xong** trên Màn hình bếp. `usp_TransitionOrderItem` chuyển món `Preparing → Ready` và ghi `OrderItems.ReadyAt = SYSUTCDATETIME()` (giờ database). Đây là mốc duy nhất dùng cho thứ tự, thời gian chờ và thông báo |
| Thông báo món mới xong ghi gì? | **Mã bàn** và **tên món**, thêm số lượng nếu từ 2 trở lên: “Món mới xong: Bàn A05 · Phở bò × 2”. Thông báo hiện phía trên danh sách, tự ẩn sau 15 giây hoặc khi món đã được mang ra. Dòng của món mới được tô xanh nhạt trong 15 giây |
| Danh sách chờ có những gì? | Món `Ready` chưa phục vụ, của bàn chưa thanh toán. Mỗi dòng gồm mã bàn, tên món, số lượng, ghi chú, giờ xong (giờ Việt Nam) và thời gian đã chờ |
| Thứ tự? | Theo giờ xong, **sớm trước, muộn sau**. Trùng giờ thì theo mã món |
| “Trong 5 giây” được bảo đảm thế nào? | Trang tải lại danh sách mỗi **3 giây** (`ServingQueue.RefreshIntervalMs`), còn dư 2 giây cho mạng và database. Mở lại tab cũng tải ngay |
| Ai xác nhận được? | Phục vụ và Quản lý (quyền `Orders.Serve`). Bếp, Thu ngân bị chặn ở máy chủ và trong thủ tục SQL |
| Hai người cùng xác nhận? | `usp_TransitionOrderItem` lấy khoá độc quyền (`sp_getapplock`) rồi mới đọc trạng thái món, nên chỉ một lần chuyển `Ready → Served` được ghi. Người thành công nhận “Đã mang ra: Bàn A05 · Phở bò.”. Người đến sau nhận **409** “Bàn A05 · Phở bò đã được *Nguyễn An* mang ra lúc 18:01. Không cần mang lại.”, và dòng cũng được gỡ khỏi màn hình của họ. Tự bấm lại lần hai thì nhận “Bạn đã xác nhận mang ra … rồi.” |

Câu hỏi cho PO: có cần âm báo hay rung khi có món mới xong không? Hiện chỉ có thông báo trên màn hình, vì máy dùng chung ở quầy có thể đang tắt tiếng.

## Cách hoạt động

1. Bếp bấm **Đã xong** → món `Ready`, có `ReadyAt`.
2. Màn hình **Món chờ mang ra** (`/Serving`) gọi `GET /Serving/Queue` mỗi 3 giây. Món chưa có trên trang được thêm vào đúng vị trí, kèm thông báo mã bàn và tên món.
3. Phục vụ bấm **Đã mang ra** (`POST /Serving/Serve/{id}`): món chuyển sang `Served` (ghi `ServedAt`, `ServedBy`, một sự kiện `OrderItemEvents`), dòng biến mất ngay. Các máy khác gỡ dòng ở lần tải kế tiếp.
4. Nếu người khác đã mang ra trước: máy chủ trả 409 kèm tên người đó và giờ mang ra. Thông báo màu vàng, dòng được gỡ.

## Thay đổi

| Lớp | File |
| --- | --- |
| Quy tắc | `Models/Serving/ServingQueue.cs`: `NotifyWithinMs` = 5000, `RefreshIntervalMs` = 3000, `ServeOutcomeFor` (thành công / đã có người mang ra) |
| Controller | `ServingController.Serve`: trả 200 khi ghi nhận được, 409 `alreadyServed` kèm tên người đã mang ra |
| Giao diện | `Views/Serving/Index.cshtml` (vùng thông báo), `wwwroot/js/serving-queue.js` (thông báo món mới, cảnh báo đã có người mang ra), `serving-queue.css` |
| Database | Không cần migration mới |

## Demo

1. Đăng nhập `waiter` và mở **Món chờ mang ra**. Ở trình duyệt khác, đăng nhập `manager` và cũng mở màn hình này.
2. Đăng nhập `kitchen`, chuyển một món sang **Đang nấu** rồi **Đã xong**. Trong vòng 3 giây, cả hai màn hình hiện “Món mới xong: Bàn … · …” và dòng món tô xanh.
3. `waiter` bấm **Đã mang ra**: dòng biến mất, hiện “Đã mang ra: Bàn … · ….”. Màn hình của `manager` cũng gỡ dòng ở lần tải kế tiếp.
4. Trùng xác nhận: lặp bước 2, rồi cả hai bấm **Đã mang ra** gần như cùng lúc (hoặc `manager` bấm trước khi trang của mình kịp tải lại). Một người nhận “Đã mang ra”, người kia nhận thông báo vàng “… đã được … mang ra lúc …. Không cần mang lại.”.

## Kiểm thử

| Bài | Cách chạy | Nội dung |
| --- | --- | --- |
| `ServingQueueTests` (không cần database) | `dotnet run --project tests/RestaurantManagement.AreaTests` | Danh sách có mã bàn, tên món, giờ xong, sắp sớm → muộn; chu kỳ tải lại 3 s, nhỏ hơn 5 s; dữ liệu tải lại có mã bàn và tên món; nội dung thông báo khi thành công, khi người khác đã mang ra, khi tự bấm lại |
| `serving-queue.test.cjs` (cần Node.js) | `node --test tools/tests/serving-queue.test.cjs` | Món mới hiện ở lần tải 3 giây kế tiếp, kèm thông báo đủ mã bàn và tên món, không thông báo lại; nhiều món mới theo đúng thứ tự; món được người khác mang ra thì gỡ cả dòng lẫn thông báo; 409 “đã có người mang ra” gỡ dòng và hiện cảnh báo vàng |
| `KitchenCashierVerification` (web thật + SQL Server thật) | `verify-api-permissions` (cần `RM_CONNECTION_STRING`) | Bếp bấm Đã xong → API danh sách chờ có món đúng mã bàn, tên món, và thời gian từ lúc xong cộng chu kỳ tải lại ≤ 5 s; **Phục vụ và Quản lý xác nhận đồng thời** → đúng một người 200, người kia 409 kèm tên người thắng; tự bấm lại nhận “Bạn đã xác nhận…”; database ghi `Served` đúng người, đúng một sự kiện; món rời danh sách chờ |
