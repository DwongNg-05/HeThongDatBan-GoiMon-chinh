-- S3-05: PO confirmed cancellation of the entire line only.
CREATE OR ALTER PROCEDURE dbo.usp_CancelPendingOrderItem
 @OrderItemId bigint,@Quantity int,@Reason varchar(30),@ActorUserId int,@RequestId uniqueidentifier
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  -- Shared with kitchen transitions and checkout: whichever commits first wins.
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Orders.Manage';
  IF @RequestId IS NULL OR @RequestId='00000000-0000-0000-0000-000000000000'
   THROW 51033,N'Yêu cầu huỷ không hợp lệ.',1;
  IF @Reason IS NULL OR @Reason NOT IN ('ChangedMind','Mistake','SoldOut')
   THROW 51032,N'Chọn lý do huỷ: khách đổi ý, gọi nhầm hoặc hết nguyên liệu.',1;
  IF @Quantity IS NULL OR @Quantity NOT BETWEEN 1 AND 99
   THROW 51034,N'Số lượng huỷ không hợp lệ.',1;
  IF EXISTS(SELECT 1 FROM dbo.PendingOrderCancellations WHERE RequestId=@RequestId)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.PendingOrderCancellations WHERE RequestId=@RequestId
     AND OrderItemId=@OrderItemId AND Quantity=@Quantity AND Reason=@Reason AND ActorUserId=@ActorUserId)
    THROW 51033,N'Mã yêu cầu đã được dùng cho thao tác khác.',1;
   SELECT CONVERT(bit,0) AS Changed;
   COMMIT; RETURN;
  END;
  DECLARE @status varchar(20),@sessionStatus varchar(20),@available int,@cancelledId bigint;
  SELECT @status=i.Status,@sessionStatus=s.Status,@available=i.Quantity
  FROM dbo.OrderItems i WITH(UPDLOCK,HOLDLOCK)
  JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId
  WHERE i.Id=@OrderItemId;
  IF @status IS NULL THROW 51035,N'Không tìm thấy dòng món.',1;
  IF @status<>'Pending' OR @sessionStatus<>'Open'
   THROW 51031,N'Món không còn chờ bếp hoặc phiên đã đóng. Đã cập nhật trạng thái mới nhất.',1;
  IF @Quantity<>@available THROW 51034,N'Chỉ được huỷ toàn bộ số lượng trên dòng món. Vui lòng tải lại trạng thái.',1;
  UPDATE dbo.OrderItems SET Status='Cancelled',CancelledAt=SYSUTCDATETIME(),CancelledBy=@ActorUserId,
   CancelReason=@Reason,ChargeWhenCancelled=0 WHERE Id=@OrderItemId;
  SET @cancelledId=@OrderItemId;
  INSERT dbo.PendingOrderCancellations(RequestId,OrderItemId,CancelledOrderItemId,Quantity,ActorUserId,Reason)
   VALUES(@RequestId,@OrderItemId,@cancelledId,@Quantity,@ActorUserId,@Reason);
  INSERT dbo.OrderItemEvents(OrderItemId,FromStatus,ToStatus,ActorUserId,Reason)
   VALUES(@cancelledId,'Pending','Cancelled',@ActorUserId,CONCAT(@Reason,N'; số lượng: ',@Quantity,N'; dòng gốc: ',@OrderItemId));
  SELECT CONVERT(bit,1) AS Changed;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
