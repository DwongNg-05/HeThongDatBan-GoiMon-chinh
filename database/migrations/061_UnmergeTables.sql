-- Keep the source of a split batch for order-request retries and receipt history.
ALTER TABLE dbo.OrderBatches ADD SplitFromBatchId bigint NULL REFERENCES dbo.OrderBatches(Id);
GO
CREATE INDEX IX_OrderBatches_SplitFrom ON dbo.OrderBatches(SplitFromBatchId) WHERE SplitFromBatchId IS NOT NULL;
GO
-- A new merge after unmerging is a new group, even when it keeps the same root session.
CREATE OR ALTER TRIGGER dbo.tr_SessionMerges_GroupLabel ON dbo.SessionMerges AFTER INSERT AS
BEGIN
 SET NOCOUNT ON;
 UPDATE root SET TableGroupLabel=CONCAT('N-G',m.MergeId)
 FROM dbo.DiningSessions root
 JOIN (SELECT MainSessionId,MAX(Id) MergeId FROM inserted GROUP BY MainSessionId) m ON m.MainSessionId=root.Id
 WHERE root.TableGroupLabel IS NULL;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_UnmergeSession @ChildSessionId bigint,@ActorUserId int,
 @ExpectedMergeId bigint=NULL,@ExpectedMainVersion binary(8)=NULL,@ExpectedChildVersion binary(8)=NULL,
 @ExpectedTableId int=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Sessions.Manage';
  DECLARE @main bigint,@merge bigint;
  SELECT @main=BillingSessionId FROM dbo.DiningSessions WITH(UPDLOCK,HOLDLOCK)
   WHERE Id=@ChildSessionId AND Status IN ('Open','AwaitingPayment');
  IF @main IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WITH(UPDLOCK,HOLDLOCK) WHERE Id=@main AND Status IN ('Open','AwaitingPayment'))
   THROW 51160,N'Nhóm đã thanh toán, đã huỷ gộp hoặc không còn hợp lệ. Thông tin bàn đã thay đổi; vui lòng tải lại.',1;
  SELECT @merge=Id FROM dbo.SessionMerges WHERE MainSessionId=@main AND ChildSessionId=@ChildSessionId AND UndoneAt IS NULL;
  IF @merge IS NULL OR (@ExpectedMergeId IS NOT NULL AND @ExpectedMergeId<>@merge)
   OR (@ExpectedMainVersion IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE Id=@main AND RowVersion=@ExpectedMainVersion))
   OR (@ExpectedChildVersion IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE Id=@ChildSessionId AND RowVersion=@ExpectedChildVersion))
   THROW 51161,N'Nhóm hoặc trạng thái vừa thay đổi. Vui lòng tải lại và xác nhận huỷ gộp lần nữa.',1;
  IF (SELECT COUNT(*) FROM dbo.DiningSessions WHERE BillingSessionId=@main)<>1
   OR (SELECT COUNT(*) FROM dbo.SessionTables WHERE SessionId IN(@main,@ChildSessionId) AND ReleasedAt IS NULL)<>2
   OR (SELECT COUNT(DISTINCT SessionId) FROM dbo.SessionTables WHERE SessionId IN(@main,@ChildSessionId) AND ReleasedAt IS NULL)<>2
   THROW 51162,N'Chỉ hỗ trợ huỷ gộp nhóm đúng hai bàn còn phiên riêng.',1;
  IF EXISTS(SELECT 1 FROM dbo.Invoices WHERE SessionId IN(@main,@ChildSessionId))
   THROW 51160,N'Nhóm đã có hoá đơn thanh toán, không thể huỷ gộp.',1;
  IF EXISTS(SELECT 1 FROM dbo.DiningSessions s JOIN dbo.Shifts h ON h.Id=s.ShiftId WHERE s.Id IN(@main,@ChildSessionId) AND h.Status<>'Open')
   THROW 51160,N'Ca đã đóng, không thể huỷ gộp.',1;
  DECLARE @members TABLE(SessionId bigint PRIMARY KEY,TableId int UNIQUE);
  INSERT @members SELECT SessionId,TableId FROM dbo.SessionTables WHERE SessionId IN(@main,@ChildSessionId) AND ReleasedAt IS NULL;
  IF @ExpectedTableId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM @members WHERE TableId=@ExpectedTableId)
   THROW 51161,N'Bàn đã đổi phiên hoặc nhóm. Vui lòng tải lại.',1;
  IF (SELECT COUNT(*) FROM dbo.DiningTables t WITH(UPDLOCK,HOLDLOCK) JOIN @members m ON m.TableId=t.Id WHERE t.Status='Serving' AND t.IsActive=1)<>2
   THROW 51161,N'Trạng thái bàn vừa thay đổi. Vui lòng tải lại.',1;
  IF EXISTS(SELECT 1 FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
    WHERE b.SessionId IN(@main,@ChildSessionId) AND NOT EXISTS(SELECT 1 FROM @members m WHERE m.TableId=i.OriginalTableId))
   THROW 51163,N'Có món mang bàn gốc ngoài nhóm; chưa thể tách an toàn. Vui lòng liên hệ quản lý.',1;

  -- Normal batches have one origin. Also partition a historical mixed-origin batch by LINE origin.
  DECLARE @batch bigint,@origin bigint,@by int,@guest bigint,@created datetime2(3),@ancestor bigint;
  DECLARE batches CURSOR LOCAL STATIC READ_ONLY FOR
   SELECT Id,OriginSessionId,CreatedBy,GuestSessionId,CreatedAt,COALESCE(SplitFromBatchId,Id)
   FROM dbo.OrderBatches WHERE SessionId IN(@main,@ChildSessionId) ORDER BY Id;
  OPEN batches; FETCH NEXT FROM batches INTO @batch,@origin,@by,@guest,@created,@ancestor;
  WHILE @@FETCH_STATUS=0
  BEGIN
   DECLARE @keep bigint=NULL,@target bigint,@newBatch bigint,@number int;
   SELECT TOP(1) @keep=m.SessionId FROM dbo.OrderItems i JOIN @members m ON m.TableId=i.OriginalTableId
    WHERE i.BatchId=@batch ORDER BY CASE WHEN m.SessionId=@origin THEN 0 ELSE 1 END,m.SessionId;
   SET @keep=COALESCE(@keep,CASE WHEN @origin IN(@main,@ChildSessionId) THEN @origin ELSE @main END);
   DECLARE targets CURSOR LOCAL STATIC READ_ONLY FOR
    SELECT DISTINCT m.SessionId FROM dbo.OrderItems i JOIN @members m ON m.TableId=i.OriginalTableId
    WHERE i.BatchId=@batch AND m.SessionId<>@keep;
   OPEN targets; FETCH NEXT FROM targets INTO @target;
   WHILE @@FETCH_STATUS=0
   BEGIN
    SELECT @number=COALESCE(MAX(BatchNumber),0)+1 FROM dbo.OrderBatches WHERE SessionId=@target;
    INSERT dbo.OrderBatches(SessionId,OriginSessionId,BatchNumber,RequestId,CreatedBy,GuestSessionId,CreatedAt,SplitFromBatchId)
     VALUES(@target,@origin,@number,NEWID(),@by,@guest,@created,@ancestor);
    SET @newBatch=SCOPE_IDENTITY();
    UPDATE i SET BatchId=@newBatch FROM dbo.OrderItems i JOIN @members m ON m.TableId=i.OriginalTableId
     WHERE i.BatchId=@batch AND m.SessionId=@target;
    FETCH NEXT FROM targets INTO @target;
   END;
   CLOSE targets; DEALLOCATE targets;
   IF EXISTS(SELECT 1 FROM dbo.OrderBatches WHERE Id=@batch AND SessionId<>@keep)
   BEGIN
    SELECT @number=COALESCE(MAX(BatchNumber),0)+1 FROM dbo.OrderBatches WHERE SessionId=@keep;
    UPDATE dbo.OrderBatches SET SessionId=@keep,BatchNumber=@number WHERE Id=@batch;
   END;
   FETCH NEXT FROM batches INTO @batch,@origin,@by,@guest,@created,@ancestor;
  END;
  CLOSE batches; DEALLOCATE batches;

  UPDATE dbo.DiningSessions SET BillingSessionId=NULL,Status='Open',PaymentRequestedAt=NULL,TableGroupLabel=NULL
   WHERE Id IN(@main,@ChildSessionId);
  UPDATE dbo.SessionMerges SET UndoneAt=SYSUTCDATETIME(),UndoneBy=@ActorUserId WHERE Id=@merge;
  INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,NewValues)
   VALUES(@ActorUserId,'UnmergeSessions','DiningSession',@main,CONCAT('{"mergeId":',@merge,',"childSessionId":',@ChildSessionId,'}'));
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Checkout @SessionId bigint,@RequestId uniqueidentifier,@Method varchar(10),@ActorUserId int,
 @DiscountType varchar(10)='None',@DiscountValue decimal(18,2)=0,@DiscountReason nvarchar(500)=NULL,
 @CashReceived decimal(18,0)=NULL,@TransferReference nvarchar(100)=NULL,@ExpectedSessionVersion binary(8)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Payments.Manage';

  IF EXISTS(SELECT 1 FROM dbo.Invoices WHERE RequestId=@RequestId)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.Invoices WHERE RequestId=@RequestId AND SessionId=@SessionId AND Status='Paid')
    THROW 51041,N'Mã yêu cầu thanh toán không hợp lệ.',1;
   SELECT Id AS InvoiceId,InvoiceNumber,Total FROM dbo.Invoices WHERE RequestId=@RequestId;
  END
  ELSE
  BEGIN
   IF (@ExpectedSessionVersion IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE Id=@SessionId AND RowVersion=@ExpectedSessionVersion))
    OR (@ExpectedSessionVersion IS NULL AND EXISTS(SELECT 1 FROM dbo.SessionMerges WHERE UndoneAt IS NOT NULL AND (MainSessionId=@SessionId OR ChildSessionId=@SessionId)))
    THROW 51165,N'Phiên hoặc nhóm bàn vừa thay đổi. Vui lòng tải lại màn hình thanh toán và xác nhận số tiền mới.',1;
   DECLARE @shift bigint,@opened datetime2(3),@subtotal decimal(18,0),@discount decimal(18,0),@total decimal(18,0);
   SELECT @shift=ShiftId,@opened=OpenedAt FROM dbo.DiningSessions WHERE Id=@SessionId AND BillingSessionId IS NULL AND Status IN ('Open','AwaitingPayment');
   IF @shift IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.Shifts WHERE Id=@shift AND Status='Open')
    THROW 51042,N'Phiên hoặc ca đã đóng.',1;
   IF EXISTS(SELECT 1 FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
     JOIN dbo.DiningSessions s ON s.Id=b.SessionId WHERE (s.Id=@SessionId OR s.BillingSessionId=@SessionId) AND i.Status NOT IN ('Served','Cancelled'))
    THROW 51043,N'Còn món chưa phục vụ. Hãy phục vụ hoặc xử lý huỷ trước khi thanh toán.',1;
   SELECT @subtotal=COALESCE(SUM(i.LineTotal),0) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
    JOIN dbo.DiningSessions s ON s.Id=b.SessionId WHERE (s.Id=@SessionId OR s.BillingSessionId=@SessionId) AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1);
   IF @DiscountType NOT IN ('None','Amount','Percent') OR @DiscountValue<0
    OR (@DiscountType='None' AND @DiscountValue<>0) OR (@DiscountType='Percent' AND @DiscountValue>50)
    OR (@DiscountType='Amount' AND @DiscountValue<>FLOOR(@DiscountValue))
    THROW 51044,N'Giảm giá không hợp lệ.',1;
   SET @discount=CASE @DiscountType WHEN 'Amount' THEN @DiscountValue WHEN 'Percent' THEN ROUND(@subtotal*@DiscountValue/100,0) ELSE 0 END;
   IF @discount>@subtotal*0.5 OR (@discount>0 AND NULLIF(LTRIM(RTRIM(@DiscountReason)),'') IS NULL)
    THROW 51045,N'Giảm giá tối đa 50% và phải có lý do.',1;
   SET @total=@subtotal-@discount;
   IF @Method NOT IN ('Cash','Transfer') OR (@Method='Cash' AND (@CashReceived IS NULL OR @CashReceived<@total))
    OR (@Method='Transfer' AND NULLIF(LTRIM(RTRIM(@TransferReference)),'') IS NULL)
    THROW 51046,N'Thông tin thanh toán không hợp lệ.',1;
   DECLARE @date date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())),@n int;
   IF NOT EXISTS(SELECT 1 FROM dbo.DailyInvoiceCounters WHERE BusinessDate=@date)
    INSERT dbo.DailyInvoiceCounters(BusinessDate,LastNumber) VALUES(@date,1);
   ELSE UPDATE dbo.DailyInvoiceCounters SET LastNumber=LastNumber+1 WHERE BusinessDate=@date;
   SELECT @n=LastNumber FROM dbo.DailyInvoiceCounters WHERE BusinessDate=@date;
   DECLARE @number varchar(30)=CONCAT(CONVERT(char(8),@date,112),'-',RIGHT(CONCAT('000000',@n),6)),@labels nvarchar(500),@guests int;
   SELECT @labels=STRING_AGG(CONVERT(nvarchar(max),t.Code),N', ') FROM dbo.SessionTables st
    JOIN dbo.DiningTables t ON t.Id=st.TableId JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE s.Id=@SessionId OR s.BillingSessionId=@SessionId;
   SELECT @guests=SUM(GuestCount) FROM dbo.DiningSessions WHERE Id=@SessionId OR BillingSessionId=@SessionId;
   INSERT dbo.Invoices(SessionId,ShiftId,InvoiceNumber,BusinessDate,RequestId,RestaurantName,RestaurantAddress,RestaurantPhone,
    TableLabels,GuestCount,SessionOpenedAt,IssuedBy,Subtotal,DiscountType,DiscountValue,DiscountAmount,DiscountReason)
    SELECT @SessionId,@shift,@number,@date,@RequestId,Name,Address,Phone,@labels,@guests,@opened,@ActorUserId,@subtotal,@DiscountType,@DiscountValue,@discount,@DiscountReason
    FROM dbo.RestaurantSettings WHERE Id=1;
   DECLARE @invoice bigint=SCOPE_IDENTITY();
   INSERT dbo.InvoiceLines(InvoiceId,OrderItemId,MenuItemId,ItemName,Unit,Quantity,UnitPrice,Notes,OriginalTableCode,IsChargedCancellation)
    SELECT @invoice,i.Id,i.MenuItemId,i.ItemName,i.Unit,i.Quantity,i.UnitPrice,i.Notes,t.Code,i.ChargeWhenCancelled
    FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId
    WHERE (s.Id=@SessionId OR s.BillingSessionId=@SessionId) AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1);
   INSERT dbo.Payments(InvoiceId,Method,Amount,CashReceived,TransferReference,ReceivedBy)
    VALUES(@invoice,@Method,@total,CASE WHEN @Method='Cash' THEN @CashReceived END,CASE WHEN @Method='Transfer' THEN @TransferReference END,@ActorUserId);
   UPDATE t SET Status='Cleaning',StatusChangedAt=SYSUTCDATETIME() FROM dbo.DiningTables t JOIN dbo.SessionTables st ON st.TableId=t.Id
    JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE s.Id=@SessionId OR s.BillingSessionId=@SessionId;
   UPDATE st SET ReleasedAt=SYSUTCDATETIME() FROM dbo.SessionTables st JOIN dbo.DiningSessions s ON s.Id=st.SessionId
    WHERE s.Id=@SessionId OR s.BillingSessionId=@SessionId;
   UPDATE g SET RevokedAt=SYSUTCDATETIME() FROM dbo.GuestSessions g JOIN dbo.DiningSessions s ON s.Id=g.SessionId
    WHERE s.Id=@SessionId OR s.BillingSessionId=@SessionId;
   UPDATE dbo.DiningSessions SET Status='Closed',ClosedAt=SYSUTCDATETIME(),ClosedBy=@ActorUserId WHERE Id=@SessionId OR BillingSessionId=@SessionId;
   INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,Reason,NewValues)
    VALUES(@ActorUserId,'Checkout','Invoice',@invoice,@DiscountReason,CONCAT('{"total":',@total,',"discount":',@discount,'}'));
   SELECT @invoice AS InvoiceId,@number AS InvoiceNumber,@total AS Total;
  END;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
