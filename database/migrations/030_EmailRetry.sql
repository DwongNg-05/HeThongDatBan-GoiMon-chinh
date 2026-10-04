-- S2-09 Task 2: tự động gửi lại email khi lần gửi đầu thất bại và ghi nhận kết quả cuối cùng.
-- Quy tắc (đã chốt): 1 lần gửi đầu + tối đa 3 lần gửi lại = tối đa 4 lần thử (khớp CHECK AttemptCount BETWEEN 0 AND 4),
-- mỗi lần gửi lại cách lần thử trước 5 phút. Gửi thành công hoặc hết lần thử thì có kết quả cuối cùng (Sent / Failed).
-- Cần migration 024 (LastAttemptAt) và 026 (usp_ClaimReservationEmail).

-- 1) Lịch sử từng lần thử gửi: số thứ tự, thời điểm, trạng thái và lỗi của lần đó.
IF OBJECT_ID('dbo.EmailAttempts','U') IS NULL
 CREATE TABLE dbo.EmailAttempts (
  Id bigint IDENTITY CONSTRAINT PK_EmailAttempts PRIMARY KEY,
  EmailId bigint NOT NULL CONSTRAINT FK_EmailAttempts_EmailOutbox REFERENCES dbo.EmailOutbox(Id) ON DELETE CASCADE,
  AttemptNumber int NOT NULL CONSTRAINT CK_EmailAttempts_Number CHECK(AttemptNumber BETWEEN 1 AND 4),
  Status varchar(15) NOT NULL CONSTRAINT DF_EmailAttempts_Status DEFAULT 'Sending'
   CONSTRAINT CK_EmailAttempts_Status CHECK(Status IN ('Sending','Succeeded','Failed')),
  StartedAt datetime2(3) NOT NULL CONSTRAINT DF_EmailAttempts_StartedAt DEFAULT SYSUTCDATETIME(),
  CompletedAt datetime2(3) NULL,
  Error nvarchar(2000) NULL,
  CONSTRAINT UX_EmailAttempts_Email_Number UNIQUE(EmailId,AttemptNumber)
 );
GO
GRANT SELECT ON dbo.EmailAttempts TO restaurant_app;
GO

-- 2) Trạng thái email xác nhận ngay trên lượt đặt bàn, cập nhật sau mỗi lần thử.
IF COL_LENGTH('dbo.Reservations','ConfirmationEmailStatus') IS NULL
 ALTER TABLE dbo.Reservations ADD
  ConfirmationEmailStatus varchar(15) NULL
   CONSTRAINT CK_Reservations_ConfirmationEmailStatus CHECK(ConfirmationEmailStatus IN ('Pending','Sending','Retrying','Sent','Failed','Cancelled')),
  ConfirmationEmailAttempts int NOT NULL CONSTRAINT DF_Reservations_ConfirmationEmailAttempts DEFAULT 0,
  ConfirmationEmailUpdatedAt datetime2(3) NULL;
GO

-- Nội bộ: chép trạng thái email xác nhận (BookingReceived) sang lượt đặt bàn.
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
 WHERE e.Id=@EmailId AND e.MessageType='BookingReceived';
END;
GO

-- Dữ liệu cũ: điền trạng thái email xác nhận cho các lượt đặt bàn đã có email.
UPDATE r SET
 ConfirmationEmailStatus=CASE e.Status WHEN 'Sent' THEN 'Sent' WHEN 'Failed' THEN 'Failed' WHEN 'Cancelled' THEN 'Cancelled'
  WHEN 'Processing' THEN 'Sending' ELSE CASE WHEN e.AttemptCount>0 THEN 'Retrying' ELSE 'Pending' END END,
 ConfirmationEmailAttempts=e.AttemptCount,
 ConfirmationEmailUpdatedAt=COALESCE(e.LastAttemptAt,e.CreatedAt)
FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id AND e.MessageType='BookingReceived'
WHERE r.ConfirmationEmailStatus IS NULL;
GO

