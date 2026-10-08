CREATE OR ALTER PROCEDURE dbo.usp_KitchenBatchComplete @BatchId bigint,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Kitchen.Manage';
  IF NOT EXISTS(SELECT 1 FROM dbo.OrderBatches b JOIN dbo.DiningSessions s ON s.Id=b.SessionId
    WHERE b.Id=@BatchId AND s.Status<>'Closed')
    THROW 51029,N'Phiếu không tồn tại hoặc phiên đã đóng.',1;
  IF EXISTS(SELECT 1 FROM dbo.OrderItems WHERE BatchId=@BatchId AND Status='Pending')
    THROW 51030,N'Phiếu còn món chờ bếp. Hãy Bắt đầu từng món trước khi bấm Xong cả phiếu.',1;
  -- Ready, Served and Cancelled lines are preserved, including all timestamps/history.
  DECLARE @id bigint,@version binary(8),@changed int=0;
  DECLARE lines CURSOR LOCAL STATIC FOR
    SELECT Id,RowVersion FROM dbo.OrderItems WHERE BatchId=@BatchId AND Status='Preparing' ORDER BY Id;
  OPEN lines;
  FETCH NEXT FROM lines INTO @id,@version;
  WHILE @@FETCH_STATUS=0
  BEGIN
    EXEC dbo.usp_KitchenLineTransition @OrderItemId=@id,@FromStatus='Preparing',@ToStatus='Ready',
      @ExpectedVersion=@version,@ActorUserId=@ActorUserId;
    SET @changed+=1;
    FETCH NEXT FROM lines INTO @id,@version;
  END;
  CLOSE lines; DEALLOCATE lines;
  COMMIT;
  SELECT @changed;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
