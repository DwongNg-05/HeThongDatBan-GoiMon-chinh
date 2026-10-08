# S2-08 – Nghiệm thu story “Tạm hết” (Task 4)

Story: Bếp và Quản lý bật/tắt “Tạm hết” cho món, thay đổi hiện ngay trên các màn hình, mọi lần thay đổi có nhật ký, và mỗi ngày lúc 00:00 (Asia/Ho_Chi_Minh) món tự trở lại còn hàng.

## 4 tiêu chí chấp nhận

| # | Tiêu chí (AC của Task 1–3) | Trạng thái | Bằng chứng (kiểm thử tự động) |
| --- | --- | --- | --- |
| AC1 | Bật/tắt “Tạm hết” một thao tác (Bếp, Quản lý) | ✔ | Kịch bản bước 1, 5, 7 (`verify-temporary-out`); `KitchenCashierVerification` T1–T4 |
| AC2 | Hiển thị tạm hết trên thực đơn công khai và màn hình gọi món trong tối đa 5 giây; món tạm hết không nhận order | ✔ | Bước 1, 2, 3, 5 (đo độ trễ ≤ 5000 ms), so trạng thái đồng nhất sau mỗi bước |
| AC3 | Mỗi lần bật/tắt được ghi nhật ký kèm thời điểm và người thao tác | ✔ | Bước 4, 7, 8 (6 lần liên tiếp, đúng thứ tự, đúng người); trang **Lịch sử tạm hết** |
| AC4 | Toàn bộ trạng thái tạm hết tự đặt lại thành còn hàng lúc 00:00 mỗi ngày theo Asia/Ho_Chi_Minh | ✔ | Bước 9, 10 (nhiều món cùng lúc, nhận order lại); `TemporaryOutResetWorkerTests` (đồng hồ giả lập chạy qua 00:00 nhiều ngày) |

## Checklist Task 4

| Kiểm tra | Cách kiểm |
| --- | --- |
| Luồng bật tạm hết: Món trong ngày → thực đơn công khai → màn hình gọi món | Bước 1–3 |
| Luồng tắt tạm hết và nhận order trở lại | Bước 5–6 (màn hình gọi món + `usp_SubmitOrder`) |
| Quyền của Bếp và Quản lý trên toàn luồng | Bếp, Quản lý bật/tắt được (bước 1, 5, 7); Phục vụ, Thu ngân bị chặn ở màn hình, thao tác và nhật ký; chỉ Quản lý xem nhật ký (bước 8) |
| Trạng thái đồng nhất giữa các màn hình | Sau mỗi bước: database = API trạng thái = Món trong ngày = thực đơn công khai = màn hình gọi món, cho **mọi** món. Quản lý món (`/Dishes`) cũng hiện nhãn “Tạm hết (Món trong ngày)”. |
| Nhật ký sau nhiều lần bật/tắt liên tiếp | Bước 7: 6 lần xen kẽ Bếp/Quản lý → 6 dòng, đúng thứ tự, đúng người, thời điểm không giảm; trang lịch sử mới nhất trước |
| Tự đặt lại nhiều món cùng lúc | Bước 9: 3 món tạm hết trước 00:00 → cả 3 về còn hàng, mỗi món một dòng nhật ký “Hệ thống”; món đang còn hàng không bị đụng |
| Trạng thái sau khi hệ thống chạy qua 00:00 | `TemporaryOutResetWorkerTests`: tác vụ chạy liên tục từ 21:00 → không làm gì lúc 23:59:59 → chạy đúng 00:00 → không chạy thêm trong ngày → chạy lại 00:00 hôm sau; lỗi database thì thử lại sau 1 phút. Bước 9: web khởi động lại sau nửa đêm vẫn chạy bù đúng mốc. |
| Thời gian phản ánh ≤ 5 giây | Bước 1, 5: thời gian tới khi API thấy thay đổi + chu kỳ hỏi lại 3 giây ≤ 5000 ms |

## Sửa trong Task 4

- **Màn hình Gọi món lưu được đơn trên SQL Server:** `041_S208OrderingCartTables.sql` tạo `DonHangs`/`DongHangs` (trước đây chưa có bảng nên “Gọi món” lỗi — không kiểm được “nhận order trở lại”).
- **Đồng nhất trạng thái ở Quản lý món:** `/Dishes` hiện nhãn “Tạm hết (Món trong ngày)” cho món đang tạm hết (trước đây vẫn ghi “Còn món”).
- **Tác vụ 00:00 kiểm thử được:** `TemporaryOutResetWorker` nhận đồng hồ (`TimeProvider`) — kiểm thử chạy qua nhiều nửa đêm trong vài giây.

## Kịch bản demo (≈ 5 phút)

1. `kitchen` → **Món trong ngày** → bấm **Tạm hết** ở “Pepsi”. Mở `/Menu` (trình duyệt khác, không đăng nhập) và **Gọi món** (`waiter`): trong ≤ 5 giây “Pepsi” có nhãn “Tạm hết”, nút “Thêm vào giỏ” bị khoá.
2. `waiter` cố gọi “Pepsi” (món đã có trong giỏ trước đó): bị bỏ khỏi giỏ / máy chủ từ chối.
3. `manager` → **Món trong ngày** → **Lịch sử** của “Pepsi”: dòng “Bật tạm hết – (kitchen) · Bếp – giờ”.
4. `kitchen` bấm **Bán lại**: ≤ 5 giây các màn hình trở lại bình thường, `waiter` gọi “Pepsi” được.
5. Qua 00:00: bật **Tạm hết** vài món, rồi giả lập nửa đêm (không cần chờ):
   ```sql
   DECLARE @m datetime2(3)=DATEADD(hour,-7,CONVERT(datetime2(3),CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))));
   UPDATE dbo.MenuTemporaryOutEvents SET ChangedAt=DATEADD(hour,-2,@m) WHERE ChangedAt>=@m AND IsTemporarilyOut=1;
   DELETE dbo.ScheduledJobRuns WHERE JobName='TemporaryOutReset' AND ScheduledFor=@m;
   ```
   Khởi động lại web → các món tự về còn hàng, gọi món được; **Lịch sử** có dòng “Hệ thống – tự đặt lại 00:00”.

## Chạy nghiệm thu

```powershell
dotnet run --project tools/RestaurantManagement.DbTool -- migrate
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build                         # TemporaryOutTests, TemporaryOutResetWorkerTests
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-temporary-out     # kịch bản đầy đủ + tự đặt lại 00:00 (database tạm)
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions   # phân quyền toàn bộ API + T1–T6, nhật ký
```

Tài liệu từng task: `S2-08-Task1.md`, `S2-08-Task2.md`, `S2-08-Task3.md`.
