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
CREATE OR ALTER PROCEDURE dbo.usp_CleanTable @TableId int,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Sessions.Manage';

  IF NOT EXISTS(SELECT 1 FROM dbo.DiningTables WHERE Id=@TableId AND Status='Cleaning')
   THROW 51047,N'Chỉ báo dọn xong cho bàn đang dọn.',1;
  UPDATE dbo.DiningTables SET Status=CASE WHEN EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=@TableId AND Status='Confirmed'
    AND StartsAt<=DATEADD(minute,30,SYSUTCDATETIME()) AND EndsAt>SYSUTCDATETIME()) THEN 'Reserved' ELSE 'Available' END,
    StatusChangedAt=SYSUTCDATETIME() WHERE Id=@TableId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CloseShift @ShiftId bigint,@CountedCash decimal(18,0),@ActorUserId int,@Explanation nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Payments.Manage';

  IF NOT EXISTS(SELECT 1 FROM dbo.Shifts WHERE Id=@ShiftId AND Status='Open') THROW 51048,N'Ca không đang mở.',1;
  IF @CountedCash<0 THROW 51049,N'Tiền kiểm đếm không hợp lệ.',1;
  IF EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE ShiftId=@ShiftId AND Status<>'Closed')
   THROW 51050,N'Còn bàn đang phục vụ hoặc chưa thanh toán, chưa thể chốt ca.',1;
  DECLARE @cash decimal(18,0),@transfer decimal(18,0),@discount decimal(18,0),@count int,@expected decimal(18,0);
  SELECT @cash=COALESCE(SUM(CASE WHEN p.Method='Cash' THEN p.Amount ELSE 0 END),0),
   @transfer=COALESCE(SUM(CASE WHEN p.Method='Transfer' THEN p.Amount ELSE 0 END),0),
   @discount=COALESCE(SUM(i.DiscountAmount),0),@count=COUNT(*)
   FROM dbo.Invoices i JOIN dbo.Payments p ON p.InvoiceId=i.Id WHERE i.ShiftId=@ShiftId AND i.Status='Paid';
  SET @expected=@cash+(SELECT OpeningCash FROM dbo.Shifts WHERE Id=@ShiftId);
  IF ABS(@CountedCash-@expected)>50000 AND NULLIF(LTRIM(RTRIM(@Explanation)),'') IS NULL
   THROW 51051,N'Chênh lệch quá 50.000 VND phải có giải trình.',1;
  UPDATE dbo.Shifts SET Status='Closed',ClosedAt=SYSUTCDATETIME(),ClosedBy=@ActorUserId,CountedCash=@CountedCash,ExpectedCash=@expected,
   Explanation=@Explanation,InvoiceCount=@count,Revenue=@cash+@transfer,CashRevenue=@cash,TransferRevenue=@transfer,DiscountTotal=@discount,
   ServedTableCount=(SELECT COUNT(*) FROM dbo.SessionTables st JOIN dbo.DiningSessions s ON s.Id=st.SessionId WHERE s.ShiftId=@ShiftId)
   WHERE Id=@ShiftId;
  DECLARE @snapshot nvarchar(max)=(SELECT InvoiceCount,Revenue,CashRevenue,TransferRevenue,DiscountTotal,CountedCash,ExpectedCash,CashDifference,Explanation
   FROM dbo.Shifts WHERE Id=@ShiftId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  INSERT dbo.ShiftEvents(ShiftId,Action,ActorUserId,Reason,SnapshotJson) VALUES(@ShiftId,'Closed',@ActorUserId,@Explanation,@snapshot);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ReopenShift @ShiftId bigint,@Reason nvarchar(500),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Shifts.Reopen';

  IF NULLIF(LTRIM(RTRIM(@Reason)),'') IS NULL THROW 51052,N'Phải nhập lý do mở lại ca.',1;
  IF EXISTS(SELECT 1 FROM dbo.Shifts WHERE Status='Open') THROW 51053,N'Hãy đóng ca đang mở trước.',1;
  UPDATE dbo.Shifts SET Status='Open',ClosedAt=NULL,ClosedBy=NULL WHERE Id=@ShiftId AND Status='Closed';
  IF @@ROWCOUNT=0 THROW 51054,N'Không tìm thấy ca đã đóng.',1;
  INSERT dbo.ShiftEvents(ShiftId,Action,ActorUserId,Reason) VALUES(@ShiftId,'Reopened',@ActorUserId,@Reason);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_VoidInvoice @InvoiceId bigint,@Reason nvarchar(500),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Invoices.Void';

  IF NULLIF(LTRIM(RTRIM(@Reason)),'') IS NULL THROW 51055,N'Phải nhập lý do huỷ hoá đơn.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.Invoices i JOIN dbo.Shifts s ON s.Id=i.ShiftId WHERE i.Id=@InvoiceId AND i.Status='Paid' AND s.Status='Open')
   THROW 51056,N'Hoá đơn không hợp lệ hoặc ca đã chốt. Quản lý phải mở lại ca trước.',1;
  UPDATE dbo.Invoices SET Status='Voided',VoidedBy=@ActorUserId,VoidedAt=SYSUTCDATETIME(),VoidReason=@Reason WHERE Id=@InvoiceId;
  INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,Reason) VALUES(@ActorUserId,'InvoiceVoided','Invoice',@InvoiceId,@Reason);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_RunMaintenance
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  DECLARE @now datetime2(3)=SYSUTCDATETIME(),@date date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
  INSERT dbo.MenuAvailabilityEvents(MenuItemId,IsSoldOut,BusinessDate)
    SELECT Id,0,@date FROM dbo.MenuItems WHERE IsSoldOut=1 AND (SoldOutBusinessDate<@date OR SoldOutBusinessDate IS NULL);
  UPDATE dbo.MenuItems SET IsSoldOut=0,SoldOutBusinessDate=@date WHERE IsSoldOut=1 AND (SoldOutBusinessDate<@date OR SoldOutBusinessDate IS NULL);
  UPDATE t SET Status=CASE WHEN EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status='Confirmed'
    AND r.StartsAt<=DATEADD(minute,30,@now) AND r.EndsAt>@now) THEN 'Reserved' ELSE 'Available' END
   FROM dbo.DiningTables t WHERE t.Status IN ('Available','Reserved');
  INSERT dbo.EmailOutbox(ReservationId,MessageType,Recipient,Subject,PayloadJson,DedupeKey)
   SELECT r.Id,'BookingReminder',r.Email,N'Nhắc lịch đặt bàn',
    (SELECT r.Code,r.StartsAt,r.GuestCount,r.TableId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),CONCAT('BookingReminder:',r.Id)
    FROM dbo.Reservations r WHERE r.Status='Confirmed' AND r.Email IS NOT NULL
    AND r.StartsAt>@now AND r.StartsAt<=DATEADD(minute,(SELECT ReminderLeadMinutes FROM dbo.RestaurantSettings WHERE Id=1),@now)
    AND NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox o WHERE o.DedupeKey=CONCAT('BookingReminder:',r.Id));
  UPDATE o SET Status='Cancelled' FROM dbo.EmailOutbox o JOIN dbo.Reservations r ON r.Id=o.ReservationId
    WHERE o.MessageType='BookingReminder' AND o.Status IN ('Pending','Processing') AND (r.Status<>'Confirmed' OR r.StartsAt<=@now);
  DELETE dbo.LookupAttempts WHERE AttemptedAt<DATEADD(day,-1,@now);
  DELETE dbo.LoginAttempts WHERE AttemptedAt<DATEADD(month,-12,@now);
  DELETE dbo.RefreshTokens WHERE ExpiresAt<DATEADD(day,-30,@now);
  -- Physically delete expired booking personal data; retain financial/order snapshots.
  DECLARE @old TABLE(Id bigint PRIMARY KEY);
  INSERT @old SELECT r.Id FROM dbo.Reservations r WHERE r.EndsAt<DATEADD(month,-12,@now)
   AND NOT EXISTS(SELECT 1 FROM dbo.DiningSessions s WHERE s.ReservationId=r.Id AND s.Status<>'Closed');
  UPDATE s SET ReservationId=NULL FROM dbo.DiningSessions s JOIN @old o ON o.Id=s.ReservationId;
  DELETE e FROM dbo.EmailOutbox e JOIN @old o ON o.Id=e.ReservationId;
  DELETE e FROM dbo.ReservationEvents e JOIN @old o ON o.Id=e.ReservationId;
  DELETE r FROM dbo.Reservations r JOIN @old o ON o.Id=r.Id;
  INSERT dbo.ScheduledJobRuns(JobName,ScheduledFor,CompletedAt,Status) VALUES('Maintenance',@now,SYSUTCDATETIME(),'Succeeded');
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ClaimEmail
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  DECLARE @id bigint=(SELECT TOP(1) Id FROM dbo.EmailOutbox WHERE AttemptCount<4
   AND ((Status='Pending' AND NextAttemptAt<=SYSUTCDATETIME()) OR (Status='Processing' AND LockedUntil<SYSUTCDATETIME()))
   ORDER BY NextAttemptAt,Id);
  IF @id IS NOT NULL
  BEGIN
   UPDATE dbo.EmailOutbox SET Status='Processing',AttemptCount=AttemptCount+1,LockedUntil=DATEADD(minute,2,SYSUTCDATETIME())
    OUTPUT inserted.Id,inserted.AttemptCount,inserted.Recipient,inserted.Subject,inserted.PayloadJson,inserted.MessageType WHERE Id=@id;
  END;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CompleteEmail @EmailId bigint,@AttemptNumber int,@Succeeded bit,@Error nvarchar(2000)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  UPDATE dbo.EmailOutbox SET Status=CASE WHEN @Succeeded=1 THEN 'Sent' WHEN AttemptCount>=4 THEN 'Failed' ELSE 'Pending' END,
   SentAt=CASE WHEN @Succeeded=1 THEN SYSUTCDATETIME() ELSE NULL END,LastError=@Error,LockedUntil=NULL,NextAttemptAt=DATEADD(minute,5,SYSUTCDATETIME())
   WHERE Id=@EmailId AND AttemptCount=@AttemptNumber AND Status='Processing';
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

