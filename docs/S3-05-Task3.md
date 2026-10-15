# S3-05 Task 3: Quản lý huỷ món đã bắt đầu chế biến, giữ nguyên tiền món

Quản lý huỷ một món bếp đã bắt đầu làm. Món vẫn tính tiền và dòng món giữ trên hoá đơn với nhãn **“Đã huỷ có tính tiền”**. Bếp nhận thông báo dừng món trong vòng 5 giây. Báo cáo cuối ca ghi đủ thông tin.

**Hoàn tất:** AC 5. AC 1–4 giữ nguyên: Phục vụ huỷ món chờ bếp không tính tiền, tạm tính, màn hình bếp, chống gửi lặp, nhật ký và báo cáo cuối ca. `verify-cancel-item` và `verify-cancel-report` vẫn chạy. Hai bộ kiểm thử này đã được cập nhật để Quản lý huỷ có tính tiền theo quy tắc mới (một trong ba lý do, kèm ô xác nhận).

## Quy tắc (đề xuất, cần PO chốt)

| Câu hỏi | Đề xuất (đã cài đặt) |
| --- | --- |
| Trạng thái nào sau khi bếp bắt đầu còn được huỷ? | **Đang nấu** và **Đã xong, chờ mang ra**. **Đã mang ra** thì **không huỷ được nữa** (51143), vì khách đã nhận món; nếu cần, giảm giá khi thanh toán. *Thay đổi so với trước:* bản cũ cho Quản lý huỷ cả món đã mang ra. |
| Ai huỷ được? | Chỉ **Quản lý** (quyền `Orders.CancelPrepared`). Phục vụ bị từ chối (51035). Thu ngân và Bếp không có thao tác huỷ. |
| Lý do | Bắt buộc, không chọn sẵn, đúng **ba lý do**: Khách đổi ý, Gọi nhầm món, Hết nguyên liệu. “Quản lý yêu cầu” bị ẩn khi huỷ có tính tiền và máy chủ từ chối nếu gửi thẳng (51142). Lý do này vẫn dùng được khi Quản lý huỷ món còn chờ bếp (Task 1). |
| Xác nhận | Quản lý phải đánh dấu ô **“Tôi xác nhận hủy có tính tiền…”** (ô không đánh dấu sẵn). Thiếu xác nhận thì nút bị tắt, và máy chủ cũng từ chối (51144). |
| Tiền | Món **vẫn tính tiền**. Dòng giữ trên hoá đơn tạm tính (vẫn hiện số tiền), tạm tính không giảm, và hoá đơn khi thanh toán vẫn tính dòng đó (`InvoiceLines.IsChargedCancellation = 1`). |
| Trạng thái đổi trong lúc chọn lý do | Từ chối (51034). Hoá đơn tải lại với trạng thái mới, Quản lý mở lại hộp và xác nhận lại. Thao tác huỷ và thao tác của bếp dùng chung khoá, nên không bao giờ cùng thành công một lúc. |
| Thông báo dừng món trên màn hình bếp | Món rời hàng đợi chế biến. Một **thẻ đỏ “DỪNG MÓN”** hiện ở đầu màn hình bếp ở lần tải lại kế tiếp (mỗi 3 giây, tức trong vòng 5 giây). Thẻ ghi bàn, đợt, món, số lượng, trạng thái lúc huỷ, lý do, ghi chú, người huỷ và giờ huỷ. Việc cần làm: “Dừng chế biến món này.” (đang nấu) hoặc “Không mang món này ra bàn.” (đã xong). Thẻ ở lại tới khi tài khoản Bếp bấm **Đã dừng món** (ghi một lần; bấm lặp không ghi thêm). Thẻ chưa xác nhận tự ẩn sau 12 giờ. |
| Xác nhận lặp | Không ghi thêm, không đổi tiền, báo “đã được hủy trước đó”. |
| Nhật ký và báo cáo | Mỗi lần huỷ ghi một dòng `OrderItemEvents` (trạng thái lúc huỷ là Đang nấu hoặc Xong; có người huỷ, giờ, lý do và ghi chú). Báo cáo cuối ca (Task 2) hiện nhãn **“Đã huỷ có tính tiền”**, cột tính tiền ghi “Vẫn tính tiền … (đang nấu / đã làm xong)”. Yêu cầu bị từ chối (51142, 51143, 51144…) cũng vào nhật ký từ chối của ca. |

## Cách hoạt động