-- Nội bộ: lần thử bị gián đoạn (web dừng giữa lúc gửi, quá thời gian khoá 2 phút) được ghi là thất bại.
-- Còn lần thử thì hẹn gửi lại sau 5 phút tính từ lần thử đó; hết lần thử thì kết quả cuối cùng là Failed.
CREATE OR ALTER PROCEDURE dbo.usp_ExpireStaleEmailAttempts
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @now datetime2(3)=SYSUTCDATETIME();
 DECLARE @stale TABLE(Id bigint PRIMARY KEY);
 DECLARE @message nvarchar(200)=N'Không nhận được kết quả gửi (lần thử bị gián đoạn).';
 UPDATE dbo.EmailOutbox SET
  Status=CASE WHEN AttemptCount>=4 THEN 'Failed' ELSE 'Pending' END,
  LastError=@message,LockedUntil=NULL,
  NextAttemptAt=DATEADD(minute,5,COALESCE(LastAttemptAt,@now))
  OUTPUT inserted.Id INTO @stale
  WHERE Status='Processing' AND LockedUntil<@now;
 UPDATE a SET Status='Failed',CompletedAt=@now,Error=@message
  FROM dbo.EmailAttempts a JOIN dbo.EmailOutbox e ON e.Id=a.EmailId AND a.AttemptNumber=e.AttemptCount
  JOIN @stale s ON s.Id=e.Id WHERE a.Status='Sending';
 -- Không gửi lại email xác nhận cho lượt đặt bàn đã huỷ / bị từ chối / khách không đến.
 DECLARE @ended TABLE(Id bigint PRIMARY KEY);
 UPDATE e SET Status='Cancelled',LockedUntil=NULL,
  LastError=N'Lượt đặt bàn đã kết thúc nên không gửi lại email xác nhận.'
  OUTPUT inserted.Id INTO @ended
  FROM dbo.EmailOutbox e JOIN dbo.Reservations r ON r.Id=e.ReservationId
  WHERE e.MessageType='BookingReceived' AND e.Status='Pending' AND e.AttemptCount>0
   AND r.Status IN ('Cancelled','Rejected','NoShow');
 DECLARE @changed TABLE(Id bigint PRIMARY KEY);
 INSERT @changed SELECT Id FROM @stale UNION SELECT Id FROM @ended;
 DECLARE @id bigint=(SELECT MIN(Id) FROM @changed);
 WHILE @id IS NOT NULL
 BEGIN
  EXEC dbo.usp_SyncReservationEmailStatus @id;
  SET @id=(SELECT MIN(Id) FROM @changed WHERE Id>@id);
 END;
END;
GO

-- Nội bộ: nhận (khoá) đúng một email để thử gửi, ghi một dòng EmailAttempts và trả về thông tin gửi.
CREATE OR ALTER PROCEDURE dbo.usp_StartEmailAttempt @EmailId bigint
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @claimed TABLE(Id bigint,AttemptCount int,Recipient nvarchar(254),Subject nvarchar(250),PayloadJson nvarchar(max),
  MessageType varchar(30),ReservationId bigint NULL);
 UPDATE dbo.EmailOutbox SET Status='Processing',AttemptCount=AttemptCount+1,LockedUntil=DATEADD(minute,2,SYSUTCDATETIME()),
  LastAttemptAt=SYSUTCDATETIME()
  OUTPUT inserted.Id,inserted.AttemptCount,inserted.Recipient,inserted.Subject,inserted.PayloadJson,inserted.MessageType,inserted.ReservationId
  INTO @claimed
  WHERE Id=@EmailId AND Status='Pending' AND AttemptCount<4;
 IF NOT EXISTS(SELECT 1 FROM @claimed) RETURN;
 DELETE a FROM dbo.EmailAttempts a JOIN @claimed c ON c.Id=a.EmailId AND a.AttemptNumber=c.AttemptCount;
 INSERT dbo.EmailAttempts(EmailId,AttemptNumber) SELECT Id,AttemptCount FROM @claimed;
 EXEC dbo.usp_SyncReservationEmailStatus @EmailId;
 SELECT Id,AttemptCount,Recipient,Subject,PayloadJson,MessageType,ReservationId FROM @claimed;
END;
GO

