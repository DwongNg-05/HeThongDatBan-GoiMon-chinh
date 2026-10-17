-- S4-08 Task 2: chỉ gửi email nhắc cho lượt đặt bàn VẪN ĐÃ XÁC NHẬN tại thời điểm gửi.
--
--  * Kiểm tra lại trạng thái lượt đặt bàn ở hai chỗ:
--    (1) lúc lấy danh sách email nhắc cần gửi (usp_SkipInvalidBookingReminders, gọi trong usp_ClaimDueBookingReminder,
--        usp_ClaimEmail và usp_RunMaintenance) — email nhắc của lượt không còn hợp lệ bị bỏ qua;
--    (2) ngay trước khi gửi (usp_CheckBookingReminderBeforeSend) — lượt đổi trạng thái sau khi web đã nhận email
--        nhưng chưa gửi thì email bị bỏ qua, không gửi.
--  * Email nhắc bị bỏ qua: EmailOutbox.Status='Cancelled' kèm EmailOutbox.SkipReason (lý do bỏ qua); lượt đặt bàn
--    có ReminderEmailStatus='Cancelled' và ReminderSkipReason.
--    Lý do: Cancelled (đã huỷ), Arrived (khách đã tới), Rejected (bị từ chối), NoShow (khách không tới),
--    NotConfirmed (chưa / không còn ở trạng thái đã xác nhận), StartPassed (đã qua giờ hẹn).
--  * usp_MarkReservationArrived: nhân viên đánh dấu khách đã tới (Confirmed -> Arrived) từ chi tiết đặt bàn.
-- Chưa làm (task sau): liên kết huỷ trong email; chống gửi trùng khi tác vụ chạy lại.
-- Cần migration 059. Chạy lại an toàn.

IF COL_LENGTH('dbo.EmailOutbox','SkipReason') IS NULL
 ALTER TABLE dbo.EmailOutbox ADD SkipReason varchar(30) NULL
  CONSTRAINT CK_EmailOutbox_SkipReason CHECK(SkipReason IN ('Cancelled','Arrived','Rejected','NoShow','NotConfirmed','StartPassed'));
GO
IF COL_LENGTH('dbo.Reservations','ReminderSkipReason') IS NULL
 ALTER TABLE dbo.Reservations ADD ReminderSkipReason varchar(30) NULL
  CONSTRAINT CK_Reservations_ReminderSkipReason CHECK(ReminderSkipReason IN ('Cancelled','Arrived','Rejected','NoShow','NotConfirmed','StartPassed'));
GO

-- Lý do bỏ qua gửi nhắc theo trạng thái lượt đặt bàn; NULL = vẫn hợp lệ để gửi.
CREATE OR ALTER FUNCTION dbo.fn_BookingReminderSkipReason(@Status varchar(20),@StartsAt datetime2(3),@Now datetime2(3))
RETURNS varchar(30)
AS
BEGIN
 RETURN CASE
  WHEN @Status IS NULL THEN 'Cancelled'
  WHEN @Status='Cancelled' THEN 'Cancelled'
  WHEN @Status='Arrived' THEN 'Arrived'
  WHEN @Status='Rejected' THEN 'Rejected'
  WHEN @Status='NoShow' THEN 'NoShow'
  WHEN @Status<>'Confirmed' THEN 'NotConfirmed'
  WHEN @StartsAt<=@Now THEN 'StartPassed'
  ELSE NULL END;
END;
GO

-- Đồng bộ trạng thái email lên lượt đặt bàn (giữ nguyên như 059, thêm lý do bỏ qua email nhắc).
CREATE OR ALTER PROCEDURE dbo.usp_SyncReservationEmailStatus @EmailId bigint
AS
BEGIN
 SET NOCOUNT ON;
 UPDATE r SET
  ConfirmationEmailStatus=CASE e.Status WHEN 'Sent' THEN 'Sent' WHEN 'Failed' THEN 'Failed' WHEN 'Cancelled' THEN 'Cancelled'
   WHEN 'Processing' THEN 'Sending' ELSE CASE WHEN e.AttemptCount>0 THEN 'Retrying' ELSE 'Pending' END END,
  ConfirmationEmailAttempts=e.AttemptCount,
  ConfirmationEmailUpdatedAt=SYSUTCDATETIME()
 FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id
 WHERE e.Id=@EmailId AND e.MessageType IN ('BookingReceived','BookingConfirmed');

 UPDATE r SET
  ReminderEmailStatus=CASE e.Status WHEN 'Sent' THEN 'Sent' WHEN 'Failed' THEN 'Failed' WHEN 'Cancelled' THEN 'Cancelled'
   WHEN 'Processing' THEN 'Sending' ELSE CASE WHEN e.AttemptCount>0 THEN 'Retrying' ELSE 'Pending' END END,
  ReminderEmailSentAt=CASE WHEN e.Status='Sent' THEN e.SentAt ELSE r.ReminderEmailSentAt END,
  ReminderSkipReason=CASE WHEN e.Status='Cancelled' THEN e.SkipReason ELSE NULL END,
  ReminderEmailUpdatedAt=SYSUTCDATETIME()
 FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id
 WHERE e.Id=@EmailId AND e.MessageType='BookingReminder';
