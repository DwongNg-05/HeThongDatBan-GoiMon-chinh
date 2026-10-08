IF DB_NAME() NOT LIKE '%[_]Dev' AND DB_NAME() NOT LIKE 'RestaurantManagement[_]S305[_]Test[_]%'
 THROW 51521,N'Chỉ tạo dữ liệu demo trong database phát triển hoặc kiểm thử S305.',1;
-- Explicit development fixture, isolated from real serving sessions and table state.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
EXEC dbo.usp_LockOperations;
IF EXISTS(SELECT 1 FROM dbo.Shifts WHERE Name=N'DEMO S3-05 - Nhật ký huỷ món')
BEGIN COMMIT; RETURN; END;
DECLARE @actor int=(SELECT TOP(1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE r.Code='Manager' AND u.IsActive=1 ORDER BY u.Id);
DECLARE @table int=(SELECT TOP(1) Id FROM dbo.DiningTables ORDER BY Id);
DECLARE @menu int=(SELECT TOP(1) Id FROM dbo.MenuItems ORDER BY Id);
IF @actor IS NULL OR @table IS NULL OR @menu IS NULL THROW 51520,N'Cần tài khoản quản lý, bàn và món để tạo dữ liệu demo.',1;
INSERT dbo.Shifts(Name,BusinessDate,OpenedBy,OpenedAt,Status,ClosedBy,ClosedAt)
VALUES(N'DEMO S3-05 - Ca không có huỷ',CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())),@actor,DATEADD(hour,-3,SYSUTCDATETIME()),'Closed',@actor,DATEADD(hour,-2,SYSUTCDATETIME()));
INSERT dbo.Shifts(Name,BusinessDate,OpenedBy,OpenedAt,Status,ClosedBy,ClosedAt)
VALUES(N'DEMO S3-05 - Nhật ký huỷ món',CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())),@actor,DATEADD(hour,-2,SYSUTCDATETIME()),'Closed',@actor,SYSUTCDATETIME());
DECLARE @shift bigint=SCOPE_IDENTITY();
INSERT dbo.DiningSessions(ShiftId,GuestCount,Status,OpenedBy,OpenedAt,ClosedBy,ClosedAt)
VALUES(@shift,2,'Open',@actor,DATEADD(hour,-2,SYSUTCDATETIME()),NULL,NULL);
DECLARE @session bigint=SCOPE_IDENTITY();
INSERT dbo.OrderBatches(SessionId,BatchNumber,RequestId,CreatedBy) VALUES(@session,1,NEWID(),@actor);
DECLARE @batch bigint=SCOPE_IDENTITY();
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Notes)
VALUES(@batch,@menu,@table,N'DEMO - Khách đổi ý',N'Phần',45000,2,10,N'Dữ liệu thử S3-05');
DECLARE @item bigint=SCOPE_IDENTITY(),@request uniqueidentifier=NEWID(); EXEC dbo.usp_CancelPendingOrderItem @item,2,'ChangedMind',@actor,@request;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Notes)
VALUES(@batch,@menu,@table,N'DEMO - Gọi nhầm',N'Phần',35000,1,10,N'Dữ liệu thử S3-05');
SET @item=SCOPE_IDENTITY(); SET @request=NEWID(); EXEC dbo.usp_CancelPendingOrderItem @item,1,'Mistake',@actor,@request;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Notes)
VALUES(@batch,@menu,@table,N'DEMO - Hết nguyên liệu',N'Phần',25000,3,10,N'Dữ liệu thử S3-05');
SET @item=SCOPE_IDENTITY(); SET @request=NEWID(); EXEC dbo.usp_CancelPendingOrderItem @item,3,'SoldOut',@actor,@request;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,PreparingAt,Notes)
VALUES(@batch,@menu,@table,N'DEMO - Huỷ sau chế biến',N'Phần',60000,1,10,'Preparing',SYSUTCDATETIME(),N'Dữ liệu thử S3-05');
SET @item=SCOPE_IDENTITY(); EXEC dbo.usp_CancelOrderItem @item,'ManagerOverride',@actor,N'Demo nhật ký có tính tiền; chưa cung cấp thao tác trên giao diện';
UPDATE dbo.DiningSessions SET Status='Closed',ClosedBy=@actor,ClosedAt=SYSUTCDATETIME() WHERE Id=@session;
COMMIT;
