-- S4-02 Task 1: không cho tạo hoá đơn rỗng. Kiểm tra nằm trong cùng giao dịch
-- với cấp số hoá đơn, khoá phiên và đổi bàn sang Cleaning.
CREATE OR ALTER PROCEDURE dbo.usp_Checkout @SessionId bigint,@RequestId uniqueidentifier,@Method varchar(10),@ActorUserId int,
 @DiscountType varchar(10)='None',@DiscountValue decimal(18,2)=0,@DiscountReason nvarchar(500)=NULL,
 @CashReceived decimal(18,0)=NULL,@TransferReference nvarchar(100)=NULL
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
   DECLARE @shift bigint,@opened datetime2(3),@subtotal decimal(18,0),@discount decimal(18,0),@total decimal(18,0);
   SELECT @shift=ShiftId,@opened=OpenedAt FROM dbo.DiningSessions WHERE Id=@SessionId AND BillingSessionId IS NULL AND Status IN ('Open','AwaitingPayment');
   IF @shift IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.Shifts WHERE Id=@shift AND Status='Open') THROW 51042,N'Phiên hoặc ca đã đóng.',1;
   IF EXISTS(SELECT 1 FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId WHERE (s.Id=@SessionId OR s.BillingSessionId=@SessionId) AND i.Status NOT IN ('Served','Cancelled'))
    THROW 51043,N'Còn món chưa phục vụ. Hãy phục vụ hoặc xử lý huỷ trước khi thanh toán.',1;
   IF NOT EXISTS(SELECT 1 FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId WHERE (s.Id=@SessionId OR s.BillingSessionId=@SessionId) AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1))
    THROW 51057,N'Phiên chưa có món hợp lệ để thanh toán.',1;
   SELECT @subtotal=COALESCE(SUM(i.LineTotal),0) FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId WHERE (s.Id=@SessionId OR s.BillingSessionId=@SessionId) AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1);
   IF @DiscountType NOT IN ('None','Amount','Percent') OR @DiscountValue<0 OR (@DiscountType='None' AND @DiscountValue<>0) OR (@DiscountType='Percent' AND @DiscountValue>50) OR (@DiscountType='Amount' AND @DiscountValue<>FLOOR(@DiscountValue)) THROW 51044,N'Giảm giá không hợp lệ.',1;
   SET @discount=CASE @DiscountType WHEN 'Amount' THEN @DiscountValue WHEN 'Percent' THEN ROUND(@subtotal*@DiscountValue/100,0) ELSE 0 END;
   IF @discount>@subtotal*0.5 OR (@discount>0 AND NULLIF(LTRIM(RTRIM(@DiscountReason)),'') IS NULL) THROW 51045,N'Giảm giá tối đa 50% và phải có lý do.',1;
   SET @total=@subtotal-@discount;
   IF @Method NOT IN ('Cash','Transfer') OR (@Method='Cash' AND (@CashReceived IS NULL OR @CashReceived<@total)) OR (@Method='Transfer' AND NULLIF(LTRIM(RTRIM(@TransferReference)),'') IS NULL) THROW 51046,N'Thông tin thanh toán không hợp lệ.',1;
   DECLARE @date date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())),@n int;
   IF NOT EXISTS(SELECT 1 FROM dbo.DailyInvoiceCounters WHERE BusinessDate=@date) INSERT dbo.DailyInvoiceCounters(BusinessDate,LastNumber) VALUES(@date,1); ELSE UPDATE dbo.DailyInvoiceCounters SET LastNumber=LastNumber+1 WHERE BusinessDate=@date;
   SELECT @n=LastNumber FROM dbo.DailyInvoiceCounters WHERE BusinessDate=@date;
   DECLARE @number varchar(30)=CONCAT(CONVERT(char(8),@date,112),'-',RIGHT(CONCAT('000000',@n),6)),@labels nvarchar(500),@guests int;
   SELECT @labels=STRING_AGG(CONVERT(nvarchar(max),t.Code),N', ') FROM dbo.SessionTables st JOIN dbo.DiningTables t ON t.Id=st.TableId JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE s.Id=@SessionId OR s.BillingSessionId=@SessionId;
   SELECT @guests=SUM(GuestCount) FROM dbo.DiningSessions WHERE Id=@SessionId OR BillingSessionId=@SessionId;
   INSERT dbo.Invoices(SessionId,ShiftId,InvoiceNumber,BusinessDate,RequestId,RestaurantName,RestaurantAddress,RestaurantPhone,TableLabels,GuestCount,SessionOpenedAt,IssuedBy,Subtotal,DiscountType,DiscountValue,DiscountAmount,DiscountReason)
    SELECT @SessionId,@shift,@number,@date,@RequestId,Name,Address,Phone,@labels,@guests,@opened,@ActorUserId,@subtotal,@DiscountType,@DiscountValue,@discount,@DiscountReason FROM dbo.RestaurantSettings WHERE Id=1;
   DECLARE @invoice bigint=SCOPE_IDENTITY();
   INSERT dbo.InvoiceLines(InvoiceId,OrderItemId,MenuItemId,ItemName,Unit,Quantity,UnitPrice,Notes,OriginalTableCode,IsChargedCancellation)
    SELECT @invoice,i.Id,i.MenuItemId,i.ItemName,i.Unit,i.Quantity,i.UnitPrice,i.Notes,t.Code,i.ChargeWhenCancelled FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId WHERE (s.Id=@SessionId OR s.BillingSessionId=@SessionId) AND (i.Status<>'Cancelled' OR i.ChargeWhenCancelled=1);
   INSERT dbo.Payments(InvoiceId,Method,Amount,CashReceived,TransferReference,ReceivedBy) VALUES(@invoice,@Method,@total,CASE WHEN @Method='Cash' THEN @CashReceived END,CASE WHEN @Method='Transfer' THEN @TransferReference END,@ActorUserId);
   UPDATE t SET Status='Cleaning',StatusChangedAt=SYSUTCDATETIME() FROM dbo.DiningTables t JOIN dbo.SessionTables st ON st.TableId=t.Id JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE s.Id=@SessionId OR s.BillingSessionId=@SessionId;
   UPDATE st SET ReleasedAt=SYSUTCDATETIME() FROM dbo.SessionTables st JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE s.Id=@SessionId OR s.BillingSessionId=@SessionId;
   UPDATE g SET RevokedAt=SYSUTCDATETIME() FROM dbo.GuestSessions g JOIN dbo.DiningSessions s ON s.Id=g.SessionId WHERE s.Id=@SessionId OR s.BillingSessionId=@SessionId;
   UPDATE dbo.DiningSessions SET Status='Closed',ClosedAt=SYSUTCDATETIME(),ClosedBy=@ActorUserId WHERE Id=@SessionId OR BillingSessionId=@SessionId;
   INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,Reason,NewValues) VALUES(@ActorUserId,'Checkout','Invoice',@invoice,@DiscountReason,CONCAT('{"total":',@total,',"discount":',@discount,'}'));
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