END;
GO

-- (1) Bỏ qua mọi email nhắc đang chờ gửi của lượt đặt bàn không còn ở trạng thái đã xác nhận (hoặc đã qua giờ hẹn).
--     Gọi bên trong giao dịch đang giữ usp_LockOperations. @SkippedCount = số email vừa bỏ qua.
CREATE OR ALTER PROCEDURE dbo.usp_SkipInvalidBookingReminders @Now datetime2(3)=NULL,@SkippedCount int=NULL OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @at datetime2(3)=COALESCE(@Now,SYSUTCDATETIME());
 DECLARE @skipped TABLE(EmailId bigint PRIMARY KEY);
 UPDATE e SET Status='Cancelled',LockedUntil=NULL,
   SkipReason=dbo.fn_BookingReminderSkipReason(r.Status,r.StartsAt,@at)
  OUTPUT inserted.Id INTO @skipped
  FROM dbo.EmailOutbox e LEFT JOIN dbo.Reservations r ON r.Id=e.ReservationId
  WHERE e.MessageType='BookingReminder'
   AND dbo.fn_BookingReminderSkipReason(r.Status,r.StartsAt,@at) IS NOT NULL
   -- Email đang chờ gửi; hoặc email đã bị huỷ ở thủ tục khác (ví dụ usp_StaffCancelReservation) nhưng chưa ghi lý do.
   AND (e.Status='Pending' OR (e.Status='Cancelled' AND e.SkipReason IS NULL));
 DECLARE @id bigint=(SELECT MIN(EmailId) FROM @skipped);
 WHILE @id IS NOT NULL
 BEGIN
  EXEC dbo.usp_SyncReservationEmailStatus @id;
  SET @id=(SELECT MIN(EmailId) FROM @skipped WHERE EmailId>@id);
 END;
 SET @SkippedCount=(SELECT COUNT(*) FROM @skipped);
END;
GO

-- Nhận một email nhắc để gửi: bỏ qua lượt không còn hợp lệ trước, rồi chỉ nhận email của lượt VẪN đã xác nhận.
CREATE OR ALTER PROCEDURE dbo.usp_ClaimDueBookingReminder
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_ExpireStaleEmailAttempts;
  EXEC dbo.usp_SkipInvalidBookingReminders;
  DECLARE @id bigint=(SELECT TOP(1) e.Id FROM dbo.EmailOutbox e JOIN dbo.Reservations r ON r.Id=e.ReservationId
   WHERE e.MessageType='BookingReminder' AND e.Status='Pending' AND e.AttemptCount<4
    AND e.NextAttemptAt<=SYSUTCDATETIME() AND r.Status='Confirmed' AND r.StartsAt>SYSUTCDATETIME()
   ORDER BY r.StartsAt,e.Id);
  IF @id IS NOT NULL EXEC dbo.usp_StartEmailAttempt @id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

-- (2) Kiểm tra lại ngay trước khi gửi một email nhắc đã nhận (Processing).
--     Lượt không còn hợp lệ: email chuyển sang Cancelled kèm lý do, lần thử chưa gửi bị gỡ (không tính là một lần gửi).
--     Trả về một dòng: SkipReason (NULL = vẫn hợp lệ, web gửi email).
CREATE OR ALTER PROCEDURE dbo.usp_CheckBookingReminderBeforeSend @EmailId bigint,@AttemptNumber int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @reason varchar(30);
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  SELECT @reason=dbo.fn_BookingReminderSkipReason(r.Status,r.StartsAt,SYSUTCDATETIME())
   FROM dbo.EmailOutbox e LEFT JOIN dbo.Reservations r ON r.Id=e.ReservationId
   WHERE e.Id=@EmailId AND e.MessageType='BookingReminder';
  IF @reason IS NOT NULL
  BEGIN
   UPDATE dbo.EmailOutbox SET Status='Cancelled',SkipReason=@reason,LockedUntil=NULL,AttemptCount=AttemptCount-1
    WHERE Id=@EmailId AND Status='Processing' AND AttemptCount=@AttemptNumber;
   IF @@ROWCOUNT=1
   BEGIN
    DELETE dbo.EmailAttempts WHERE EmailId=@EmailId AND AttemptNumber=@AttemptNumber AND Status='Sending';
    EXEC dbo.usp_SyncReservationEmailStatus @EmailId;
   END;
  END;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
 SELECT @reason AS SkipReason;
END;
GO

-- Worker chung (giữ như 030): bỏ qua email nhắc của lượt không còn hợp lệ trước khi nhận email.
CREATE OR ALTER PROCEDURE dbo.usp_ClaimEmail
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_ExpireStaleEmailAttempts;
  EXEC dbo.usp_SkipInvalidBookingReminders;
  DECLARE @id bigint=(SELECT TOP(1) Id FROM dbo.EmailOutbox
   WHERE Status='Pending' AND AttemptCount<4 AND NextAttemptAt<=SYSUTCDATETIME() ORDER BY NextAttemptAt,Id);
  IF @id IS NOT NULL EXEC dbo.usp_StartEmailAttempt @id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

