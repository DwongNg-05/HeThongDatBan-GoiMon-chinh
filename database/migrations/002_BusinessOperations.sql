-- All business writes acquire one short transaction lock for a single restaurant.
-- This deliberate serialization favors correctness at the documented scale (15 staff).
CREATE OR ALTER PROCEDURE dbo.usp_LockOperations
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @result int;
 EXEC @result=sys.sp_getapplock @Resource=N'RestaurantOperations',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
 IF @result<0 THROW 51000,N'Hệ thống đang bận, vui lòng thử lại.',1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_RequirePermission @ActorUserId int, @Permission varchar(100)
AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM dbo.Users u JOIN dbo.RolePermissions rp ON rp.RoleId=u.RoleId
 JOIN dbo.Permissions p ON p.Id=rp.PermissionId
 WHERE u.Id=@ActorUserId AND u.IsActive=1 AND p.Code=@Permission)
 THROW 51001,N'Bạn không có quyền thực hiện thao tác này.',1;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_QueueBookingEmail @ReservationId bigint,@Kind varchar(30)
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @email nvarchar(254),@payload nvarchar(max),@key varchar(100)=CONCAT(@Kind,':',@ReservationId);
 SELECT @email=Email FROM dbo.Reservations WHERE Id=@ReservationId;
 IF @email IS NULL RETURN;
 SET @payload=(SELECT Code,CustomerName,StartsAt,EndsAt,GuestCount,TableId,Status FROM dbo.Reservations WHERE Id=@ReservationId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 IF NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE DedupeKey=@key)
 INSERT dbo.EmailOutbox(ReservationId,MessageType,Recipient,Subject,PayloadJson,DedupeKey)
 VALUES(@ReservationId,@Kind,@email,N'Thông tin đặt bàn',@payload,@key);
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CreateReservation @CustomerName nvarchar(100),@Phone varchar(10),@GuestCount int,@StartsAt datetime2(3),
 @PreferredAreaId int=NULL,@Email nvarchar(254)=NULL,@Notes nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  IF LEN(LTRIM(RTRIM(@CustomerName)))=0 OR @GuestCount NOT BETWEEN 1 AND 20 OR LEN(@Phone)<>10 OR @Phone LIKE '%[^0-9]%'
   THROW 51002,N'Thông tin khách đặt bàn không hợp lệ.',1;
  IF @StartsAt<=SYSUTCDATETIME() THROW 51003,N'Giờ đặt bàn phải ở tương lai.',1;
  DECLARE @local datetime2=DATEADD(hour,7,@StartsAt),@duration int,@ends datetime2(3),@open time(0),@close time(0),@closed bit;
  SELECT @duration=DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1;
  SET @ends=DATEADD(minute,@duration,@StartsAt);
  DECLARE @day date=CONVERT(date,@local),@weekday int=((DATEDIFF(day,CONVERT(date,'19000101'),CONVERT(date,@local))%7)+1);
  SELECT @open=OpensAt,@close=ClosesAt,@closed=IsClosed FROM dbo.OpeningHours WHERE DayOfWeek=@weekday;
  SELECT @open=OpensAt,@close=ClosesAt,@closed=IsClosed FROM dbo.SpecialDates WHERE BusinessDate=@day;
  IF @closed IS NULL OR @closed=1 OR CONVERT(time,@local)<@open
    OR CONVERT(date,DATEADD(hour,7,@ends))<>@day OR CONVERT(time,DATEADD(hour,7,@ends))>@close
    OR DATEPART(minute,@local)%30<>0 OR DATEPART(second,@local)<>0 OR DATEPART(millisecond,@local)<>0
   THROW 51004,N'Khung giờ không nằm trong giờ mở cửa hoặc không đúng bước 30 phút.',1;
  IF (SELECT COUNT(*) FROM dbo.Reservations WHERE Phone=@Phone AND Status='Pending')>=3
   THROW 51005,N'Mỗi số điện thoại chỉ được có tối đa 3 đặt bàn chờ xác nhận.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
   WHERE t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@GuestCount AND (@PreferredAreaId IS NULL OR t.AreaId=@PreferredAreaId)
   AND NOT EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status IN ('Confirmed','Arrived')
       AND r.StartsAt<@ends AND r.EndsAt>@StartsAt))
   THROW 51006,N'Không còn bàn phù hợp trong khung giờ này.',1;
  DECLARE @code char(6);
  SET @code=UPPER(LEFT(CONVERT(varchar(64),CRYPT_GEN_RANDOM(16),2),6));
  WHILE EXISTS(SELECT 1 FROM dbo.Reservations WHERE Code=@code)
   SET @code=UPPER(LEFT(CONVERT(varchar(64),CRYPT_GEN_RANDOM(16),2),6));
  INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,StartsAt,EndsAt,Notes)
   VALUES(@code,LTRIM(RTRIM(@CustomerName)),@Phone,NULLIF(LTRIM(RTRIM(@Email)),''),@GuestCount,@PreferredAreaId,@StartsAt,@ends,@Notes);
  DECLARE @id bigint=SCOPE_IDENTITY();
  INSERT dbo.ReservationEvents(ReservationId,ToStatus) VALUES(@id,'Pending');
  EXEC dbo.usp_QueueBookingEmail @id,'BookingReceived';
  SELECT @id AS ReservationId,@code AS Code;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ConfirmReservation @ReservationId bigint,@TableId int,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';

  DECLARE @from varchar(20),@oldTable int,@start datetime2(3),@end datetime2(3),@guests int;
  SELECT @from=Status,@oldTable=TableId,@start=StartsAt,@end=EndsAt,@guests=GuestCount FROM dbo.Reservations WHERE Id=@ReservationId;
  IF @from IS NULL OR @from NOT IN ('Pending','Confirmed') OR @start<=SYSUTCDATETIME()
   THROW 51007,N'Đặt bàn không còn được xác nhận hoặc đổi bàn.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
   WHERE t.Id=@TableId AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@guests)
   THROW 51008,N'Bàn không hoạt động hoặc không đủ sức chứa.',1;
  IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE Id<>@ReservationId AND TableId=@TableId
   AND Status IN ('Confirmed','Arrived') AND StartsAt<@end AND EndsAt>@start)
   THROW 51009,N'Bàn đã được đặt trong khung giờ giao nhau.',1;
  UPDATE dbo.Reservations SET Status='Confirmed',TableId=@TableId,ConfirmedAt=SYSUTCDATETIME(),ConfirmedBy=@ActorUserId WHERE Id=@ReservationId;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,OldTableId,NewTableId,ActorUserId)
   VALUES(@ReservationId,@from,'Confirmed',@oldTable,@TableId,@ActorUserId);
  EXEC dbo.usp_QueueBookingEmail @ReservationId,'BookingConfirmed';
  UPDATE dbo.DiningTables SET Status='Reserved',StatusChangedAt=SYSUTCDATETIME()
   WHERE Id=@TableId AND Status='Available' AND @start<=DATEADD(minute,30,SYSUTCDATETIME());
  IF @oldTable IS NOT NULL AND @oldTable<>@TableId
   UPDATE dbo.DiningTables SET Status='Available',StatusChangedAt=SYSUTCDATETIME()
   WHERE Id=@oldTable AND Status='Reserved' AND NOT EXISTS(SELECT 1 FROM dbo.Reservations
     WHERE TableId=@oldTable AND Status='Confirmed' AND StartsAt<=DATEADD(minute,30,SYSUTCDATETIME()) AND EndsAt>SYSUTCDATETIME());
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_RejectReservation @ReservationId bigint,@Reason varchar(30),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';

  IF @Reason NOT IN ('NoTable','OutsideHours','Unreachable') THROW 51010,N'Lý do từ chối không hợp lệ.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.Reservations WHERE Id=@ReservationId AND Status='Pending')
   THROW 51011,N'Chỉ từ chối được đặt bàn đang chờ.',1;
  UPDATE dbo.Reservations SET Status='Rejected',RejectionReason=@Reason WHERE Id=@ReservationId;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,ActorUserId,Reason) VALUES(@ReservationId,'Pending','Rejected',@ActorUserId,@Reason);
  EXEC dbo.usp_QueueBookingEmail @ReservationId,'BookingRejected';
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CancelReservation @Code char(6),@Phone varchar(10),@Reason nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  DECLARE @id bigint,@status varchar(20),@start datetime2(3),@table int,@cutoff int;
  SELECT @id=Id,@status=Status,@start=StartsAt,@table=TableId FROM dbo.Reservations WHERE Code=@Code AND Phone=@Phone;
  SELECT @cutoff=CancelCutoffMinutes FROM dbo.RestaurantSettings WHERE Id=1;
  IF @id IS NULL THROW 51012,N'Mã đặt bàn hoặc số điện thoại không đúng.',1;
  IF @status NOT IN ('Pending','Confirmed') OR @start<DATEADD(minute,@cutoff,SYSUTCDATETIME())
   THROW 51013,N'Không thể tự huỷ lúc này, vui lòng gọi nhà hàng.',1;
  UPDATE dbo.Reservations SET Status='Cancelled',CancelledAt=SYSUTCDATETIME(),CancelReason=@Reason WHERE Id=@id;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,Reason) VALUES(@id,@status,'Cancelled',@Reason);
  UPDATE dbo.EmailOutbox SET Status='Cancelled' WHERE ReservationId=@id AND MessageType='BookingReminder' AND Status IN ('Pending','Processing');
  EXEC dbo.usp_QueueBookingEmail @id,'BookingCancelled';
  UPDATE dbo.DiningTables SET Status='Available',StatusChangedAt=SYSUTCDATETIME()
   WHERE Id=@table AND Status='Reserved' AND NOT EXISTS(SELECT 1 FROM dbo.Reservations
    WHERE TableId=@table AND Status='Confirmed' AND StartsAt<=DATEADD(minute,30,SYSUTCDATETIME()) AND EndsAt>SYSUTCDATETIME());
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_MarkNoShow @ReservationId bigint,@Extend bit,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';

  DECLARE @start datetime2(3),@held datetime2(3),@table int,@extensions int;
  SELECT @start=StartsAt,@held=HoldExtendedUntil,@table=TableId,@extensions=ExtensionCount FROM dbo.Reservations WHERE Id=@ReservationId AND Status='Confirmed';
  DECLARE @grace int=(SELECT NoShowGraceMinutes FROM dbo.RestaurantSettings WHERE Id=1);
  IF @start IS NULL OR SYSUTCDATETIME()<COALESCE(@held,DATEADD(minute,@grace,@start))
   THROW 51014,N'Chưa hết thời gian giữ bàn.',1;
  IF @Extend=1
  BEGIN
   IF @extensions>=1 THROW 51015,N'Chỉ gia hạn giữ bàn được một lần.',1;
   UPDATE dbo.Reservations SET ExtensionCount=1,HoldExtendedUntil=DATEADD(minute,15,SYSUTCDATETIME()) WHERE Id=@ReservationId;
   INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,ActorUserId,Reason) VALUES(@ReservationId,'Confirmed','Confirmed',@ActorUserId,N'Gia hạn 15 phút');
  END
  ELSE
  BEGIN
   UPDATE dbo.Reservations SET Status='NoShow',NoShowAt=SYSUTCDATETIME() WHERE Id=@ReservationId;
   INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,ActorUserId) VALUES(@ReservationId,'Confirmed','NoShow',@ActorUserId);
   UPDATE dbo.DiningTables SET Status='Available',StatusChangedAt=SYSUTCDATETIME() WHERE Id=@table AND Status='Reserved'
    AND NOT EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=@table AND Status='Confirmed' AND StartsAt<=DATEADD(minute,30,SYSUTCDATETIME()) AND EndsAt>SYSUTCDATETIME());
  END;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_OpenShift @Name nvarchar(100),@OpeningCash decimal(18,0),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Payments.Manage';

  INSERT dbo.Shifts(Name,BusinessDate,OpenedBy,OpeningCash) VALUES(@Name,CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())),@ActorUserId,@OpeningCash);
  DECLARE @id bigint=SCOPE_IDENTITY();
  INSERT dbo.ShiftEvents(ShiftId,Action,ActorUserId) VALUES(@id,'Opened',@ActorUserId);
  SELECT @id AS ShiftId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_OpenSession @TableId int,@GuestCount int,@ActorUserId int,@ReservationId bigint=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Sessions.Manage';

  DECLARE @shift bigint=(SELECT Id FROM dbo.Shifts WHERE Status='Open');
  IF @shift IS NULL THROW 51016,N'Cần mở ca trước khi đón khách.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
   WHERE t.Id=@TableId AND t.IsActive=1 AND a.IsActive=1 AND t.Status IN ('Available','Reserved') AND t.MaxCapacity>=@GuestCount)
   THROW 51017,N'Bàn không sẵn sàng hoặc không đủ sức chứa.',1;
  IF EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId=@TableId AND ReleasedAt IS NULL)
   THROW 51018,N'Bàn đã có phiên đang mở.',1;
  IF @ReservationId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Reservations
   WHERE Id=@ReservationId AND TableId=@TableId AND Status='Confirmed' AND GuestCount=@GuestCount
    AND StartsAt<=DATEADD(minute,30,SYSUTCDATETIME()) AND EndsAt>SYSUTCDATETIME())
   THROW 51019,N'Đặt bàn không hợp lệ để đón khách lúc này.',1;
  IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=@TableId AND Status='Confirmed'
   AND (@ReservationId IS NULL OR Id<>@ReservationId)
   AND StartsAt<DATEADD(minute,(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1),SYSUTCDATETIME())
   AND EndsAt>SYSUTCDATETIME()) THROW 51020,N'Bàn có lịch đặt sắp tới, hãy chọn bàn khác.',1;
  INSERT dbo.DiningSessions(ReservationId,ShiftId,GuestCount,OpenedBy) VALUES(@ReservationId,@shift,@GuestCount,@ActorUserId);
  DECLARE @id bigint=SCOPE_IDENTITY();
  INSERT dbo.SessionTables(SessionId,TableId) VALUES(@id,@TableId);
  UPDATE dbo.DiningTables SET Status='Serving',StatusChangedAt=SYSUTCDATETIME() WHERE Id=@TableId;
  IF @ReservationId IS NOT NULL
  BEGIN
   UPDATE dbo.Reservations SET Status='Arrived',ArrivedAt=SYSUTCDATETIME() WHERE Id=@ReservationId;
   INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,ActorUserId) VALUES(@ReservationId,'Confirmed','Arrived',@ActorUserId);
  END;
  SELECT @id AS SessionId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_RotateTableQr @TableId int,@TokenHash binary(32),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';

  IF NOT EXISTS(SELECT 1 FROM dbo.DiningTables WHERE Id=@TableId AND IsActive=1) THROW 51021,N'Bàn không hoạt động.',1;
  UPDATE gs SET RevokedAt=SYSUTCDATETIME() FROM dbo.GuestSessions gs JOIN dbo.TableQrCodes q ON q.Id=gs.TableQrCodeId WHERE q.TableId=@TableId AND gs.RevokedAt IS NULL;
  UPDATE dbo.TableQrCodes SET RevokedAt=SYSUTCDATETIME() WHERE TableId=@TableId AND RevokedAt IS NULL;
  INSERT dbo.TableQrCodes(TableId,TokenHash,CreatedBy) VALUES(@TableId,@TokenHash,@ActorUserId);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_OpenGuestSession @QrTokenHash binary(32),@GuestTokenHash binary(32)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  DECLARE @qr bigint,@session bigint;
  SELECT @qr=q.Id,@session=s.Id FROM dbo.TableQrCodes q JOIN dbo.DiningTables t ON t.Id=q.TableId
   JOIN dbo.SessionTables st ON st.TableId=t.Id AND st.ReleasedAt IS NULL
   JOIN dbo.DiningSessions s ON s.Id=st.SessionId
   WHERE q.TokenHash=@QrTokenHash AND q.RevokedAt IS NULL AND t.Status='Serving' AND s.Status='Open';
  IF @session IS NULL THROW 51022,N'Mã QR không còn hiệu lực hoặc bàn chưa được phục vụ. Vui lòng gọi nhân viên.',1;
  INSERT dbo.GuestSessions(SessionId,TableQrCodeId,TokenHash,ExpiresAt) VALUES(@session,@qr,@GuestTokenHash,DATEADD(hour,12,SYSUTCDATETIME()));
  SELECT @session AS SessionId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_SubmitOrder @SessionId bigint,@RequestId uniqueidentifier,@ItemsJson nvarchar(max),
 @ActorUserId int=NULL,@GuestTokenHash binary(32)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  DECLARE @guest bigint,@root bigint,@table int;
  IF @ActorUserId IS NOT NULL EXEC dbo.usp_RequirePermission @ActorUserId,'Orders.Manage';
  ELSE
  BEGIN
   SELECT @guest=g.Id FROM dbo.GuestSessions g JOIN dbo.TableQrCodes q ON q.Id=g.TableQrCodeId
    WHERE g.TokenHash=@GuestTokenHash AND g.SessionId=@SessionId AND g.RevokedAt IS NULL
     AND g.ExpiresAt>SYSUTCDATETIME() AND q.RevokedAt IS NULL;
   IF @guest IS NULL THROW 51023,N'Phiên gọi món đã hết hạn.',1;
  END;
  IF EXISTS(SELECT 1 FROM dbo.OrderBatches WHERE RequestId=@RequestId)
  BEGIN
   IF NOT EXISTS(SELECT 1 FROM dbo.OrderBatches WHERE RequestId=@RequestId AND SessionId=@SessionId)
    THROW 51024,N'Mã yêu cầu thuộc phiên khác.',1;
   SELECT Id AS BatchId FROM dbo.OrderBatches WHERE RequestId=@RequestId;
  END
  ELSE
  BEGIN
   SELECT @root=COALESCE(BillingSessionId,Id) FROM dbo.DiningSessions WHERE Id=@SessionId AND Status='Open';
   IF @root IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE Id=@root AND Status='Open')
    THROW 51025,N'Phiên đã đóng hoặc đang chờ thanh toán.',1;
   SELECT @table=TableId FROM dbo.SessionTables WHERE SessionId=@SessionId AND ReleasedAt IS NULL;
   IF ISJSON(@ItemsJson)<>1 OR LEFT(LTRIM(@ItemsJson),1)<>'[' THROW 51026,N'Danh sách món không hợp lệ.',1;
   DECLARE @items TABLE(MenuItemId int,Quantity int,Notes nvarchar(max));
   INSERT @items SELECT MenuItemId,Quantity,Notes FROM OPENJSON(@ItemsJson) WITH(MenuItemId int,Quantity int,Notes nvarchar(max));
   IF NOT EXISTS(SELECT 1 FROM @items) OR (SELECT COUNT(*) FROM @items)>100
    OR EXISTS(SELECT 1 FROM @items WHERE MenuItemId IS NULL OR Quantity IS NULL OR Quantity NOT BETWEEN 1 AND 99 OR LEN(Notes)>200)
    THROW 51027,N'Số lượng hoặc ghi chú món không hợp lệ.',1;
   IF EXISTS(SELECT 1 FROM @items i LEFT JOIN dbo.MenuItems m ON m.Id=i.MenuItemId LEFT JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
    WHERE m.Id IS NULL OR m.IsActive=0 OR c.IsActive=0 OR (m.IsSoldOut=1 AND m.SoldOutBusinessDate=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))))
    THROW 51028,N'Có món đã hết hoặc ngừng bán. Vui lòng kiểm tra lại giỏ món.',1;
   DECLARE @number int=(SELECT COALESCE(MAX(BatchNumber),0)+1 FROM dbo.OrderBatches WHERE SessionId=@SessionId);
   INSERT dbo.OrderBatches(SessionId,BatchNumber,RequestId,CreatedBy,GuestSessionId) VALUES(@SessionId,@number,@RequestId,@ActorUserId,@guest);
   DECLARE @batch bigint=SCOPE_IDENTITY();
   INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,Notes,EstimatedPrepMinutes)
    SELECT @batch,m.Id,@table,m.Name,m.Unit,m.Price,i.Quantity,i.Notes,m.EstimatedPrepMinutes FROM @items i JOIN dbo.MenuItems m ON m.Id=i.MenuItemId;
   INSERT dbo.OrderItemEvents(OrderItemId,ToStatus,ActorUserId) SELECT Id,'Pending',@ActorUserId FROM dbo.OrderItems WHERE BatchId=@batch;
   INSERT dbo.Notifications(Kind,Title,Body,TargetRoleId,EntityType,EntityId)
    VALUES('NewOrder',N'Có phiếu gọi món mới',CONCAT(N'Bàn ',(SELECT Code FROM dbo.DiningTables WHERE Id=@table)),3,'OrderBatch',@batch);
   SELECT @batch AS BatchId;
  END;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_TransitionOrderItem @OrderItemId bigint,@ToStatus varchar(20),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  DECLARE @from varchar(20),@sessionStatus varchar(20),@table varchar(20),@name nvarchar(150);
  SELECT @from=i.Status,@sessionStatus=s.Status,@table=t.Code,@name=i.ItemName FROM dbo.OrderItems i
   JOIN dbo.OrderBatches b ON b.Id=i.BatchId JOIN dbo.DiningSessions s ON s.Id=b.SessionId
   JOIN dbo.DiningTables t ON t.Id=i.OriginalTableId WHERE i.Id=@OrderItemId;
  IF @ToStatus='Served' EXEC dbo.usp_RequirePermission @ActorUserId,'Orders.Serve';
  ELSE EXEC dbo.usp_RequirePermission @ActorUserId,'Kitchen.Manage';
  IF @from IS NULL OR @sessionStatus='Closed' THROW 51029,N'Món không tồn tại hoặc phiên đã đóng.',1;
  IF @from<>@ToStatus
  BEGIN
   IF NOT ((@from='Pending' AND @ToStatus='Preparing') OR (@from='Preparing' AND @ToStatus='Ready') OR (@from='Ready' AND @ToStatus='Served'))
    THROW 51030,N'Không được chuyển lùi hoặc bỏ qua trạng thái món.',1;
   UPDATE dbo.OrderItems SET Status=@ToStatus,
    PreparingAt=CASE WHEN @ToStatus='Preparing' THEN SYSUTCDATETIME() ELSE PreparingAt END,
    ReadyAt=CASE WHEN @ToStatus='Ready' THEN SYSUTCDATETIME() ELSE ReadyAt END,
    ServedAt=CASE WHEN @ToStatus='Served' THEN SYSUTCDATETIME() ELSE ServedAt END,
    ServedBy=CASE WHEN @ToStatus='Served' THEN @ActorUserId ELSE ServedBy END WHERE Id=@OrderItemId;
   INSERT dbo.OrderItemEvents(OrderItemId,FromStatus,ToStatus,ActorUserId) VALUES(@OrderItemId,@from,@ToStatus,@ActorUserId);
   IF @ToStatus='Ready'
    INSERT dbo.Notifications(Kind,Title,Body,TargetRoleId,EntityType,EntityId) VALUES('DishReady',N'Món đã xong',CONCAT(@table,N': ',@name),2,'OrderItem',@OrderItemId);
  END;
  SELECT CASE WHEN @from=@ToStatus THEN 0 ELSE 1 END AS Changed;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CancelOrderItem @OrderItemId bigint,@Reason varchar(30),@ActorUserId int,@Note nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Orders.Manage';

  DECLARE @status varchar(20),@sessionStatus varchar(20);
  SELECT @status=i.Status,@sessionStatus=s.Status FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id=i.BatchId
   JOIN dbo.DiningSessions s ON s.Id=b.SessionId WHERE i.Id=@OrderItemId;
  IF @status IS NULL OR @status='Cancelled' OR @sessionStatus='Closed' THROW 51031,N'Món không còn được huỷ.',1;
  IF @status<>'Pending' EXEC dbo.usp_RequirePermission @ActorUserId,'Orders.CancelPrepared';
  IF @Reason NOT IN ('ChangedMind','Mistake','SoldOut','ManagerOverride') THROW 51032,N'Lý do huỷ không hợp lệ.',1;
  UPDATE dbo.OrderItems SET Status='Cancelled',CancelledAt=SYSUTCDATETIME(),CancelledBy=@ActorUserId,
   CancelReason=@Reason,CancellationNote=@Note,ChargeWhenCancelled=CASE WHEN @status='Pending' THEN 0 ELSE 1 END WHERE Id=@OrderItemId;
  INSERT dbo.OrderItemEvents(OrderItemId,FromStatus,ToStatus,ActorUserId,Reason) VALUES(@OrderItemId,@status,'Cancelled',@ActorUserId,CONCAT(@Reason,N' ',@Note));
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_SetPaymentState @SessionId bigint,@Awaiting bit,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Sessions.Manage';

  IF NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE Id=@SessionId AND BillingSessionId IS NULL AND Status<>'Closed')
   THROW 51033,N'Hãy chọn phiên chính còn mở.',1;
  UPDATE dbo.DiningSessions SET Status=CASE WHEN @Awaiting=1 THEN 'AwaitingPayment' ELSE 'Open' END,
   PaymentRequestedAt=CASE WHEN @Awaiting=1 THEN SYSUTCDATETIME() ELSE NULL END
   WHERE Id=@SessionId OR BillingSessionId=@SessionId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_MergeSessions @MainSessionId bigint,@ChildSessionId bigint,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Sessions.Manage';

  IF @MainSessionId=@ChildSessionId OR (SELECT COUNT(*) FROM dbo.DiningSessions WHERE Id IN (@MainSessionId,@ChildSessionId) AND Status='Open' AND BillingSessionId IS NULL)<>2
   THROW 51034,N'Chỉ gộp hai phiên chính đang phục vụ.',1;
  IF EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE BillingSessionId=@ChildSessionId) THROW 51035,N'Hãy huỷ gộp nhóm bàn phụ trước.',1;
  IF (SELECT COUNT(DISTINCT t.AreaId) FROM dbo.SessionTables st JOIN dbo.DiningTables t ON t.Id=st.TableId WHERE st.SessionId IN (@MainSessionId,@ChildSessionId))<>1
   THROW 51036,N'Chỉ gộp bàn trong cùng khu vực.',1;
  IF (SELECT COUNT(DISTINCT ShiftId) FROM dbo.DiningSessions WHERE Id IN (@MainSessionId,@ChildSessionId))<>1
   THROW 51037,N'Hai bàn phải thuộc cùng ca.',1;
  UPDATE dbo.DiningSessions SET BillingSessionId=@MainSessionId WHERE Id=@ChildSessionId;
  INSERT dbo.SessionMerges(MainSessionId,ChildSessionId,MergedBy) VALUES(@MainSessionId,@ChildSessionId,@ActorUserId);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_UnmergeSession @ChildSessionId bigint,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Sessions.Manage';

  DECLARE @main bigint=(SELECT BillingSessionId FROM dbo.DiningSessions WHERE Id=@ChildSessionId AND Status='Open');
  IF @main IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE Id=@main AND Status='Open')
   THROW 51038,N'Chỉ huỷ gộp khi cả nhóm đang phục vụ và chưa thanh toán.',1;
  UPDATE dbo.DiningSessions SET BillingSessionId=NULL WHERE Id=@ChildSessionId;
  UPDATE dbo.SessionMerges SET UndoneAt=SYSUTCDATETIME(),UndoneBy=@ActorUserId WHERE ChildSessionId=@ChildSessionId AND UndoneAt IS NULL;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_UpdateMenuPrice @MenuItemId int,@Price decimal(18,0),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';

  DECLARE @old decimal(18,0)=(SELECT Price FROM dbo.MenuItems WHERE Id=@MenuItemId);
  IF @old IS NULL THROW 51039,N'Món không tồn tại.',1;
  UPDATE dbo.MenuItems SET Price=@Price,UpdatedAt=SYSUTCDATETIME() WHERE Id=@MenuItemId;
  INSERT dbo.MenuPriceHistory(MenuItemId,OldPrice,NewPrice,ChangedBy) VALUES(@MenuItemId,@old,@Price,@ActorUserId);
  INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,OldValues,NewValues)
   VALUES(@ActorUserId,'PriceChanged','MenuItem',@MenuItemId,CONCAT('{"price":',@old,'}'),CONCAT('{"price":',@Price,'}'));
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_SetMenuAvailability @MenuItemId int,@IsSoldOut bit,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Menu.Availability';

  DECLARE @date date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
  IF NOT EXISTS(SELECT 1 FROM dbo.MenuItems WHERE Id=@MenuItemId) THROW 51040,N'Món không tồn tại.',1;
  UPDATE dbo.MenuItems SET IsSoldOut=@IsSoldOut,SoldOutBusinessDate=@date,UpdatedAt=SYSUTCDATETIME() WHERE Id=@MenuItemId;
  INSERT dbo.MenuAvailabilityEvents(MenuItemId,IsSoldOut,BusinessDate,ChangedBy) VALUES(@MenuItemId,@IsSoldOut,@date,@ActorUserId);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

