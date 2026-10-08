-- S3-05 Task 1: cancel all or part of a pending line, with an idempotent request.
CREATE TABLE dbo.PendingOrderCancellations (
 RequestId uniqueidentifier NOT NULL PRIMARY KEY,
 OrderItemId bigint NOT NULL REFERENCES dbo.OrderItems(Id),
 CancelledOrderItemId bigint NOT NULL REFERENCES dbo.OrderItems(Id),
 Quantity int NOT NULL CHECK(Quantity BETWEEN 1 AND 99),
 ActorUserId int NOT NULL REFERENCES dbo.Users(Id),
 Reason varchar(30) NOT NULL CHECK(Reason IN ('ChangedMind','Mistake','SoldOut')),
 OccurredAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
CREATE OR ALTER TRIGGER dbo.tr_PendingOrderCancellations_AppendOnly
ON dbo.PendingOrderCancellations INSTEAD OF UPDATE,DELETE AS
BEGIN
 THROW 51101,N'Nhật ký huỷ món chỉ được thêm.',1;
END;
GO
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
  IF @Quantity>@available THROW 51034,N'Số lượng đã thay đổi. Vui lòng chọn lại số lượng huỷ.',1;
  IF @Quantity=@available
  BEGIN
   UPDATE dbo.OrderItems SET Status='Cancelled',CancelledAt=SYSUTCDATETIME(),CancelledBy=@ActorUserId,
    CancelReason=@Reason,ChargeWhenCancelled=0 WHERE Id=@OrderItemId;
   SET @cancelledId=@OrderItemId;
  END
  ELSE
  BEGIN
   INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,Notes,
    EstimatedPrepMinutes,Status,SubmittedAt,CancelledAt,CancelledBy,CancelReason,ChargeWhenCancelled)
   SELECT BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,@Quantity,Notes,
    EstimatedPrepMinutes,'Cancelled',SubmittedAt,SYSUTCDATETIME(),@ActorUserId,@Reason,0
   FROM dbo.OrderItems WHERE Id=@OrderItemId;
   SET @cancelledId=SCOPE_IDENTITY();
   UPDATE dbo.OrderItems SET Quantity=Quantity-@Quantity WHERE Id=@OrderItemId;
  END;
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
