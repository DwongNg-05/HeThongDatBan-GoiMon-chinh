-- PO: one extension; deadline is appointment + 30 minutes, not click time + 15.
-- Keep 043 and its four-argument function unchanged for existing consumers/tests.
CREATE OR ALTER FUNCTION dbo.fn_IsReservationHoldDue(
 @status varchar(20),@arrived datetime2(3),@start datetime2(3),@held datetime2(3),@now datetime2(3)) RETURNS bit
AS
BEGIN
 RETURN CASE WHEN @status='Confirmed' AND @arrived IS NULL
  AND @now>=COALESCE(@held,DATEADD(minute,15,@start)) THEN 1 ELSE 0 END;
END;
GO
CREATE OR ALTER VIEW dbo.v_ReservationHoldState AS
 SELECT r.Id,r.Code,r.CustomerName,r.Phone,r.StartsAt,r.TableId,t.Code AS TableCode,
  COALESCE(r.HoldExtendedUntil,DATEADD(minute,15,r.StartsAt)) AS WarningAt,
  r.HoldExtendedUntil,r.ExtensionCount,
  dbo.fn_IsReservationHoldDue(r.Status,r.ArrivedAt,r.StartsAt,r.HoldExtendedUntil,SYSUTCDATETIME()) AS IsOverdue,
  CONVERT(bit,CASE WHEN r.ExtensionCount=0 AND r.HoldExtendedUntil IS NULL AND SYSUTCDATETIME()>=DATEADD(minute,15,r.StartsAt)
    AND SYSUTCDATETIME()<DATEADD(minute,30,r.StartsAt) THEN 1 ELSE 0 END) AS CanExtend
 FROM dbo.Reservations r LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId
 WHERE r.Status='Confirmed' AND r.ArrivedAt IS NULL
  AND (SYSUTCDATETIME()>=DATEADD(minute,15,r.StartsAt) OR r.HoldExtendedUntil IS NOT NULL);
GO
CREATE OR ALTER VIEW dbo.v_NoShowAlerts AS
 SELECT Id,Code,CustomerName,Phone,StartsAt,TableId,TableCode,WarningAt
 FROM dbo.v_ReservationHoldState WHERE IsOverdue=1;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ExtendReservationHold @ReservationId bigint,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
  DECLARE @status varchar(20),@arrived datetime2(3),@start datetime2(3),@held datetime2(3),@count tinyint,@table int,
   @now datetime2(3)=SYSUTCDATETIME();
  SELECT @status=Status,@arrived=ArrivedAt,@start=StartsAt,@held=HoldExtendedUntil,@count=ExtensionCount,@table=TableId
   FROM dbo.Reservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId;
  IF @status IS NULL OR @status<>'Confirmed' OR @arrived IS NOT NULL
   THROW 51064,N'Lượt đặt đã được xử lý hoặc khách đã tới. Không thể gia hạn.',1;
  IF @count<>0 OR @held IS NOT NULL THROW 51015,N'Mỗi lượt đặt chỉ được gia hạn một lần.',1;
  IF dbo.fn_IsReservationHoldDue(@status,@arrived,@start,@held,@now)=0
   THROW 51065,N'Chỉ được gia hạn khi lượt đặt đang có cảnh báo trễ.',1;
  IF @now>=DATEADD(minute,30,@start)
   THROW 51065,N'Đã hết mốc giờ hẹn cộng 30 phút. Không thể gia hạn; bạn có thể đánh dấu khách không tới.',1;
  IF @table IS NULL OR EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId=@table AND ReleasedAt IS NULL)
   THROW 51064,N'Bàn không còn phù hợp để gia hạn giữ bàn.',1;
  UPDATE dbo.Reservations SET ExtensionCount=1,HoldExtendedUntil=DATEADD(minute,30,@start) WHERE Id=@ReservationId;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,OldTableId,ActorUserId,Reason)
   VALUES(@ReservationId,'Confirmed','Confirmed',@table,@ActorUserId,N'Gia hạn giữ bàn đến giờ hẹn cộng 30 phút');
  -- Reservation interval remains unchanged; extending arrival grace must never free its table.
  UPDATE dbo.DiningTables SET Status='Reserved',StatusChangedAt=@now WHERE Id=@table AND Status='Available';
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_RecordReservationNoShow @ReservationId bigint,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
  DECLARE @status varchar(20),@arrived datetime2(3),@start datetime2(3),@held datetime2(3),@table int,@now datetime2(3)=SYSUTCDATETIME();
  SELECT @status=Status,@arrived=ArrivedAt,@start=StartsAt,@held=HoldExtendedUntil,@table=TableId
   FROM dbo.Reservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId;
  IF @status IS NULL OR @status<>'Confirmed' OR @arrived IS NOT NULL
   THROW 51064,N'Lượt đặt đã được xử lý hoặc khách đã tới. Không thể đánh dấu không tới.',1;
  IF dbo.fn_IsReservationHoldDue(@status,@arrived,@start,@held,@now)=0
   THROW 51065,N'Chưa hết thời hạn giữ bàn. Không thể đánh dấu khách không tới.',1;
  IF EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId=@table AND ReleasedAt IS NULL)
   THROW 51064,N'Bàn đang phục vụ khách. Không thể giải phóng bàn.',1;
  UPDATE dbo.Reservations SET Status='NoShow',NoShowAt=@now WHERE Id=@ReservationId;
  INSERT dbo.ReservationNoShowHistory(ReservationId,ReservationCode,NormalizedPhone,AppointmentAt,NoShowAt,ActorUserId)
   SELECT Id,Code,dbo.fn_NoShowPhone(Phone),StartsAt,@now,@ActorUserId FROM dbo.Reservations WHERE Id=@ReservationId;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,OldTableId,ActorUserId)
   VALUES(@ReservationId,'Confirmed','NoShow',@table,@ActorUserId);
  UPDATE dbo.DiningTables SET Status='Available',StatusChangedAt=@now
   WHERE Id=@table AND Status='Reserved'
   AND NOT EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=@table AND Status IN ('Pending','Confirmed')
       AND StartsAt<=DATEADD(minute,30,@now) AND EndsAt>@now);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_MarkNoShow @ReservationId bigint,@Extend bit,@ActorUserId int AS
BEGIN
 IF @Extend=1 EXEC dbo.usp_ExtendReservationHold @ReservationId,@ActorUserId;
 ELSE EXEC dbo.usp_RecordReservationNoShow @ReservationId,@ActorUserId;
END;
GO
GRANT SELECT ON dbo.v_ReservationHoldState TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ExtendReservationHold TO restaurant_app;
