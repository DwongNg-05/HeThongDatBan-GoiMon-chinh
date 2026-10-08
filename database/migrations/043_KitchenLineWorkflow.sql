-- Task 1 deliberately does not write transition timestamps or timestamped events.
CREATE OR ALTER PROCEDURE dbo.usp_KitchenLineTransition
 @OrderItemId bigint, @FromStatus varchar(20), @ToStatus varchar(20),
 @ExpectedVersion binary(8), @ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Kitchen.Manage';
  IF @FromStatus IS NULL OR @ToStatus IS NULL OR NOT
   ((@FromStatus='Pending' AND @ToStatus='Preparing') OR (@FromStatus='Preparing' AND @ToStatus='Ready'))
   THROW 51030,N'Chỉ được chuyển Chờ bếp → Đang chế biến → Đã xong; không được chuyển lùi hoặc nhảy cóc.',1;
  UPDATE i SET Status=@ToStatus
   FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
   JOIN dbo.DiningSessions s ON s.Id=b.SessionId
   WHERE i.Id=@OrderItemId AND i.Status=@FromStatus AND i.RowVersion=@ExpectedVersion AND s.Status<>'Closed';
  IF @@ROWCOUNT<>1
   THROW 51029,N'Món đã được thiết bị khác cập nhật, không tồn tại hoặc phiên đã đóng. Hãy xem trạng thái mới nhất.',1;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
