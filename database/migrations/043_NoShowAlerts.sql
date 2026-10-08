-- Inclusive 15-minute threshold. Historical phone snapshots survive booking deletion.
CREATE OR ALTER FUNCTION dbo.fn_NoShowPhone(@phone nvarchar(100)) RETURNS nvarchar(100)
AS
BEGIN
 DECLARE @p nvarchar(100)=REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@phone,' ',''),'.',''),'-',''),'(',''),')',''),CHAR(9),''),NCHAR(160),'');
 IF LEFT(@p,3)='+84' SET @p='0'+SUBSTRING(@p,4,100);
 ELSE IF LEFT(@p,4)='0084' SET @p='0'+SUBSTRING(@p,5,100);
 RETURN @p;
END;
GO
CREATE OR ALTER FUNCTION dbo.fn_IsNoShowDue(@status varchar(20),@arrived datetime2(3),@start datetime2(3),@now datetime2(3)) RETURNS bit
AS
BEGIN
 RETURN CASE WHEN @status='Confirmed' AND @arrived IS NULL AND @now>=DATEADD(minute,15,@start) THEN 1 ELSE 0 END;
END;
GO
CREATE TABLE dbo.ReservationNoShowHistory(
 Id bigint IDENTITY PRIMARY KEY,
 ReservationId bigint NOT NULL UNIQUE,
 ReservationCode varchar(20) NOT NULL,
 NormalizedPhone nvarchar(100) NOT NULL,
 AppointmentAt datetime2(3) NOT NULL,
 NoShowAt datetime2(3) NOT NULL,
 ActorUserId int NULL REFERENCES dbo.Users(Id)
);
CREATE INDEX IX_NoShowHistory_Phone ON dbo.ReservationNoShowHistory(NormalizedPhone,NoShowAt DESC);
INSERT dbo.ReservationNoShowHistory(ReservationId,ReservationCode,NormalizedPhone,AppointmentAt,NoShowAt)
 SELECT Id,Code,dbo.fn_NoShowPhone(Phone),StartsAt,NoShowAt FROM dbo.Reservations WHERE Status='NoShow' AND NoShowAt IS NOT NULL;
GO
CREATE OR ALTER VIEW dbo.v_NoShowAlerts AS
 SELECT r.Id,r.Code,r.CustomerName,r.Phone,r.StartsAt,r.TableId,t.Code AS TableCode,
 DATEADD(minute,15,r.StartsAt) AS WarningAt
 FROM dbo.Reservations r LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId
 WHERE dbo.fn_IsNoShowDue(r.Status,r.ArrivedAt,r.StartsAt,SYSUTCDATETIME())=1;
GO
CREATE OR ALTER PROCEDURE dbo.usp_RecordReservationNoShow @ReservationId bigint,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
  DECLARE @status varchar(20),@arrived datetime2(3),@start datetime2(3),@table int,@now datetime2(3)=SYSUTCDATETIME();
  SELECT @status=Status,@arrived=ArrivedAt,@start=StartsAt,@table=TableId FROM dbo.Reservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId;
  IF @status IS NULL OR @status<>'Confirmed' OR @arrived IS NOT NULL
   THROW 51064,N'Lượt đặt đã được xử lý hoặc khách đã tới. Không thể đánh dấu không tới.',1;
  IF dbo.fn_IsNoShowDue(@status,@arrived,@start,@now)=0 THROW 51065,N'Chỉ được đánh dấu khi khách trễ từ 15 phút.',1;
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
CREATE OR ALTER PROCEDURE dbo.usp_MarkManagedReservationNoShow @ReservationId bigint,@ActorUserId int AS
 EXEC dbo.usp_RecordReservationNoShow @ReservationId,@ActorUserId;
GO
CREATE OR ALTER PROCEDURE dbo.usp_MarkNoShow @ReservationId bigint,@Extend bit,@ActorUserId int AS
BEGIN
 IF @Extend=1 THROW 51065,N'Gia hạn giữ bàn chưa được hỗ trợ trong chức năng này.',1;
 EXEC dbo.usp_RecordReservationNoShow @ReservationId,@ActorUserId;
END;
GO
GRANT SELECT ON dbo.v_NoShowAlerts TO restaurant_app;
GRANT SELECT ON dbo.ReservationNoShowHistory TO restaurant_app;
GRANT EXECUTE ON dbo.fn_NoShowPhone TO restaurant_app;
GRANT EXECUTE ON dbo.usp_RecordReservationNoShow TO restaurant_app;
DENY INSERT,UPDATE,DELETE ON dbo.ReservationNoShowHistory TO restaurant_app;
