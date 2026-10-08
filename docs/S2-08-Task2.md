# S2-08 Task 2 – Nhật ký bật/tắt “Tạm hết”

**Mục tiêu:** ghi nhận đầy đủ lịch sử mỗi lần bật hoặc tắt trạng thái tạm hết. Phần tự đặt lại lúc 00:00 chưa thuộc task này.

## Đối chiếu tiêu chí

| Tiêu chí | Cách đáp ứng |
| --- | --- |
| Tạo nhật ký cho mỗi lần thay đổi | Mỗi lần trạng thái **thật sự đổi** ghi đúng một dòng vào `dbo.MenuTemporaryOutEvents`, trong **cùng giao dịch** với thay đổi (không thể đổi mà thiếu nhật ký). Bấm lại khi trạng thái không đổi thì không ghi. |
| Người thực hiện, món, trạng thái trước, trạng thái sau, thời điểm | `ChangedBy`, `MenuItemId`, `OldIsTemporarilyOut`, `IsTemporarilyOut`, `ChangedAt` (UTC, cùng giá trị với `MenuItems.UpdatedAt`). |
| Bếp bật / Bếp tắt / Quản lý bật hoặc tắt | Cả ba cùng đi qua nút một chạm ở **Món trong ngày** (`DailyDishStore.SetTemporarilyOut`). API cũ của Quản lý (`POST /menu/{id}/temporarily-out`, `usp_SetMenuTemporarilyOut`) cũng ghi nhật ký. |
| Hiển thị lịch sử cho người có quyền quản lý | Trang **Lịch sử tạm hết** `/Kitchen/Dishes/History` (chỉ Quản lý): thời điểm (giờ Việt Nam), món, thao tác, trạng thái trước → sau, người thực hiện (họ tên, tên đăng nhập, vai trò); mới nhất ở trên; lọc theo món. Mở từ **Món trong ngày** (link “Xem lịch sử tạm hết” và “Lịch sử” trên từng món — chỉ Quản lý thấy). |
| Nhật ký không bị sửa | Migration `039_S208TemporaryOutLog.sql`: tài khoản ứng dụng chỉ được đọc và ghi thêm (`DENY UPDATE, DELETE`); ràng buộc trạng thái trước ≠ trạng thái sau. |

## Cài đặt

```powershell
dotnet run --project tools/RestaurantManagement.DbTool -- migrate   # phải thấy "Applied: 039_S208TemporaryOutLog.sql"
```

## Demo

1. Đăng nhập `kitchen` → **Món trong ngày** → bấm **Tạm hết** ở một món, rồi **Bán lại**.
2. Đăng nhập `manager` → **Món trong ngày** → bấm **Tạm hết** rồi **Bán lại** ở cùng món.
3. Bấm **Lịch sử** ở món đó: thấy 4 dòng (mới nhất trên cùng) — Quản lý tắt, Quản lý bật, Bếp tắt, Bếp bật — mỗi dòng có người thao tác và thời điểm.

## Kiểm thử

| Bài kiểm thử (theo task) | Ở đâu |
| --- | --- |
| Bật tạm hết tạo đúng một nhật ký | `KitchenCashierVerification`: số dòng nhật ký tăng đúng 1 sau mỗi lần bật; bấm lại khi đã tạm hết không tăng. |
| Tắt tạm hết tạo đúng một nhật ký | Số dòng tăng đúng 1 sau mỗi lần tắt. |
| Đúng người thao tác | `ChangedBy` = tài khoản `kitchen` (Bếp) hoặc `manager` (Quản lý) đã bấm. |
| Thời điểm được ghi nhận | `ChangedAt` nằm giữa thời điểm trước và sau khi bấm (giờ máy chủ database) và bằng `MenuItems.UpdatedAt`. |
| Nhiều lần bật/tắt đúng thứ tự | 4 lần liên tiếp ghi theo thứ tự `kitchen 0→1, kitchen 1→0, manager 0→1, manager 1→0`, thời điểm không giảm; trang lịch sử hiển thị mới nhất trước. |
| Quyền xem | Chỉ Quản lý mở được lịch sử (Bếp bị chặn); thao tác bị chặn của Phục vụ/Thu ngân không ghi nhật ký. `TemporaryOutTests` (không cần database) kiểm tra quyền và nhãn hiển thị. |

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions
```
