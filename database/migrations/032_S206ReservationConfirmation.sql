-- S2-06 Task 1. Public Pending reservations already hold a concrete table
-- (S2-02/S2-03); confirming preserves that hold instead of allocating again.
CREATE OR ALTER PROCEDURE dbo.usp_PendingReservations @ActorUserId int
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
 SELECT r.Id,r.Code,r.CustomerName,r.GuestCount,r.StartsAt,r.EndsAt,r.Status,
  COALESCE(r.AreaNameSnapshot,a.Name) AS AreaName
 FROM dbo.Reservations r LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
 WHERE r.Status='Pending' ORDER BY r.StartsAt,r.Id;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ReservationConfirmationDetails @ActorUserId int,@ReservationId bigint
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
 SELECT r.Id,r.Code,r.CustomerName,r.Phone,r.Email,r.GuestCount,r.StartsAt,r.EndsAt,r.Status,
  COALESCE(r.AreaNameSnapshot,a.Name) AS AreaName,t.Code AS TableCode,r.TableId,r.RejectionReason,
  o.Status AS EmailStatus,o.LastError AS EmailError,o.AttemptCount
 FROM dbo.Reservations r LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
 LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId
 LEFT JOIN dbo.EmailOutbox o ON o.DedupeKey=CONCAT('BookingConfirmed:',r.Id)
 WHERE r.Id=@ReservationId;

 -- A modern public reservation has a held table. Legacy unassigned Pending rows
 -- receive candidates using the same Pending/Confirmed + 15-minute rule.
 SELECT t.Id,t.Code,t.MaxCapacity,a.Name AS AreaName
 FROM dbo.Reservations r CROSS JOIN dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
 WHERE r.Id=@ReservationId AND r.Status='Pending' AND r.StartsAt>SYSUTCDATETIME()
 AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=r.GuestCount
 AND (r.TableId IS NULL OR t.Id=r.TableId)
 AND NOT EXISTS(SELECT 1 FROM dbo.Reservations other WHERE other.TableId=t.Id AND other.Id<>r.Id
   AND other.Status IN ('Pending','Confirmed')
   AND other.StartsAt<DATEADD(minute,15,r.EndsAt)
   AND r.StartsAt<DATEADD(minute,15,other.EndsAt))
 ORDER BY CASE WHEN t.AreaId=r.PreferredAreaId THEN 0 ELSE 1 END,t.MaxCapacity,t.SortOrder,t.Code,t.Id;
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
  DECLARE @status varchar(20),@currentTableId int,@start datetime2(3),@end datetime2(3),@guests int,@tableCode varchar(20);
  SELECT @status=Status,@currentTableId=TableId,@start=StartsAt,@end=EndsAt,@guests=GuestCount
   FROM dbo.Reservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId;
  IF @status IS NULL THROW 51501,N'Không tìm thấy lượt đặt bàn.',1;
  IF @status<>'Pending' THROW 51502,N'Lượt đặt đã được xử lý. Không thể xác nhận lại.',1;
  IF @start<=SYSUTCDATETIME() THROW 51503,N'Giờ hẹn đã qua. Không thể xác nhận lượt đặt này.',1;
  IF @currentTableId IS NOT NULL AND @TableId<>@currentTableId
   THROW 51504,N'Lượt đặt đã giữ một bàn khác. Hãy xác nhận bàn đang giữ hoặc dùng chức năng đổi bàn sau khi xác nhận.',1;
  SELECT @tableCode=t.Code FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
   WHERE t.Id=@TableId AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@guests;
  IF @tableCode IS NULL THROW 51008,N'Bàn không còn hoạt động hoặc không đủ chỗ. Vui lòng chọn bàn khác.',1;
  IF EXISTS(SELECT 1 FROM dbo.Reservations other WHERE other.TableId=@TableId AND other.Id<>@ReservationId
    AND other.Status IN ('Pending','Confirmed')
    AND other.StartsAt<DATEADD(minute,15,@end)
    AND @start<DATEADD(minute,15,other.EndsAt))
   THROW 51009,N'Bàn vừa được giữ trong khung giờ này. Vui lòng chọn bàn khác.',1;
  UPDATE dbo.Reservations SET Status='Confirmed',TableId=@TableId,ConfirmedAt=SYSUTCDATETIME(),ConfirmedBy=@ActorUserId
   WHERE Id=@ReservationId;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,OldTableId,NewTableId,ActorUserId)
   VALUES(@ReservationId,'Pending','Confirmed',@currentTableId,@TableId,@ActorUserId);
  EXEC dbo.usp_QueueBookingEmail @ReservationId,'BookingConfirmed';
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ReservedTableSlots @ActorUserId int,@Day date
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
 DECLARE @start datetime2(3)=DATEADD(hour,-7,CONVERT(datetime2(3),@Day));
 SELECT r.Id,r.Code AS ReservationCode,t.Code AS TableCode,t.MaxCapacity,a.Name AS AreaName,r.StartsAt,r.EndsAt,r.GuestCount
 FROM dbo.Reservations r JOIN dbo.DiningTables t ON t.Id=r.TableId JOIN dbo.Areas a ON a.Id=t.AreaId
 WHERE r.Status IN ('Pending','Confirmed') AND r.StartsAt<DATEADD(day,1,@start) AND DATEADD(minute,15,r.EndsAt)>@start
 ORDER BY r.StartsAt,t.Code,r.Id;
END;
GO
GRANT EXECUTE ON dbo.usp_PendingReservations TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ReservationConfirmationDetails TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ReservedTableSlots TO restaurant_app;
GO
