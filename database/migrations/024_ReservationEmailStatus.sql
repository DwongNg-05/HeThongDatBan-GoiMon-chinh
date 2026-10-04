-- S2-09 Task 3: nhân viên xem trạng thái email xác nhận của từng lượt đặt bàn.
-- dbo.EmailOutbox đã có Status, AttemptCount, NextAttemptAt, SentAt, LastError.
-- Bổ sung thời điểm thử gửi gần nhất (LastAttemptAt) và chỉ mục tra cứu theo lượt đặt bàn.
IF COL_LENGTH('dbo.EmailOutbox','LastAttemptAt') IS NULL
 ALTER TABLE dbo.EmailOutbox ADD LastAttemptAt datetime2(3) NULL;
GO
-- Dữ liệu cũ: ước lượng thời điểm thử gần nhất từ SentAt hoặc lịch thử lại (NextAttemptAt = lần thử + 5 phút).
UPDATE dbo.EmailOutbox
SET LastAttemptAt=COALESCE(SentAt,DATEADD(minute,-5,NextAttemptAt))
WHERE LastAttemptAt IS NULL AND AttemptCount>0 AND Status IN ('Pending','Sent','Failed');
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_EmailOutbox_Reservation' AND object_id=OBJECT_ID('dbo.EmailOutbox'))
 CREATE INDEX IX_EmailOutbox_Reservation ON dbo.EmailOutbox(ReservationId,CreatedAt)
  INCLUDE(MessageType,Recipient,Status,AttemptCount,NextAttemptAt,LastAttemptAt,SentAt);
GO
-- Giống 003_PaymentsAndJobs.sql, thêm ghi LastAttemptAt khi worker nhận một lần thử gửi.
CREATE OR ALTER PROCEDURE dbo.usp_ClaimEmail
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  DECLARE @id bigint=(SELECT TOP(1) Id FROM dbo.EmailOutbox WHERE AttemptCount<4
   AND ((Status='Pending' AND NextAttemptAt<=SYSUTCDATETIME()) OR (Status='Processing' AND LockedUntil<SYSUTCDATETIME()))
   ORDER BY NextAttemptAt,Id);
  IF @id IS NOT NULL
  BEGIN
   UPDATE dbo.EmailOutbox SET Status='Processing',AttemptCount=AttemptCount+1,LockedUntil=DATEADD(minute,2,SYSUTCDATETIME()),
    LastAttemptAt=SYSUTCDATETIME()
    OUTPUT inserted.Id,inserted.AttemptCount,inserted.Recipient,inserted.Subject,inserted.PayloadJson,inserted.MessageType WHERE Id=@id;
  END;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
-- Giống 003_PaymentsAndJobs.sql; gửi thành công thì xoá lỗi của lần thử trước.
CREATE OR ALTER PROCEDURE dbo.usp_CompleteEmail @EmailId bigint,@AttemptNumber int,@Succeeded bit,@Error nvarchar(2000)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  UPDATE dbo.EmailOutbox SET Status=CASE WHEN @Succeeded=1 THEN 'Sent' WHEN AttemptCount>=4 THEN 'Failed' ELSE 'Pending' END,
   SentAt=CASE WHEN @Succeeded=1 THEN SYSUTCDATETIME() ELSE NULL END,
   LastError=CASE WHEN @Succeeded=1 THEN NULL ELSE @Error END,
   LockedUntil=NULL,NextAttemptAt=DATEADD(minute,5,SYSUTCDATETIME())
   WHERE Id=@EmailId AND AttemptCount=@AttemptNumber AND Status='Processing';
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