-- 3) Worker chung (giữ tên và kết quả trả về như 003/024): lần thử bị gián đoạn được ghi thất bại thay vì gửi chồng.
CREATE OR ALTER PROCEDURE dbo.usp_ClaimEmail
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_ExpireStaleEmailAttempts;
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

-- 4) Web gửi ngay email của lượt đặt bàn vừa tạo / vừa huỷ (lần gửi đầu).
CREATE OR ALTER PROCEDURE dbo.usp_ClaimReservationEmail @ReservationId bigint,@MessageType varchar(30)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  DECLARE @id bigint=(SELECT TOP(1) Id FROM dbo.EmailOutbox
   WHERE ReservationId=@ReservationId AND MessageType=@MessageType AND Status='Pending' AND AttemptCount<4 ORDER BY Id);
  IF @id IS NOT NULL EXEC dbo.usp_StartEmailAttempt @id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

-- 5) Worker gửi lại của web: chỉ nhận email xác nhận / báo huỷ đặt bàn ĐÃ thử gửi ít nhất 1 lần và thất bại,
--    còn lần thử (tối đa 3 lần gửi lại) và đã tới giờ gửi lại (5 phút sau lần thử trước).
CREATE OR ALTER PROCEDURE dbo.usp_ClaimDueBookingEmail
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_ExpireStaleEmailAttempts;
  DECLARE @id bigint=(SELECT TOP(1) Id FROM dbo.EmailOutbox
   WHERE Status='Pending' AND AttemptCount BETWEEN 1 AND 3 AND NextAttemptAt<=SYSUTCDATETIME()
    AND MessageType IN ('BookingReceived','BookingCancelled')
   ORDER BY NextAttemptAt,Id);
  IF @id IS NOT NULL EXEC dbo.usp_StartEmailAttempt @id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

-- 6) Ghi kết quả một lần thử: thành công → Sent (kết quả cuối cùng);
--    thất bại → còn lần thử thì Pending, hẹn gửi lại đúng 5 phút sau; lần thử thứ 4 thất bại → Failed (kết quả cuối cùng).
CREATE OR ALTER PROCEDURE dbo.usp_CompleteEmail @EmailId bigint,@AttemptNumber int,@Succeeded bit,@Error nvarchar(2000)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  DECLARE @now datetime2(3)=SYSUTCDATETIME();
  UPDATE dbo.EmailOutbox SET Status=CASE WHEN @Succeeded=1 THEN 'Sent' WHEN AttemptCount>=4 THEN 'Failed' ELSE 'Pending' END,
   SentAt=CASE WHEN @Succeeded=1 THEN @now ELSE NULL END,
   LastError=CASE WHEN @Succeeded=1 THEN NULL ELSE @Error END,
   LockedUntil=NULL,NextAttemptAt=DATEADD(minute,5,@now)
   WHERE Id=@EmailId AND AttemptCount=@AttemptNumber AND Status='Processing';
  IF @@ROWCOUNT=1
  BEGIN
   UPDATE dbo.EmailAttempts SET Status=CASE WHEN @Succeeded=1 THEN 'Succeeded' ELSE 'Failed' END,CompletedAt=@now,
    Error=CASE WHEN @Succeeded=1 THEN NULL ELSE @Error END
    WHERE EmailId=@EmailId AND AttemptNumber=@AttemptNumber;
   IF @@ROWCOUNT=0
    INSERT dbo.EmailAttempts(EmailId,AttemptNumber,Status,StartedAt,CompletedAt,Error)
    SELECT Id,@AttemptNumber,CASE WHEN @Succeeded=1 THEN 'Succeeded' ELSE 'Failed' END,COALESCE(LastAttemptAt,@now),@now,
     CASE WHEN @Succeeded=1 THEN NULL ELSE @Error END
    FROM dbo.EmailOutbox WHERE Id=@EmailId;
   EXEC dbo.usp_SyncReservationEmailStatus @EmailId;
  END;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_ClaimDueBookingEmail TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ClaimReservationEmail TO restaurant_app;
GRANT EXECUTE ON dbo.usp_CompleteEmail TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ClaimEmail TO restaurant_worker;
GRANT EXECUTE ON dbo.usp_CompleteEmail TO restaurant_worker;
GO