- **Migration `058_S305ChargedCancellation.sql`**:
  - `usp_CancelOrderItem` có thêm `@ConfirmCharged`. Các kiểm tra chạy theo thứ tự: trạng thái mới nhất (51034), Phục vụ (51035), món đã mang ra (51143), lý do huỷ có tính tiền (51142), ô xác nhận (51144).
  - Bảng `KitchenStopAcknowledgements` lưu lần Bếp xác nhận đã dừng món.
  - View `vw_KitchenStopNotices` liệt kê các thẻ dừng món đang chờ.
  - Thủ tục `usp_AcknowledgeKitchenStop`: chỉ quyền `Kitchen.Manage`; báo 51145 nếu không có thông báo cho món đó.
- **Hoá đơn tạm tính** (Quản lý):
  - Món đang nấu hoặc đã xong có nút **Hủy có tính tiền**.
  - Hộp xác nhận có ba lý do và ô xác nhận. Nút đổi thành **Xác nhận hủy có tính tiền**.
  - Món đã mang ra có nút bị vô hiệu, kèm lý do.
  - Sau khi huỷ, dòng món giữ số tiền, có nhãn “Đã huỷ có tính tiền”, và có trong nhật ký huỷ của hoá đơn.
- **Màn hình bếp**: thẻ “DỪNG MÓN” nằm trong vùng tự tải lại. Nút **Đã dừng món** chỉ có với tài khoản Bếp; Quản lý xem được thẻ nhưng không có nút.
- **Màn hình gọi món** cũng hiện “Đã huỷ có tính tiền” cho dòng loại này.

## Demo

1. Chạy `migrate` (áp dụng 058) rồi chạy web. Mở ca nếu chưa có.
2. Phục vụ mở bàn và gọi món. Bếp bấm **Bắt đầu nấu**.
3. Quản lý mở **Hoá đơn tạm tính**, bấm **Hủy có tính tiền**, chọn lý do, đánh dấu ô xác nhận, rồi bấm **Xác nhận hủy có tính tiền**.
4. Hoá đơn vẫn giữ dòng món với số tiền và nhãn “Đã huỷ có tính tiền”; tạm tính không đổi.
5. Màn hình bếp hiện thẻ “DỪNG MÓN” trong vòng vài giây. Bếp bấm **Đã dừng món**.
6. Quản lý mở **Báo cáo huỷ món**: lần huỷ hiện nhãn “Đã huỷ có tính tiền” cùng người thực hiện, giờ, lý do và cách tính tiền.

## Kiểm thử

- **`tests/RestaurantManagement.AreaTests` → `ChargedCancellationTests`** (không cần database):
  - trạng thái được huỷ, ba lý do, thông báo từ chối;
  - nhãn và tạm tính không giảm;
  - thẻ dừng món;
  - nhãn trong báo cáo;
  - quyền xác nhận dừng món;
  - quy tắc trong migration 058.
- **`node --test tools/tests/provisional-bill-cancel.test.cjs`**: hộp huỷ có tính tiền chỉ có ba lý do; phải đánh dấu ô xác nhận thì mới gửi được; mở lại món chờ bếp thì trở về bốn lý do và không gửi ô xác nhận.
- **`dotnet run --project tools/RestaurantManagement.DbTool -- verify-charged-cancel`** (SQL Server thật và web thật, database tạm; cần build Debug trước). Lệnh `verify` cũng chạy bộ này. Bộ kiểm thử xác nhận:
  - Quản lý huỷ được món đang nấu và đã xong; món đã mang ra bị từ chối.
  - Thiếu lý do, “Quản lý yêu cầu”, thiếu xác nhận (cả khi gửi thẳng không kèm trạng thái) đều bị từ chối và được ghi vào nhật ký ca.
  - Phục vụ, Bếp, Thu ngân, khách và lời gọi thẳng thủ tục SQL đều bị từ chối.
  - Tạm tính không giảm. Dòng món vẫn trên hoá đơn, giữ số tiền và đúng nhãn.
  - Trong 5 giây, món rời hàng đợi bếp và thẻ dừng món hiện đủ thông tin. Hàng chờ mang ra của Phục vụ bỏ món đã xong bị huỷ.
  - Xác nhận lặp không ghi thêm.
  - Bếp chuyển món sang Đã xong trong lúc Quản lý đang chọn lý do: yêu cầu bị từ chối, hoá đơn hiện trạng thái mới; Quản lý xác nhận lại thì huỷ thành công.
  - Bếp bấm “Đã dừng món” một lần (bấm lặp không ghi thêm); vai trò khác không bấm được.
  - Báo cáo cuối ca (API và trang) ghi “Đã huỷ có tính tiền”, trạng thái lúc huỷ, lý do và người huỷ.
  - Thanh toán xong, hoá đơn vẫn tính đủ các dòng huỷ có tính tiền.
