-- S4-08 Task 1: gửi email nhắc trước giờ hẹn 2 tiếng cho lượt đặt bàn ĐÃ XÁC NHẬN.
--
-- Quy định (đề xuất, cần PO chốt — xem docs/S4-08-Task1.md):
--  * Email nhận nhắc lấy từ THÔNG TIN LƯỢT ĐẶT BÀN (dbo.Reservations.Email). Hệ thống không có bảng/tài khoản khách hàng:
--    khách nhập email cho từng lượt đặt, nên đây là nguồn email duy nhất và đúng với người đặt lượt đó.
--  * Mốc nhắc = giờ hẹn - RestaurantSettings.ReminderLeadMinutes (mặc định 120 phút).
--    Lượt đến hạn nhắc khi: Status='Confirmed', có email, giờ hẹn còn ở tương lai và còn <= 2 tiếng nữa.
--    Tác vụ chạy mỗi 60 giây, nên email đi trong khoảng 2 tiếng đến 1 tiếng 59 phút trước giờ hẹn.
--    Lượt được xác nhận khi chỉ còn dưới 2 tiếng thì được nhắc ngay ở lượt chạy kế tiếp.
--  * Nội dung email: mã đặt bàn, giờ hẹn, bàn đã xếp (chụp lại trong PayloadJson lúc xếp hàng gửi).
--
-- Chưa làm ở task này (task sau): liên kết huỷ trong email; loại trừ lượt đã huỷ / khách đã tới sau khi đã xếp hàng nhắc;
-- cơ chế bảo đảm không gửi trùng khi tác vụ chạy lại hoặc web dừng giữa lúc gửi.
-- Chạy lại an toàn.

-- 1) Trạng thái gửi nhắc và thời điểm gửi thành công của từng lượt đặt bàn.
IF COL_LENGTH('dbo.Reservations','ReminderEmailStatus') IS NULL
 ALTER TABLE dbo.Reservations ADD
  ReminderEmailStatus varchar(15) NULL
   CONSTRAINT CK_Reservations_ReminderEmailStatus CHECK(ReminderEmailStatus IN ('Pending','Sending','Retrying','Sent','Failed','Cancelled')),
  ReminderEmailSentAt datetime2(3) NULL,
  ReminderEmailUpdatedAt datetime2(3) NULL;
GO

-- Tìm nhanh các lượt đã xác nhận theo giờ hẹn (tác vụ nhắc lịch chạy mỗi phút).
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Reservations_ConfirmedStartsAt' AND object_id=OBJECT_ID('dbo.Reservations'))
 CREATE INDEX IX_Reservations_ConfirmedStartsAt ON dbo.Reservations(StartsAt) INCLUDE(Email,TableId) WHERE Status='Confirmed';
GO

-- 2) Đồng bộ trạng thái email lên lượt đặt bàn sau mỗi lần thử gửi
--    (giữ nguyên phần email xác nhận như migration 050, thêm phần email nhắc lịch).
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

 -- S4-08 Task 1: email nhắc lịch. Thời điểm gửi thành công chỉ ghi khi email đã gửi được (usp_CompleteEmail đặt SentAt trước khi gọi).
 UPDATE r SET
  ReminderEmailStatus=CASE e.Status WHEN 'Sent' THEN 'Sent' WHEN 'Failed' THEN 'Failed' WHEN 'Cancelled' THEN 'Cancelled'
   WHEN 'Processing' THEN 'Sending' ELSE CASE WHEN e.AttemptCount>0 THEN 'Retrying' ELSE 'Pending' END END,
  ReminderEmailSentAt=CASE WHEN e.Status='Sent' THEN e.SentAt ELSE r.ReminderEmailSentAt END,
  ReminderEmailUpdatedAt=SYSUTCDATETIME()
 FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id
 WHERE e.Id=@EmailId AND e.MessageType='BookingReminder';
END;
GO

