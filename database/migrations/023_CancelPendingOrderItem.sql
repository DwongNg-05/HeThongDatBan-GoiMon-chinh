-- Staff cancellation is deliberately separate from management's charged cancellation.
CREATE OR ALTER PROCEDURE dbo.usp_CancelPendingOrderItem
 @OrderItemId bigint,@Reason varchar(30),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Orders.Manage';
  IF @Reason IS NULL OR @Reason NOT IN ('ChangedMind','Mistake','SoldOut')
   THROW 51501,N'Vui lòng chọn lý do huỷ hợp lệ.',1;
  DECLARE @status varchar(20),@sessionStatus varchar(20),@by int,@oldReason varchar(30),@charged bit;
  SELECT @status=i.Status,@sessionStatus=s.Status,@by=i.CancelledBy,@oldReason=i.CancelReason,@charged=i.ChargeWhenCancelled
  FROM dbo.OrderItems i WITH (UPDLOCK,HOLDLOCK)
  JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId
  WHERE i.Id=@OrderItemId;
  IF @status='Cancelled' AND @by=@ActorUserId AND @oldReason=@Reason AND @charged=0
  BEGIN
   COMMIT; SELECT CAST(0 AS bit) AS Changed; RETURN;
  END;
  IF @status IS NULL THROW 51502,N'Dòng món không tồn tại.',1;
  IF @status<>'Pending' THROW 51503,N'Món không còn chờ bếp. Không thể huỷ; danh sách đã được cập nhật.',1;
  IF @sessionStatus<>'Open' THROW 51504,N'Phiên phục vụ không còn mở. Không thể huỷ món.',1;
  UPDATE dbo.OrderItems SET Status='Cancelled',CancelledAt=SYSUTCDATETIME(),CancelledBy=@ActorUserId,
   CancelReason=@Reason,ChargeWhenCancelled=0 WHERE Id=@OrderItemId;
  INSERT dbo.OrderItemEvents(OrderItemId,FromStatus,ToStatus,ActorUserId,Reason)
   VALUES(@OrderItemId,'Pending','Cancelled',@ActorUserId,@Reason);
  COMMIT; SELECT CAST(1 AS bit) AS Changed;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_CancelPendingOrderItem TO restaurant_app;
GO