-- Nhân viên đánh dấu khách đã tới (chỉ lượt đã xác nhận). Email nhắc đang chờ của lượt bị bỏ qua ngay (lý do Arrived).
CREATE OR ALTER PROCEDURE dbo.usp_MarkReservationArrived @ReservationId bigint,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  DECLARE @status varchar(20)=(SELECT Status FROM dbo.Reservations WITH(UPDLOCK) WHERE Id=@ReservationId);
  IF @status IS NULL THROW 51401,N'Không tìm thấy lượt đặt bàn.',1;
  IF @status='Arrived' THROW 51402,N'Lượt đặt bàn này đã được đánh dấu khách đã tới.',1;
  IF @status<>'Confirmed' THROW 51403,N'Chỉ đánh dấu khách đã tới cho lượt đặt bàn đã xác nhận.',1;
  UPDATE dbo.Reservations SET Status='Arrived',ArrivedAt=SYSUTCDATETIME() WHERE Id=@ReservationId;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,ActorUserId,Reason)
   VALUES(@ReservationId,'Confirmed','Arrived',@ActorUserId,N'Nhân viên đánh dấu khách đã tới');
  EXEC dbo.usp_SkipInvalidBookingReminders;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

-- usp_RunMaintenance (bản 059): email nhắc của lượt không còn hợp lệ được bỏ qua kèm lý do thay vì huỷ không rõ lý do.
CREATE OR ALTER PROCEDURE dbo.usp_RunMaintenance
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  DECLARE @now datetime2(3)=SYSUTCDATETIME(),@date date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
  INSERT dbo.MenuAvailabilityEvents(MenuItemId,IsSoldOut,BusinessDate)
    SELECT Id,0,@date FROM dbo.MenuItems WHERE IsSoldOut=1 AND (SoldOutBusinessDate<@date OR SoldOutBusinessDate IS NULL);
  UPDATE dbo.MenuItems SET IsSoldOut=0,SoldOutBusinessDate=@date WHERE IsSoldOut=1 AND (SoldOutBusinessDate<@date OR SoldOutBusinessDate IS NULL);
  UPDATE t SET Status=CASE WHEN EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status='Confirmed'
    AND r.StartsAt<=DATEADD(minute,30,@now) AND r.EndsAt>@now) THEN 'Reserved' ELSE 'Available' END
   FROM dbo.DiningTables t WHERE t.Status IN ('Available','Reserved');
  EXEC dbo.usp_QueueDueBookingReminders @Now=@now;
  EXEC dbo.usp_SkipInvalidBookingReminders @Now=@now;
  DELETE dbo.LookupAttempts WHERE AttemptedAt<DATEADD(day,-1,@now);
  -- Physically delete expired booking personal data; retain financial/order snapshots.
  DECLARE @old TABLE(Id bigint PRIMARY KEY);
  INSERT @old SELECT r.Id FROM dbo.Reservations r WHERE r.EndsAt<DATEADD(month,-12,@now)
   AND NOT EXISTS(SELECT 1 FROM dbo.DiningSessions s WHERE s.ReservationId=r.Id AND s.Status<>'Closed');
  UPDATE s SET ReservationId=NULL FROM dbo.DiningSessions s JOIN @old o ON o.Id=s.ReservationId;
  DELETE e FROM dbo.EmailOutbox e JOIN @old o ON o.Id=e.ReservationId;
  DELETE e FROM dbo.ReservationEvents e JOIN @old o ON o.Id=e.ReservationId;
  DELETE r FROM dbo.Reservations r JOIN @old o ON o.Id=r.Id;
  INSERT dbo.ScheduledJobRuns(JobName,ScheduledFor,CompletedAt,Status) VALUES('Maintenance',@now,SYSUTCDATETIME(),'Succeeded');
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

-- Dữ liệu cũ: email nhắc đã bị huỷ trước đây (usp_StaffCancelReservation, usp_RunMaintenance) nhận lý do theo trạng thái lượt đặt.
UPDATE e SET SkipReason=COALESCE(dbo.fn_BookingReminderSkipReason(r.Status,r.StartsAt,SYSUTCDATETIME()),'NotConfirmed')
FROM dbo.EmailOutbox e LEFT JOIN dbo.Reservations r ON r.Id=e.ReservationId
WHERE e.MessageType='BookingReminder' AND e.Status='Cancelled' AND e.SkipReason IS NULL;
UPDATE r SET ReminderSkipReason=e.SkipReason,ReminderEmailStatus='Cancelled'
FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id AND e.MessageType='BookingReminder'
WHERE e.Status='Cancelled' AND r.ReminderSkipReason IS NULL;
GO

GRANT EXECUTE ON dbo.usp_CheckBookingReminderBeforeSend TO restaurant_app;
GRANT EXECUTE ON dbo.usp_MarkReservationArrived TO restaurant_app;
GO
