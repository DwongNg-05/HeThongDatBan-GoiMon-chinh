# S2-08 Task 1 – Bật/tắt “Tạm hết” trên danh sách món trong ngày

**Mục tiêu:** Bếp và Quản lý bật/tắt trạng thái tạm hết cho món ngay trên danh sách món trong ngày; nhãn “Tạm hết” xuất hiện trên thực đơn công khai và màn hình gọi món trong tối đa 5 giây; món tạm hết không nhận order mới.

Ngoài phạm vi task này: tự đặt lại lúc 00:00 và nhật ký thao tác (task sau). Mã nguồn đã có sẵn phần nền cho hai việc này
(`007_DailyTemporaryOutReset.sql`, `TemporaryOutResetWorker`, bảng `MenuTemporaryOutEvents`) nhưng không thuộc nghiệm thu Task 1.

## Đối chiếu tiêu chí

| Tiêu chí | Cách đáp ứng |
| --- | --- |
| Chốt quyền với PO | Bảng quyền bên dưới; một nguồn trong code: `TemporaryOutRules.AllowedRoles` (Quản lý, Bếp). |
| Trạng thái tạm hết cho từng món trong danh sách món trong ngày | Cột `MenuItems.IsTemporarilyOut`; màn hình **Món trong ngày** `/Kitchen/Dishes`. |
| Thao tác một chạm cho Bếp | Nút **Tạm hết / Bán lại** trên mỗi dòng; một lần bấm là lưu, không hỏi lại, không tải lại trang. |
| Hiển thị trạng thái trên danh sách món trong ngày | Nhãn “Tạm hết” trên dòng món, đổi ngay sau khi bấm; đếm số món đang không nhận order. |
| Màn hình gọi món không nhận order món tạm hết | Nút “Thêm vào giỏ” bị khóa, món bị bỏ khỏi giỏ; máy chủ từ chối (`/Ordering/Checkout` 400, `usp_SubmitOrder` 51028). |
| Nhãn tạm hết trên thực đơn công khai | Nhãn “Tạm hết”, món làm mờ. |
| Cập nhật ≤ 5 giây | Các trang hỏi `GET /api/menu/availability` mỗi `TemporaryOutRules.PollIntervalMs` = 3 giây (không cache); API phản ánh thao tác ngay. |

## Quyền (cần PO chốt)

| Thao tác | Quản lý | Bếp | Phục vụ | Thu ngân |
| --- | --- | --- | --- | --- |
| Mở **Món trong ngày** (`/Kitchen/Dishes`) | ✔ | ✔ | ✘ | ✘ |
| Bật/tắt **Tạm hết** (một chạm) | ✔ | ✔ | ✘ | ✘ |
| **Báo hết** trong ngày ở Quản lý món (`/Dishes`, S2-01 Task 3) | ✔ | ✘ | ✘ | ✘ |

- Màn hình Món trong ngày lưu thay đổi bằng một câu lệnh kiểm tra lại vai trò trong database (Bếp/Quản lý đang hoạt động), nên Bếp bật/tắt được ngay cả khi database chưa có quyền mới. Database còn có quyền `Menu.TemporarilyOut` (Quản lý, Bếp) cho `dbo.usp_SetMenuTemporarilyOut` (migration 038). Quyền `Menu.Availability` (Báo hết) giữ nguyên **chỉ Quản lý** như migration 031/032.
- Lưu ý: migration 032 từng bỏ màn hình “Món trong ngày” của Bếp. Task này mở lại màn hình đó (chỉ cho bật/tắt Tạm hết), theo yêu cầu “Bếp và Quản lý”.

## Thay đổi

