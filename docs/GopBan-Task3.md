# Gộp bàn — Task 3: huỷ gộp trước thanh toán (AC 4)

## Quy tắc đã chốt

Người dùng xác nhận các quy tắc sau làm nghiệp vụ chính thức ngày 15/10/2026:

- Cho phép huỷ nhóm đúng hai bàn đang phục vụ hoặc chờ thanh toán, miễn nhóm chưa thanh toán.
- Huỷ yêu cầu thanh toán chung và đưa cả hai bàn về **Đang phục vụ** trong các phiên riêng ban đầu. Mã phiên của từng bàn không thay đổi.
- Mỗi dòng món, kể cả món gọi thêm sau khi gộp, được trả về phiên gắn với `OriginalTableId` của dòng đó. Giữ ID dòng, số lượng, đơn giá, ghi chú, trạng thái bếp/phục vụ và thời điểm gửi.
- Hoá đơn chưa trả trong ứng dụng hiện tại là **hoá đơn tạm tính** đọc trực tiếp từ các dòng món; sau huỷ, tạm tính của từng bàn được đọc và cộng lại riêng. `Invoices` lưu hoá đơn đã thanh toán/đã huỷ hoá đơn; không có hoá đơn chờ trả cần xoá hay sửa trong bảng này. Không thay đổi hoá đơn đã thanh toán.
- Chỉ hỗ trợ nhóm hai bàn; gộp hơn hai bàn nằm ngoài story.

## Đối chiếu yêu cầu

| Yêu cầu | Thực hiện |
|---|---|
| Thao tác và màn hình xác nhận huỷ gộp | Nút trên khối nhóm trong màn hình gọi món và hộp chi tiết nhóm ở sơ đồ trang chính. Trang `/Ordering/Unmerge` liệt kê cả hai bàn, mã phiên riêng và tạm tính theo bàn gốc; cần tích xác nhận. |
| Kiểm tra chưa thanh toán ngay khi thực hiện | `usp_UnmergeSession` lấy khoá nghiệp vụ dùng chung với gọi món và thanh toán; kiểm tra lại trạng thái, hoá đơn, ca, hai bàn đang gắn phiên và phiên bản của nhóm trước khi ghi. |
| Khôi phục phiên và món theo bàn gốc | Giữ hai phiên cũ, bỏ `BillingSessionId`; đối chiếu từng dòng với bàn gốc. Đợt chỉ có một bàn gốc được chuyển nguyên đợt; đợt lịch sử chứa nhiều bàn gốc được tách thành các phần mà không sao chép dòng món. `SplitFromBatchId` giữ quan hệ đợt gốc để gửi lại yêu cầu/đọc biên nhận không tạo món trùng hoặc mất dòng biên nhận. |
| Hoá đơn/tổng từng bàn và trạng thái sau huỷ | Đưa cả hai phiên về `Open`, xoá `PaymentRequestedAt`; các màn hình gọi món, tạm tính và thu ngân đọc lại món/tổng riêng theo phiên. Món huỷ không tính tiền được loại khỏi tổng, món huỷ có tính tiền giữ theo quy tắc hiện hành. |
| Xoá nhóm và nhãn sơ đồ | Xoá liên kết thanh toán, đặt nhãn nhóm về NULL, đánh dấu bản ghi gộp đã huỷ kèm người/thời điểm. Giữ lịch sử cho kiểm toán. View nhóm đang hoạt động không còn trả nhóm đó; cả hai sơ đồ và hộp chi tiết được cập nhật qua polling. |
| Không để huỷ gộp và thanh toán cùng thành công | Chung khoá giao dịch. Form thu ngân mang phiên bản phiên đã hiển thị; sau huỷ, phiên bản đổi nên thanh toán cũ bị từ chối và tải lại danh sách/tổng mới. Câu lệnh thanh toán không mang phiên bản cũng bị từ chối với phiên từng huỷ gộp, tránh coi yêu cầu nhóm cũ là thanh toán riêng bàn chính. Thanh toán thành công trước thì huỷ gộp bị từ chối vì nhóm đã đóng. |
| Huỷ trùng / dữ liệu thay đổi / gộp lại | Xác nhận gắn ID lần gộp và phiên bản hai phiên. Huỷ trùng hoặc xác nhận cũ sau gộp lại đều bị từ chối. Gộp lại được khi hai bàn đủ điều kiện, nhãn mới `N-G<Id lần gộp>` không tái sử dụng nhãn cũ. |
| Phân quyền | Chỉ Quản lý/Phục vụ; Razor Page chặn người ngoài quyền và kiểm tra chống giả mạo. SQL kiểm tra `Sessions.Manage` lần nữa. Thu ngân chỉ được thanh toán, không dùng thao tác huỷ gộp. |

