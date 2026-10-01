-- Pending -> Rejected is mutually exclusive with confirmation under the existing operation lock.
CREATE OR ALTER PROCEDURE dbo.usp_RejectReservation
 @ReservationId bigint,@Reason nvarchar(100),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
  IF @Reason IS NULL OR NOT EXISTS(SELECT 1 FROM (VALUES(N'NoTable'),(N'OutsideHours'),(N'Unreachable')) reasons(Code)
    WHERE Code COLLATE Latin1_General_100_BIN2=@Reason COLLATE Latin1_General_100_BIN2 AND DATALENGTH(Code)=DATALENGTH(@Reason))
   THROW 51010,N'Vui lòng chọn một trong ba lý do từ chối hợp lệ.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.Reservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId AND Status='Pending')
   THROW 51011,N'Chỉ có thể từ chối lượt đặt đang chờ. Lượt đặt đã được xử lý hoặc không tồn tại.',1;
  UPDATE dbo.Reservations SET Status='Rejected',RejectionReason=@Reason,TableId=NULL WHERE Id=@ReservationId;
  -- Preserve the existing internal event stream. No new audit UI or rejection email in this slice.
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,ActorUserId,Reason)
   VALUES(@ReservationId,'Pending','Rejected',@ActorUserId,@Reason);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CustomerReservationLookup
 @Code varchar(64),@Phone varchar(64),@IpAddress varchar(45)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @id bigint;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  IF (SELECT COUNT(*) FROM dbo.LookupAttempts WHERE IpAddress=@IpAddress AND AttemptedAt>DATEADD(minute,-15,SYSUTCDATETIME()))>=20
  BEGIN
   COMMIT;
   THROW 51620,N'Bạn đã tra cứu quá nhiều lần. Vui lòng thử lại sau 15 phút.',1;
  END;
  SELECT @id=Id FROM dbo.Reservations WHERE Code=@Code AND Phone=@Phone AND LEN(@Code)=6 AND LEN(@Phone)=10;
  INSERT dbo.LookupAttempts(IpAddress,Succeeded) VALUES(@IpAddress,CASE WHEN @id IS NULL THEN 0 ELSE 1 END);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
 -- Return only customer-facing fields, never contact details or internal notes.
 SELECT r.Code,r.Status,CASE WHEN r.Status='Rejected' THEN r.RejectionReason END AS RejectionReason,
  r.StartsAt,r.EndsAt,r.GuestCount,t.Code AS TableCode
 FROM dbo.Reservations r LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId WHERE r.Id=@id;
END;
GO
GRANT EXECUTE ON dbo.usp_CustomerReservationLookup TO restaurant_app;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ReservationConfirmationDetails @ActorUserId int,@ReservationId bigint
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
 SELECT r.Id,r.Code,r.CustomerName,r.Phone,r.Email,r.GuestCount,r.StartsAt,r.EndsAt,r.Status,
  COALESCE(r.AreaNameSnapshot,a.Name) AS AreaName,t.Code AS TableCode,
  r.RejectionReason,o.Status AS EmailStatus,o.LastError AS EmailError,o.AttemptCount
 FROM dbo.Reservations r LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
 LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId
 LEFT JOIN dbo.EmailOutbox o ON o.DedupeKey=CONCAT('BookingConfirmed:',r.Id)
 WHERE r.Id=@ReservationId;
 -- Preferred area is a preference: show all eligible areas, preferred first.
 SELECT t.Id,t.Code,t.MaxCapacity,a.Name AS AreaName
 FROM dbo.Reservations r CROSS JOIN dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
 WHERE r.Id=@ReservationId AND r.Status='Pending' AND r.StartsAt>SYSUTCDATETIME()
 AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=r.GuestCount
 AND NOT EXISTS(SELECT 1 FROM dbo.Reservations other WHERE other.TableId=t.Id
   AND other.Status IN ('Confirmed','Arrived') AND other.StartsAt<r.EndsAt AND other.EndsAt>r.StartsAt)
 ORDER BY CASE WHEN t.AreaId=r.PreferredAreaId THEN 0 ELSE 1 END,t.MaxCapacity,t.SortOrder,t.Code,t.Id;
END;
GO
