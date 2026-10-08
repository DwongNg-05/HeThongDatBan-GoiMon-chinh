-- S3-05 Task 3: whole-line manager cancellation after preparation, always billable.
CREATE TABLE dbo.PreparedOrderCancellations (
 RequestId uniqueidentifier NOT NULL PRIMARY KEY,
 OrderItemId bigint NOT NULL UNIQUE REFERENCES dbo.OrderItems(Id),
 Quantity int NOT NULL,
 ExpectedStatus varchar(20) NOT NULL CHECK(ExpectedStatus IN ('Preparing','Ready')),
 ActorUserId int NOT NULL REFERENCES dbo.Users(Id),
 Reason varchar(30) NOT NULL CHECK(Reason IN ('ChangedMind','Mistake','SoldOut')),
 OccurredAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
CREATE OR ALTER TRIGGER dbo.tr_PreparedOrderCancellations_AppendOnly
ON dbo.PreparedOrderCancellations INSTEAD OF UPDATE,DELETE AS
BEGIN
 THROW 51101,N'Nhật ký huỷ món chỉ được thêm.',1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CancelPreparedOrderItem
 @OrderItemId bigint,@Quantity int,@Reason varchar(30),@ActorUserId int,
 @RequestId uniqueidentifier,@ExpectedStatus varchar(20),@ConfirmCharged bit
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  IF NOT EXISTS(SELECT 1 FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId
      WHERE u.Id=@ActorUserId AND u.IsActive=1 AND r.Code='Manager')
   THROW 51001,N'Chỉ quản lý được huỷ món đã bắt đầu chế biến.',1;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Orders.CancelPrepared';
  IF @Reason IS NULL OR @Reason NOT IN ('ChangedMind','Mistake','SoldOut')
   THROW 51032,N'Chọn một lý do huỷ bắt buộc.',1;
  IF @ConfirmCharged IS NULL OR @ConfirmCharged<>1 OR @Quantity IS NULL OR @Quantity NOT BETWEEN 1 AND 99
   THROW 51034,N'Phải xác nhận huỷ toàn bộ dòng và vẫn tính tiền.',1;
  IF @ExpectedStatus IS NULL OR @ExpectedStatus NOT IN ('Preparing','Ready')
   THROW 51036,N'Chỉ huỷ có tính tiền khi đang chế biến hoặc chờ mang ra.',1;
  IF @RequestId IS NULL OR @RequestId='00000000-0000-0000-0000-000000000000'
   THROW 51037,N'Mã yêu cầu không hợp lệ.',1;
  IF EXISTS(SELECT 1 FROM dbo.PreparedOrderCancellations WHERE RequestId=@RequestId)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.PreparedOrderCancellations WHERE RequestId=@RequestId
     AND OrderItemId=@OrderItemId AND Quantity=@Quantity AND Reason=@Reason AND ActorUserId=@ActorUserId AND ExpectedStatus=@ExpectedStatus)
    THROW 51037,N'Mã yêu cầu đã được dùng cho thao tác khác.',1;
   SELECT CONVERT(bit,0) AS Changed;
   COMMIT; RETURN;
  END;
  DECLARE @status varchar(20),@sessionStatus varchar(20),@quantityNow int;
  SELECT @status=i.Status,@quantityNow=i.Quantity,@sessionStatus=s.Status
  FROM dbo.OrderItems i WITH(UPDLOCK,HOLDLOCK)
  JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId
  WHERE i.Id=@OrderItemId;
  IF @status IS NULL THROW 51035,N'Không tìm thấy dòng món.',1;
  IF @status NOT IN ('Preparing','Ready') OR @status<>@ExpectedStatus
   THROW 51038,N'Trạng thái món đã thay đổi. Đã cập nhật trạng thái mới nhất; vui lòng kiểm tra lại.',1;
  IF @sessionStatus NOT IN ('Open','AwaitingPayment')
   THROW 51039,N'Phiên phục vụ đã đóng; không thể huỷ món.',1;
  IF @Quantity<>@quantityNow THROW 51034,N'Chỉ được huỷ toàn bộ số lượng trên dòng món.',1;
  DECLARE @now datetime2(3)=SYSUTCDATETIME();
  UPDATE dbo.OrderItems SET Status='Cancelled',CancelledAt=@now,CancelledBy=@ActorUserId,
   CancelReason=@Reason,ChargeWhenCancelled=1 WHERE Id=@OrderItemId;
  INSERT dbo.PreparedOrderCancellations(RequestId,OrderItemId,Quantity,ExpectedStatus,ActorUserId,Reason,OccurredAt)
   VALUES(@RequestId,@OrderItemId,@Quantity,@ExpectedStatus,@ActorUserId,@Reason,@now);
  INSERT dbo.OrderItemEvents(OrderItemId,FromStatus,ToStatus,ActorUserId,Reason,OccurredAt)
   VALUES(@OrderItemId,@status,'Cancelled',@ActorUserId,@Reason,@now);
  SELECT CONVERT(bit,1) AS Changed;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_CancelPreparedOrderItem TO restaurant_app;