| Phần | Nội dung |
| --- | --- |
| `database/migrations/038_S208KitchenTemporaryOut.sql` | Thêm quyền `Menu.TemporarilyOut` cho Quản lý, Bếp; `usp_SetMenuTemporarilyOut` kiểm tra quyền này, chỉ nhận món đang bán và trả về tên món + trạng thái mới. |
| `Services/Menu/DailyDishStore.cs` | Đọc danh sách món trong ngày; bật/tắt Tạm hết; trả danh sách món không nhận order (tạm hết **hoặc** hết trong ngày). |
| `KitchenController` + `Views/Kitchen/Dishes.cshtml` | Màn hình **Món trong ngày**: món theo nhóm, nhãn “Tạm hết”, nút một chạm **Tạm hết / Bán lại** (gửi bằng fetch, cập nhật dòng ngay, không tải lại trang; vẫn chạy khi tắt JavaScript). Có mục trên thanh điều hướng cho Quản lý và Bếp. |
| `GET /api/menu/availability` | Không cần đăng nhập, không cache: `{ unavailable, temporarilyOut, soldOutToday, checkedAt }`. |
| `wwwroot/js/menu-availability.js` | Gọi API trên mỗi **3 giây** (và khi quay lại tab), cập nhật nhãn/khóa nút trên thực đơn công khai, màn hình gọi món và Món trong ngày ⇒ thay đổi hiển thị trong tối đa 5 giây. |
| Thực đơn công khai (`/Menu`) | Thêm/bỏ nhãn “Tạm hết” và làm mờ món ngay trên trang. |
| Màn hình gọi món (`/Ordering`) | Món tạm hết có nhãn “Tạm hết – không nhận order”, khóa ô số lượng và nút “Thêm vào giỏ”; món vừa tạm hết bị bỏ khỏi giỏ kèm thông báo. Máy chủ từ chối gọi món tạm hết (`Ordering/Checkout` trả 400; `usp_SubmitOrder` trả 51028). |

## Cài đặt

```powershell
dotnet run --project tools/RestaurantManagement.DbTool -- migrate   # phải thấy "Applied: 038_S208KitchenTemporaryOut.sql"
```

(Nếu database chưa chạy `006_TemporaryOutHistory.sql` và `007_DailyTemporaryOutReset.sql`, lệnh trên chạy luôn.)

## Demo

1. Đăng nhập `kitchen` → **Món trong ngày** → bấm **Tạm hết** ở một món: dòng món có nhãn “Tạm hết”, nút đổi thành **Bán lại**.
2. Mở `/Menu` ở trình duyệt khác (không đăng nhập): trong ≤ 5 giây món có nhãn “Tạm hết” và bị làm mờ, không cần tải lại.
3. Đăng nhập `waiter` → **Gọi món**: món có nhãn “Tạm hết – không nhận order”, nút “Thêm vào giỏ” bị khóa. Nếu món đã nằm trong giỏ, món bị bỏ ra kèm thông báo.
4. Bấm **Bán lại** (Bếp hoặc Quản lý): ≤ 5 giây sau cả hai màn hình hiển thị lại bình thường.

## Kiểm thử

| Bài kiểm thử (theo task) | Ở đâu |
| --- | --- |
| T1 Bật tạm hết một món | `KitchenCashierVerification` — Bếp bấm **Tạm hết**, database lưu, danh sách món trong ngày hiện nhãn ngay. |
| T2 Tắt tạm hết một món | Bếp bấm **Bán lại**, món nhận order lại. |
| T3 Bếp thao tác được | T1, T2 thực hiện bằng tài khoản `kitchen`. |
| T4 Quản lý thao tác được | Tài khoản `manager` bật rồi tắt trên cùng màn hình. |
| T5 Món tạm hết không nhận order mới | `/Ordering/Checkout` trả 400; `usp_SubmitOrder` trả 51028 (cả khi Quản lý bật). |
| T6 Nhãn xuất hiện ở thực đơn công khai và màn hình gọi món ≤ 5 giây | Sau mỗi thao tác: API đã phản ánh, thời gian (từ lúc máy chủ xác nhận + chu kỳ 3 giây) ≤ 5000 ms; `/Menu` và `/Ordering` có nhãn và tự cập nhật. |
| Quyền đã chốt | Phục vụ, Thu ngân bị chặn (màn hình và thao tác). `TemporaryOutTests`, `RoleNavigationTests`, `ApiAccessMatrix`. |

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet run --project tests/RestaurantManagement.AreaTests --no-build                          # TemporaryOutTests (không cần database)
dotnet run --no-build --project tools/RestaurantManagement.DbTool -- verify-api-permissions    # T1–T6 trên SQL Server + HTTP thật
```
