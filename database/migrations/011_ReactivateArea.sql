CREATE OR ALTER PROCEDURE dbo.usp_ReactivateArea @ActorUserId int,@AreaId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  UPDATE dbo.Areas SET IsActive=1 WHERE Id=@AreaId;
  IF @@ROWCOUNT=0 THROW 51404,N'Không tìm thấy khu vực.',1;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
