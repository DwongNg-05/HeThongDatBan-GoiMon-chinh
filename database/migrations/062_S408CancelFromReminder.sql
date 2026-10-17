-- S4-08 Task 4: khách huỷ lượt đặt bàn bằng đường dẫn trong email nhắc.
--
-- Quy định (đề xuất, cần PO chốt — xem docs/S4-08-Task4.md):
--  * HIỂN THỊ đường dẫn huỷ: tính theo GIỜ GỬI EMAIL. Email nhắc chỉ có đường dẫn huỷ khi giờ hẹn - giờ gửi > 60 phút
--    (RestaurantSettings.CancelCutoffMinutes). Còn đúng 60 phút hoặc ít hơn thì email không có đường dẫn.
--  * CHẤP NHẬN huỷ: tính theo THỜI ĐIỂM KHÁCH BẤM XÁC NHẬN HUỶ. Khách mở email muộn, khi chỉ còn ≤ 60 phút,
--    thì yêu cầu huỷ bị từ chối ("không còn đủ điều kiện huỷ"), giống quy tắc tự huỷ ở trang Tra cứu đặt bàn.
--  * Đường dẫn riêng cho từng lượt: mã bảo mật (ASP.NET Data Protection) bọc Id + mã đặt bàn; thủ tục kiểm tra cả hai,
--    nên đường dẫn chỉ huỷ đúng lượt của email đó.
-- Chạy lại an toàn. Cần migration 059–061.

-- Kết quả (một dòng, cột Result):
--  Cancelled         : huỷ thành công (chỉ trường hợp này thay đổi dữ liệu);
--  AlreadyCancelled  : lượt đã huỷ trước đó (bấm lại đường dẫn / gửi lại yêu cầu) — không thay đổi gì;
--  Arrived           : khách đã tới — không huỷ;
--  TooLate           : còn ≤ 60 phút tới giờ hẹn — không huỷ;
--  NotCancellable    : trạng thái khác (bị từ chối, khách không tới...) — không huỷ;
--  NotFound          : không có lượt khớp cả Id và mã đặt bàn — không huỷ.
CREATE OR ALTER PROCEDURE dbo.usp_CancelReservationFromReminder @ReservationId bigint,@Code char(6)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @result varchar(20),@status varchar(20),@start datetime2(3),@table int,@cutoff int;
 DECLARE @now datetime2(3)=SYSUTCDATETIME();
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  SELECT @status=Status,@start=StartsAt,@table=TableId FROM dbo.Reservations WITH(UPDLOCK)
   WHERE Id=@ReservationId AND Code=@Code;
  SET @cutoff=COALESCE((SELECT CancelCutoffMinutes FROM dbo.RestaurantSettings WHERE Id=1),60);
  SET @result=CASE
   WHEN @status IS NULL THEN 'NotFound'
   WHEN @status='Cancelled' THEN 'AlreadyCancelled'
   WHEN @status='Arrived' THEN 'Arrived'
   WHEN @status NOT IN ('Pending','Confirmed') THEN 'NotCancellable'
   WHEN @start<=DATEADD(minute,@cutoff,@now) THEN 'TooLate'
   ELSE 'Cancelled' END;
  IF @result='Cancelled'
  BEGIN
   UPDATE dbo.Reservations SET Status='Cancelled',CancelledAt=@now,CancelReason=N'Khách huỷ qua email nhắc lịch'
    WHERE Id=@ReservationId AND Status=@status;
   INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,Reason)
    VALUES(@ReservationId,@status,'Cancelled',N'Khách huỷ qua đường dẫn trong email nhắc lịch');
   -- Email nhắc chưa gửi (nếu có) bị bỏ qua với lý do "đã huỷ"; xếp email báo huỷ cho khách.
   EXEC dbo.usp_SkipInvalidBookingReminders @Now=@now;
   EXEC dbo.usp_QueueBookingEmail @ReservationId,'BookingCancelled';
   UPDATE dbo.DiningTables SET Status='Available',StatusChangedAt=@now
    WHERE Id=@table AND Status='Reserved' AND NOT EXISTS(SELECT 1 FROM dbo.Reservations
     WHERE TableId=@table AND Status='Confirmed' AND StartsAt<=DATEADD(minute,30,@now) AND EndsAt>@now);
  END;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
 SELECT @result AS Result;
END;
GO

GRANT EXECUTE ON dbo.usp_CancelReservationFromReminder TO restaurant_app;
GO
