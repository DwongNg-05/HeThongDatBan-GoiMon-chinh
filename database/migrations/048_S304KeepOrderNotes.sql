-- S3-04 Task 4: giữ nguyên ghi chú trên hoá đơn tạm tính.
--
-- Hoá đơn tạm tính và màn hình bếp cùng đọc dbo.OrderItems.Notes (ghi chú tự do) và dbo.OrderItems.QuickNotes (lựa chọn nhanh)
-- của từng dòng đơn, không lưu bản sao. Trigger dưới đây khoá hai cột đó sau khi dòng được gửi bếp: mọi cập nhật đơn
-- (đổi trạng thái chế biến, mang ra, huỷ món, gộp/tách bàn, gọi thêm) vẫn chạy bình thường, nhưng nếu một câu lệnh UPDATE làm
-- THAY ĐỔI nội dung ghi chú (so sánh nhị phân, phân biệt hoa thường và dấu) thì bị từ chối — ghi chú phục vụ đối chiếu luôn là
-- đúng nội dung bếp đã nhận. Muốn đổi yêu cầu: huỷ dòng cũ và gọi dòng mới (giữ được lịch sử).
-- Chạy lại an toàn.

CREATE OR ALTER TRIGGER dbo.tr_OrderItems_KeepNotes ON dbo.OrderItems AFTER UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 IF (UPDATE(Notes) OR UPDATE(QuickNotes))
  AND EXISTS(
   SELECT i.Id, i.Notes COLLATE Latin1_General_BIN2, i.QuickNotes COLLATE Latin1_General_BIN2 FROM inserted i
   EXCEPT
   SELECT d.Id, d.Notes COLLATE Latin1_General_BIN2, d.QuickNotes COLLATE Latin1_General_BIN2 FROM deleted d)
  THROW 51113, N'Ghi chú của món đã gửi bếp không được sửa. Hãy huỷ dòng món và gọi lại với ghi chú mới.', 1;
END;
GO
