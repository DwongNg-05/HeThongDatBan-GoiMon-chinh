-- S4-08 Task 2: không gửi email nhắc cho lượt đặt bàn không còn hợp lệ.
-- Bản migration này là tiền đề của Task 3. Các cột và thủ tục đều chạy lại an toàn.

IF COL_LENGTH('dbo.EmailOutbox','SkipReason') IS NULL
 ALTER TABLE dbo.EmailOutbox ADD SkipReason varchar(30) NULL;
GO

IF COL_LENGTH('dbo.Reservations','ReminderSkipReason') IS NULL
 ALTER TABLE dbo.Reservations ADD ReminderSkipReason varchar(30) NULL;
GO

CREATE OR ALTER FUNCTION dbo.fn_BookingReminderSkipReason
(@Status varchar(20), @StartsAt datetime2(3), @Now datetime2(3))
RETURNS varchar(30)
AS
BEGIN
 RETURN CASE
  WHEN @Status IS NULL OR @Status='Cancelled' THEN 'Cancelled'
  WHEN @Status='Arrived' THEN 'Arrived'
  WHEN @Status='Rejected' THEN 'Rejected'
  WHEN @Status='NoShow' THEN 'NoShow'
  WHEN @Status<>'Confirmed' THEN 'NotConfirmed'
  WHEN @StartsAt<=@Now THEN 'StartPassed'
  ELSE NULL END;
END;
GO

-- Huỷ các email nhắc chưa gửi của lượt đã bị huỷ, khách đã tới, không được xác nhận
-- hoặc đã qua giờ hẹn. Khi email đang Processing, giảm lại một lượt thử vì nó chưa gửi.
CREATE OR ALTER PROCEDURE dbo.usp_SkipInvalidBookingReminders @Now datetime2(3)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @at datetime2(3)=COALESCE(@Now,SYSUTCDATETIME());
 DECLARE @changed TABLE(EmailId bigint NOT NULL, ReservationId bigint NULL);
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  UPDATE e SET
   Status='Cancelled',
   SkipReason=dbo.fn_BookingReminderSkipReason(r.Status,r.StartsAt,@at),
   LockedUntil=NULL,
   AttemptCount=CASE WHEN e.Status='Processing' AND e.AttemptCount>0 THEN e.AttemptCount-1 ELSE e.AttemptCount END,
   LastError=N'Lượt đặt bàn không còn hợp lệ để gửi email nhắc.'
   OUTPUT inserted.Id,inserted.ReservationId INTO @changed(EmailId,ReservationId)
  FROM dbo.EmailOutbox e
  LEFT JOIN dbo.Reservations r ON r.Id=e.ReservationId
  WHERE e.MessageType='BookingReminder' AND e.Status IN ('Pending','Processing')
   AND dbo.fn_BookingReminderSkipReason(r.Status,r.StartsAt,@at) IS NOT NULL;

  DELETE a FROM dbo.EmailAttempts a JOIN @changed c ON c.EmailId=a.EmailId
   WHERE a.Status='Sending';

  UPDATE r SET
   ReminderEmailStatus='Cancelled',
   ReminderSkipReason=e.SkipReason,
   ReminderEmailUpdatedAt=SYSUTCDATETIME()
  FROM dbo.Reservations r
  JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id
  JOIN @changed c ON c.EmailId=e.Id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

GRANT EXECUTE ON dbo.fn_BookingReminderSkipReason TO restaurant_app;
GRANT EXECUTE ON dbo.usp_SkipInvalidBookingReminders TO restaurant_app;
GRANT EXECUTE ON dbo.usp_SkipInvalidBookingReminders TO restaurant_worker;
GO
