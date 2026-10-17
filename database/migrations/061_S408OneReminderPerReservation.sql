-- S4-08 Task 3: mỗi lượt đặt bàn chỉ nhận TỐI ĐA MỘT email nhắc, kể cả khi tác vụ định kỳ chạy lại hoặc chạy đồng thời.
--
-- Các lớp bảo vệ:
--  1) Đánh dấu "đã nhắc": Reservations.ReminderEmailSentAt chỉ được ghi khi lần gửi THÀNH CÔNG (usp_CompleteEmail -> Sent).
--     Lượt đã có ReminderEmailSentAt bị loại khỏi danh sách cần xếp hàng và cần gửi ở mọi lần chạy sau.
--  2) Một lượt chỉ có một email nhắc: chỉ mục duy nhất UX_EmailOutbox_OneReminderPerReservation
--     (ngoài DedupeKey 'BookingReminder:{Id}'), nên hai lần xếp hàng đồng thời không tạo được email thứ hai.
--  3) Chạy đồng thời: nhận email để gửi diễn ra trong giao dịch giữ khoá usp_LockOperations và chuyển Pending -> Processing
--     (tăng AttemptCount) trong cùng câu lệnh; tác vụ thứ hai không nhận được email đang Processing.
--  4) Ngay trước khi gửi (usp_CheckBookingReminderBeforeSend): nếu lượt đã được nhắc thành công, hoặc email không còn
--     đúng lần thử mà tác vụ này đang giữ (khoá đã hết hạn và tác vụ khác đã nhận lại), thì KHÔNG gửi.
--  Gửi thất bại: email trở lại Pending, tự thử lại sau 5 phút, tối đa 4 lần thử (giữ quy tắc 030). Lượt chưa bị đánh dấu đã nhắc.
--  Trạng thái theo dõi: Reservations.ReminderEmailAttempts = số lần thử gửi nhắc.
-- Chưa làm (task sau): đường dẫn huỷ trong email. Cần migration 059, 060. Chạy lại an toàn.

IF COL_LENGTH('dbo.Reservations','ReminderEmailAttempts') IS NULL
 ALTER TABLE dbo.Reservations ADD ReminderEmailAttempts int NOT NULL CONSTRAINT DF_Reservations_ReminderEmailAttempts DEFAULT 0;
GO

-- Chỉ tạo chỉ mục khi dữ liệu cũ chưa có lượt nào nhiều hơn một email nhắc (DedupeKey vốn đã bảo đảm điều này).
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_EmailOutbox_OneReminderPerReservation' AND object_id=OBJECT_ID('dbo.EmailOutbox'))
 AND NOT EXISTS(SELECT ReservationId FROM dbo.EmailOutbox WHERE MessageType='BookingReminder' AND ReservationId IS NOT NULL
                GROUP BY ReservationId HAVING COUNT(*)>1)
 CREATE UNIQUE INDEX UX_EmailOutbox_OneReminderPerReservation ON dbo.EmailOutbox(ReservationId)
  WHERE MessageType='BookingReminder' AND ReservationId IS NOT NULL;
GO

-- Đồng bộ trạng thái email lên lượt đặt bàn (như 060), thêm số lần thử gửi nhắc.
-- ReminderEmailSentAt không bao giờ bị xoá khi đã ghi: đó là dấu "đã nhắc thành công".
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
  ReminderEmailSentAt=CASE WHEN e.Status='Sent' AND r.ReminderEmailSentAt IS NULL THEN e.SentAt ELSE r.ReminderEmailSentAt END,
  ReminderSkipReason=CASE WHEN e.Status='Cancelled' THEN e.SkipReason ELSE NULL END,
  ReminderEmailAttempts=e.AttemptCount,
  ReminderEmailUpdatedAt=SYSUTCDATETIME()
 FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id
 WHERE e.Id=@EmailId AND e.MessageType='BookingReminder';
END;
GO