## Cập nhật cơ sở dữ liệu

Migration mới: `061_UnmergeTables.sql`. Thêm nguồn phần đợt gọi món, cập nhật thủ tục huỷ gộp, thủ tục thanh toán và cách cấp nhãn khi gộp lại. Không sửa các migration Task 1–2 đã áp dụng. Bản cập nhật không tự huỷ bất kỳ nhóm đang phục vụ nào; chỉ thay đổi dữ liệu nhóm khi nhân viên xác nhận thao tác.

Đã áp dụng migration 061 vào `RestaurantManagement_Dev` trên `lpc:.\SQLEXPRESS`. Dùng `migration-status` để xác nhận `OrderBatches.SplitFromBatchId` có mặt và không còn migration thiếu. Ứng dụng kiểm tra cấu trúc mới trước khi mở các chức năng phụ thuộc.

## Demo

1. Gộp hai bàn hợp lệ, gọi món từ từng bàn; gọi thêm từ cả hai sau khi gộp. Kiểm tra nhãn và món chung như Task 1–2.
2. Chọn một bàn trong nhóm trên sơ đồ → **Huỷ gộp nhóm bàn**. Hoặc mở màn hình gọi món của bàn và chọn cùng nút.
3. Kiểm tra hai bàn, mã phiên, tạm tính theo bàn gốc. Tích xác nhận và bấm **Xác nhận huỷ gộp**.
4. Mở lần lượt từng bàn: không còn nhóm, phiên riêng trở lại, mỗi bàn có đúng các món mang bàn gốc của mình, kể cả món gọi thêm; giá và số lượng giữ nguyên.
5. Trở về sơ đồ: cả hai bàn vẫn Đang phục vụ nhưng không còn badge nhóm. Mở hoá đơn tạm tính từng bàn để kiểm tra tổng riêng.
6. Lặp lại với nhóm Chờ thanh toán: sau huỷ, cả hai bàn trở về Đang phục vụ, yêu cầu thanh toán chung bị huỷ. Thu ngân tải lại để thấy hai phiên riêng; form nhóm mở trước khi huỷ không thanh toán được.
7. Gộp lại hai bàn: tạo nhóm và nhãn mới, tổng món không đổi. Thử huỷ cùng lúc thu ngân thanh toán: chỉ một thao tác thành công, bên còn lại nhận thông báo tải lại theo trạng thái mới nhất.

## Kiểm thử

```powershell
dotnet build RestaurantManagement.sln --no-restore -m:1
dotnet tests/RestaurantManagement.AreaTests/bin/Debug/net10.0/RestaurantManagement.AreaTests.dll
dotnet tools/RestaurantManagement.DbTool/bin/Debug/net10.0/RestaurantManagement.DbTool.dll verify-unmerge-tables
dotnet tools/RestaurantManagement.DbTool/bin/Debug/net10.0/RestaurantManagement.DbTool.dll verify-merge-tables
dotnet tools/RestaurantManagement.DbTool/bin/Debug/net10.0/RestaurantManagement.DbTool.dll verify-merge-upgrade
node tests/home-table-group-sync.test.cjs
node tests/table-map-group-sync.test.cjs
```

Các bộ SQL/HTTP tạo database thử riêng có tên duy nhất và xoá sau kiểm thử. Bao phủ huỷ trước thanh toán ở hai trạng thái; màn hình xác nhận, quyền, chống giả mạo và xác nhận bắt buộc; bàn gốc của món cũ/món thêm và đợt trộn bàn gốc; giữ dữ liệu món đang chế biến/đã huỷ; tổng riêng; gỡ nhãn và quan hệ; huỷ trùng; xác nhận cũ khi đổi trạng thái hoặc gộp lại; huỷ trước/thanh toán trước và hai yêu cầu chạy đồng thời; form thu ngân cũ; thanh toán riêng sau tách và gửi lại không nhân đôi hoá đơn. Chạy lại nghiệm thu Task 1–2 và nâng cấp database cũ để bảo đảm AC 1, AC 2, AC 3 vẫn đạt.

Build vẫn có cảnh báo giấy phép ImageSharp có sẵn; kiểm tra bảo mật và chế độ coi cảnh báo NuGet là lỗi vẫn bật.

Kết quả cuối: build 0 lỗi; 799 kiểm thử hồi quy đạt; bộ nghiệm thu huỷ gộp, bộ Task 1–2 và bộ nâng cấp dữ liệu cũ đều đạt; kiểm thử JavaScript của cả hai sơ đồ đạt, gồm huỷ/gộp lại khi trạng thái bàn vẫn Đang phục vụ. Database sử dụng đã áp dụng migration 061, không tự tạo dữ liệu demo hoặc tự huỷ nhóm hiện có.
