-- One shared rule for completing cleaning and both table maps.
CREATE OR ALTER FUNCTION dbo.fn_NextTableReservation(@TableId int,@Now datetime2(3))
RETURNS TABLE
AS RETURN
 SELECT TOP(1) Id,CustomerName,Phone,GuestCount,StartsAt
 FROM dbo.Reservations
 WHERE TableId=@TableId AND Status='Confirmed' AND ArrivedAt IS NULL
   AND StartsAt>=@Now AND StartsAt<=DATEADD(minute,30,@Now)
 ORDER BY StartsAt,Id;
GO
GRANT SELECT ON dbo.fn_NextTableReservation TO restaurant_app;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CompleteTableCleaning @Code varchar(20),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Sessions.Manage';
  DECLARE @id int,@status varchar(20),@next bigint,@locked bigint;
  SELECT @id=t.Id,@status=t.Status FROM dbo.DiningTables t WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.Areas a ON a.Id=t.AreaId WHERE t.Code=@Code AND t.IsActive=1 AND a.IsActive=1;
  IF @id IS NULL OR @status<>'Cleaning'
   THROW 51070,N'Chỉ được báo đã dọn xong cho bàn đang dọn. Vui lòng kiểm tra trạng thái mới nhất.',1;
  IF EXISTS(SELECT 1 FROM dbo.SessionTables WITH(UPDLOCK,HOLDLOCK) WHERE TableId=@id AND ReleasedAt IS NULL)
   THROW 51071,N'Bàn đang có phiên phục vụ. Không thể báo đã dọn xong.',1;
  -- Protect the table's reservation range until the status change commits.
  SELECT @locked=COUNT_BIG(*) FROM dbo.Reservations WITH(UPDLOCK,HOLDLOCK) WHERE TableId=@id;
  DECLARE @now datetime2(3)=SYSUTCDATETIME();
  SELECT @next=Id FROM dbo.fn_NextTableReservation(@id,@now);
  UPDATE dbo.DiningTables
   SET Status=CASE WHEN @next IS NULL THEN 'Available' ELSE 'Reserved' END,StatusChangedAt=@now
   WHERE Id=@id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