-- Xếp email nhắc (như 059): thêm điều kiện lượt CHƯA được nhắc thành công.
-- Chèn trùng do chạy đồng thời (dù đã có khoá) bị chỉ mục duy nhất chặn; thủ tục bỏ qua dòng trùng thay vì báo lỗi.
CREATE OR ALTER PROCEDURE dbo.usp_QueueDueBookingReminders @Now datetime2(3)=NULL,@QueuedCount int=NULL OUTPUT
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @at datetime2(3)=COALESCE(@Now,SYSUTCDATETIME());
 DECLARE @lead int=COALESCE((SELECT ReminderLeadMinutes FROM dbo.RestaurantSettings WHERE Id=1),120);
 DECLARE @restaurant nvarchar(150)=COALESCE(NULLIF(LTRIM(RTRIM((SELECT Name FROM dbo.RestaurantSettings WHERE Id=1))),N''),N'Bếp Nhà');
 DECLARE @queued TABLE(EmailId bigint NOT NULL,ReservationId bigint NOT NULL);
 DECLARE @outer int=@@TRANCOUNT; -- >0 khi được gọi bên trong giao dịch khác (usp_RunMaintenance)
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
    AND r.ReminderEmailSentAt IS NULL
    AND r.StartsAt>@at AND r.StartsAt<=DATEADD(minute,@lead,@at)
    AND NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox o WITH (UPDLOCK,HOLDLOCK)
                   WHERE o.MessageType='BookingReminder' AND (o.ReservationId=r.Id OR o.DedupeKey=CONCAT('BookingReminder:',r.Id)));
  UPDATE r SET ReminderEmailStatus='Pending',ReminderEmailAttempts=0,ReminderEmailUpdatedAt=SYSUTCDATETIME()
   FROM dbo.Reservations r JOIN @queued q ON q.ReservationId=r.Id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  -- 2601/2627: email nhắc của lượt này vừa được xếp ở lần chạy khác. Không phải lỗi, không xếp thêm.
  IF ERROR_NUMBER() IN (2601,2627) AND @outer=0 BEGIN SET @QueuedCount=0; RETURN; END;
  THROW;
 END CATCH
 SET @QueuedCount=(SELECT COUNT(*) FROM @queued);
END;
GO

-- Nhận một email nhắc để gửi (như 060): thêm điều kiện lượt CHƯA được nhắc thành công.
-- Email của lượt đã nhắc thành công mà vẫn còn chờ (dữ liệu bất thường) bị huỷ để không bao giờ gửi lần hai.
CREATE OR ALTER PROCEDURE dbo.usp_ClaimDueBookingReminder
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_ExpireStaleEmailAttempts;
  EXEC dbo.usp_SkipInvalidBookingReminders;
  UPDATE e SET Status='Cancelled',LockedUntil=NULL,LastError=N'Lượt đặt bàn đã được nhắc thành công, không gửi lại.'
   FROM dbo.EmailOutbox e JOIN dbo.Reservations r ON r.Id=e.ReservationId
   WHERE e.MessageType='BookingReminder' AND e.Status='Pending' AND r.ReminderEmailSentAt IS NOT NULL;
  DECLARE @id bigint=(SELECT TOP(1) e.Id FROM dbo.EmailOutbox e JOIN dbo.Reservations r ON r.Id=e.ReservationId
   WHERE e.MessageType='BookingReminder' AND e.Status='Pending' AND e.AttemptCount<4
    AND e.NextAttemptAt<=SYSUTCDATETIME() AND r.Status='Confirmed' AND r.StartsAt>SYSUTCDATETIME()
    AND r.ReminderEmailSentAt IS NULL
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

-- Kiểm tra ngay trước khi gửi (như 060), thêm hai trường hợp KHÔNG gửi để chống trùng:
--  'AlreadySent' : lượt đã được nhắc thành công (ví dụ bởi một tác vụ khác);
--  'NotClaimed'  : email không còn đúng lần thử tác vụ này đang giữ (khoá hết hạn, tác vụ khác đã nhận lại / đã gửi).
--  Hai mã này chỉ trả về cho web, không ghi vào SkipReason (email vẫn thuộc về tác vụ đang giữ nó).
CREATE OR ALTER PROCEDURE dbo.usp_CheckBookingReminderBeforeSend @EmailId bigint,@AttemptNumber int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @reason varchar(30),@status varchar(15),@attempts int,@sentAt datetime2(3);
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  SELECT @reason=dbo.fn_BookingReminderSkipReason(r.Status,r.StartsAt,SYSUTCDATETIME()),
         @status=e.Status,@attempts=e.AttemptCount,@sentAt=r.ReminderEmailSentAt
   FROM dbo.EmailOutbox e LEFT JOIN dbo.Reservations r ON r.Id=e.ReservationId
   WHERE e.Id=@EmailId AND e.MessageType='BookingReminder';
  IF @status IS NULL OR @status<>'Processing' OR @attempts<>@AttemptNumber
   SET @reason='NotClaimed';
  ELSE IF @sentAt IS NOT NULL
  BEGIN
   SET @reason='AlreadySent';
   UPDATE dbo.EmailOutbox SET Status='Cancelled',LockedUntil=NULL,AttemptCount=AttemptCount-1,
     LastError=N'Lượt đặt bàn đã được nhắc thành công, không gửi lại.'
    WHERE Id=@EmailId AND Status='Processing' AND AttemptCount=@AttemptNumber;
   DELETE dbo.EmailAttempts WHERE EmailId=@EmailId AND AttemptNumber=@AttemptNumber AND Status='Sending';
  END
  ELSE IF @reason IS NOT NULL
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

-- Dữ liệu cũ: số lần thử gửi nhắc.
UPDATE r SET ReminderEmailAttempts=e.AttemptCount
FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id AND e.MessageType='BookingReminder';
GO
