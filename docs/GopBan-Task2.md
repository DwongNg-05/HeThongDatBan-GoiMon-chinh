# Gộp bàn — Task 2: nhãn nhóm trên sơ đồ (AC 3)

## Phạm vi

Nhân viên nhận ra các bàn cùng nhóm ngay trên sơ đồ. Giữ luồng gộp, bàn gốc của món, gọi thêm và một hoá đơn của Task 1 (AC 1, AC 2). Không thêm thao tác huỷ gộp.

## Đối chiếu yêu cầu

| Yêu cầu | Thực hiện |
|---|---|
| Tạo và lưu nhãn duy nhất khi gộp | `DiningSessions.TableGroupLabel` lưu ở phiên chính, dạng `N-<mã phiên chính>`. Trigger `tr_SessionMerges_GroupLabel` tạo nhãn trong chính giao dịch gộp. Chỉ mục unique chặn nhãn trùng; không tạo nhãn cho giao dịch bị từ chối/rollback. |
| Trả nhãn và danh sách thành viên khi tải sơ đồ | Cả API trang chính `/api/table-status` và snapshot/polling `/api/table-map` trả trường `group`: `label`, `mainSessionId`, `mainTableCode`, `memberTableCodes`, `status`. Cùng đọc `vw_ActiveTableGroups`. |
| Cùng nhãn trên hai bàn | Thẻ bàn hiển thị badge “Nhóm N-…”, cả HTML lúc tải trang lẫn cập nhật tự động. Các nhóm khác nhau có nhãn khác nhau. |
| Chọn bàn thấy thành viên và bàn chính | Chọn một bàn đang phục vụ/chờ thanh toán mở màn hình gọi món với khối thông tin nhóm: nhãn, các mã bàn thành viên, bàn giữ phiên chính, mã phiên chính và trạng thái. API chi tiết và hộp chi tiết cũng có cùng dữ liệu. |
| Mở đúng phiên chính và giữ bàn gốc | Sơ đồ đưa mã phiên chính vào liên kết kiểm tra phiên hiện tại. Món chung và hoá đơn đọc từ phiên chính; bàn đang được mở vẫn là bàn gốc của món mới theo quy tắc đã chốt ở Task 1. |
| Cập nhật sau gộp/tải lại/chờ thanh toán | Nhãn được lưu trong database nên giữ nguyên khi tải lại. Polling 2 giây cập nhật cả trường hợp trạng thái vẫn là Serving nhưng vừa xuất hiện nhóm. Khi chờ thanh toán, giữ nhãn và cập nhật trạng thái nhóm. |
| Thanh toán xong không còn nhãn phục vụ | View chỉ trả nhóm khi phiên còn mở/chờ thanh toán và bàn còn được gán. Checkout đóng phiên và giải phóng bàn nên nhãn, danh sách thành viên và liên kết phiên chính biến mất khỏi dữ liệu sơ đồ và badge bị ẩn. Nhãn lịch sử vẫn được lưu trong phiên đã đóng, không tái sử dụng cho phiên mới. |

## Dữ liệu và cập nhật

Migration mới `060_TableGroupLabels.sql` bổ sung cột nhãn, unique index, trigger và view đọc nhóm đang hoạt động. Bổ sung nhãn cho các nhóm đã tồn tại từ Task 1. Không đổi dòng món, giá, số lượng, bàn gốc hay hoá đơn. Không sửa các migration Task 1 đã áp dụng.

Đã áp dụng migration 060 vào `RestaurantManagement_Dev` trên `lpc:.\SQLEXPRESS` ngày 15/10/2026. Có thể kiểm tra bằng `migration-status` của DbTool. Các màn hình phụ thuộc kiểm tra thêm cột nhãn và view mới trước khi truy vấn để tránh lỗi thiếu cấu trúc khi triển khai lệch phiên bản.

## Demo

1. Chuẩn bị hai bàn đã cấu hình liền nhau, cùng khu vực, đang phục vụ trong cùng ca.
2. Gộp bằng giao diện phục vụ và chọn bàn giữ phiên chính như Task 1.
3. Trở về sơ đồ: cả hai bàn hiển thị cùng badge “Nhóm N-…”. Sơ đồ đã mở trên máy khác nhận nhãn ở lượt đồng bộ tiếp theo, kể cả khi cả hai bàn vẫn giữ trạng thái Đang phục vụ.
4. Tải lại trang: hai bàn vẫn có đúng nhãn đó.
5. Chọn lần lượt từng bàn: thấy cùng danh sách thành viên, cùng bàn chính và cùng phiên thanh toán chính; vẫn xem và gọi thêm món theo AC 1, AC 2.
6. Có nhóm thứ hai: nhãn của hai nhóm khác nhau.
7. Yêu cầu thanh toán: giữ nhãn với trạng thái Chờ thanh toán. Thu ngân thanh toán một hoá đơn; sơ đồ đưa cả hai bàn sang Đang dọn và gỡ nhãn nhóm phục vụ, nhóm khác vẫn giữ nhãn.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet tests/RestaurantManagement.AreaTests/bin/Debug/net10.0/RestaurantManagement.AreaTests.dll
dotnet tools/RestaurantManagement.DbTool/bin/Debug/net10.0/RestaurantManagement.DbTool.dll verify-merge-tables
dotnet tools/RestaurantManagement.DbTool/bin/Debug/net10.0/RestaurantManagement.DbTool.dll verify-merge-upgrade
node tests/table-map-group-sync.test.cjs
node tests/home-table-group-sync.test.cjs
```

- Hồi quy hiện có: 799 kiểm thử đạt.
- SQL Server và HTTP thật: hai bàn cùng nhãn; danh sách thành viên đầy đủ; HTML sơ đồ có badge trên đúng hai bàn; tải lại nhãn không đổi; snapshot và polling đều trả nhóm; mở từng bàn nhận đúng phiên chính; nhãn giữ khi chờ thanh toán; biến mất sau thanh toán; hai nhóm đang mở không trùng nhãn; thanh toán nhóm này không ảnh hưởng nhóm kia. Chạy cùng toàn bộ nghiệm thu Task 1 (đúng món/bàn gốc/tổng, không nhân đôi, xử lý yêu cầu đồng thời, một hoá đơn).
- Nâng cấp schema cũ: các nhóm đã gộp nhận nhãn chung, giữ nguyên dữ liệu món, bếp và màn hình gọi món vẫn hoạt động.
- JavaScript chạy mã đồng bộ thật của cả hai sơ đồ với DOM adapter: cập nhật badge khi chỉ thay đổi nhóm; không phát sự kiện dư khi polling dữ liệu không đổi; giữ nhãn lúc chờ thanh toán; ẩn badge và xoá liên kết phiên cũ sau checkout; hộp chi tiết trên trang chính hiển thị thành viên và giữ đúng bàn đang chọn khi mở phiên chính; mở lại bàn sau chờ thanh toán không bị kẹt bởi mốc thời gian cũ.
- Build còn cảnh báo giấy phép ImageSharp đã có từ trước; không tắt kiểm tra bảo mật NuGet.