-- 3) Tác vụ kiểm tra: xếp email nhắc cho mọi lượt đã xác nhận đã tới mốc nhắc (còn <= 2 tiếng tới giờ hẹn).
--    Email nhận nhắc, mã đặt bàn, giờ hẹn và bàn đã xếp được chụp lại trong EmailOutbox (Recipient, PayloadJson).
--    @Now chỉ dùng cho kiểm thử SQL; web luôn gọi không tham số (giờ máy chủ database, UTC).
--    @QueuedCount (OUTPUT) = số email nhắc vừa xếp hàng.
CREATE OR ALTER PROCEDURE dbo.usp_QueueDueBookingReminders @Now datetime2(3)=NULL,@QueuedCount int=NULL OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @at datetime2(3)=COALESCE(@Now,SYSUTCDATETIME());
 DECLARE @lead int=COALESCE((SELECT ReminderLeadMinutes FROM dbo.RestaurantSettings WHERE Id=1),120);
 DECLARE @restaurant nvarchar(150)=COALESCE(NULLIF(LTRIM(RTRIM((SELECT Name FROM dbo.RestaurantSettings WHERE Id=1))),N''),N'Bếp Nhà');
 DECLARE @queued TABLE(EmailId bigint NOT NULL,ReservationId bigint NOT NULL);
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  INSERT dbo.EmailOutbox(ReservationId,MessageType,Recipient,Subject,PayloadJson,DedupeKey)
   OUTPUT inserted.Id,inserted.ReservationId INTO @queued(EmailId,ReservationId)
   SELECT r.Id,'BookingReminder',LTRIM(RTRIM(r.Email)),CONCAT(@restaurant,N' - Nhắc lịch đặt bàn ',r.Code),
    (SELECT r.Code,t.Code AS TableCode,r.CustomerName,r.StartsAt,r.EndsAt,r.GuestCount,r.TableId,r.Status,
            COALESCE(r.AreaNameSnapshot,ta.Name,a.Name) AS AreaName,
            @restaurant AS RestaurantName,s.Address AS RestaurantAddress,s.Phone AS RestaurantPhone
     FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
    CONCAT('BookingReminder:',r.Id)
   FROM dbo.Reservations r
   LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId
   LEFT JOIN dbo.Areas ta ON ta.Id=t.AreaId
   LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
   LEFT JOIN dbo.RestaurantSettings s ON s.Id=1
   WHERE r.Status='Confirmed' AND r.Email IS NOT NULL AND LEN(LTRIM(RTRIM(r.Email)))>0
    AND r.StartsAt>@at AND r.StartsAt<=DATEADD(minute,@lead,@at)
    AND NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox o WHERE o.DedupeKey=CONCAT('BookingReminder:',r.Id));
  UPDATE r SET ReminderEmailStatus='Pending',ReminderEmailUpdatedAt=SYSUTCDATETIME()
   FROM dbo.Reservations r JOIN @queued q ON q.ReservationId=r.Id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
 SET @QueuedCount=(SELECT COUNT(*) FROM @queued);
END;
GO

-- 4) Nhận (khoá) một email nhắc lịch đang chờ gửi để web gửi. Lượt đã qua giờ hẹn thì không gửi nữa.
--    Lần gửi lỗi được usp_CompleteEmail hẹn thử lại sau 5 phút (tối đa 4 lần thử) như các email đặt bàn khác.
CREATE OR ALTER PROCEDURE dbo.usp_ClaimDueBookingReminder
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_ExpireStaleEmailAttempts;
  DECLARE @id bigint=(SELECT TOP(1) e.Id FROM dbo.EmailOutbox e JOIN dbo.Reservations r ON r.Id=e.ReservationId
   WHERE e.MessageType='BookingReminder' AND e.Status='Pending' AND e.AttemptCount<4
    AND e.NextAttemptAt<=SYSUTCDATETIME() AND r.StartsAt>SYSUTCDATETIME()
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

-- 5) usp_RunMaintenance (bản 053) dùng chung thủ tục xếp email nhắc ở trên, để email nhắc luôn có đủ bàn đã xếp
--    và tên khách. Phần còn lại giữ nguyên; email nhắc bị huỷ cũng cập nhật trạng thái trên lượt đặt bàn.
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
  DECLARE @cancelledReminders TABLE(ReservationId bigint);
  UPDATE o SET Status='Cancelled' OUTPUT inserted.ReservationId INTO @cancelledReminders
    FROM dbo.EmailOutbox o JOIN dbo.Reservations r ON r.Id=o.ReservationId
    WHERE o.MessageType='BookingReminder' AND o.Status IN ('Pending','Processing') AND (r.Status<>'Confirmed' OR r.StartsAt<=@now);
  UPDATE r SET ReminderEmailStatus='Cancelled',ReminderEmailUpdatedAt=SYSUTCDATETIME()
    FROM dbo.Reservations r JOIN @cancelledReminders c ON c.ReservationId=r.Id;
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

-- 6) Dữ liệu cũ: lượt đặt bàn đã có email nhắc từ trước (usp_RunMaintenance cũ) nhận trạng thái tương ứng.
UPDATE r SET
 ReminderEmailStatus=CASE e.Status WHEN 'Sent' THEN 'Sent' WHEN 'Failed' THEN 'Failed' WHEN 'Cancelled' THEN 'Cancelled'
  WHEN 'Processing' THEN 'Sending' ELSE CASE WHEN e.AttemptCount>0 THEN 'Retrying' ELSE 'Pending' END END,
 ReminderEmailSentAt=CASE WHEN e.Status='Sent' THEN e.SentAt END,
 ReminderEmailUpdatedAt=COALESCE(e.LastAttemptAt,e.CreatedAt)
FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id AND e.MessageType='BookingReminder'
WHERE r.ReminderEmailStatus IS NULL;
GO

GRANT EXECUTE ON dbo.usp_QueueDueBookingReminders TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ClaimDueBookingReminder TO restaurant_app;
GRANT EXECUTE ON dbo.usp_QueueDueBookingReminders TO restaurant_worker;
GO
