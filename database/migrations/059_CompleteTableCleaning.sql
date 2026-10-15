-- Task 1: only Cleaning -> Available. No overdue warning or next-reservation decision.
CREATE OR ALTER PROCEDURE dbo.usp_CompleteTableCleaning @Code varchar(20),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Sessions.Manage';
  DECLARE @id int,@status varchar(20);
  SELECT @id=t.Id,@status=t.Status FROM dbo.DiningTables t WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.Areas a ON a.Id=t.AreaId WHERE t.Code=@Code AND t.IsActive=1 AND a.IsActive=1;
  IF @id IS NULL OR @status<>'Cleaning'
   THROW 51070,N'Chỉ được báo đã dọn xong cho bàn đang dọn. Vui lòng kiểm tra trạng thái mới nhất.',1;
  IF EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId=@id AND ReleasedAt IS NULL)
   THROW 51071,N'Bàn đang có phiên phục vụ. Không thể báo đã dọn xong.',1;
  UPDATE dbo.DiningTables SET Status='Available',StatusChangedAt=SYSUTCDATETIME() WHERE Id=@id;
  -- The existing status-change trigger records the event in this same transaction.
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
GRANT EXECUTE ON dbo.usp_CompleteTableCleaning TO restaurant_app;
GO
