-- Confirm pending reservations only. Occupancy is [StartsAt, EndsAt), not a whole-day table flag.
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
  COALESCE(r.AreaNameSnapshot,a.Name) AS AreaName,t.Code AS TableCode,
  o.Status AS EmailStatus,o.LastError AS EmailError,o.AttemptCount
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
CREATE OR ALTER PROCEDURE dbo.usp_ConfirmReservation @ReservationId bigint,@TableId int,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
  DECLARE @status varchar(20),@start datetime2(3),@end datetime2(3),@guests int,@email nvarchar(254),@tableCode varchar(20);
  SELECT @status=Status,@start=StartsAt,@end=EndsAt,@guests=GuestCount,@email=Email
   FROM dbo.Reservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId;
  IF @status IS NULL THROW 51501,N'Không tìm thấy lượt đặt bàn.',1;
  IF @status<>'Pending' THROW 51502,N'Lượt đặt đã được xử lý. Không thể xác nhận lại.',1;
  IF @start<=SYSUTCDATETIME() THROW 51503,N'Giờ hẹn đã qua. Không thể xác nhận lượt đặt này.',1;
  SELECT @tableCode=t.Code FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
   WHERE t.Id=@TableId AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@guests;
  IF @tableCode IS NULL THROW 51008,N'Bàn không còn hoạt động hoặc không đủ chỗ. Vui lòng chọn bàn khác.',1;
  IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=@TableId AND Status IN ('Confirmed','Arrived')
    AND StartsAt<@end AND EndsAt>@start)
   THROW 51009,N'Bàn vừa được giữ trong khung giờ này. Danh sách gợi ý đã được cập nhật, vui lòng chọn bàn khác.',1;
  UPDATE dbo.Reservations SET Status='Confirmed',TableId=@TableId,ConfirmedAt=SYSUTCDATETIME(),ConfirmedBy=@ActorUserId
   WHERE Id=@ReservationId;
  -- Keep the existing event stream for other modules; no new audit UI in this slice.
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,NewTableId,ActorUserId)
   VALUES(@ReservationId,'Pending','Confirmed',@TableId,@ActorUserId);
  UPDATE dbo.DiningTables SET Status='Reserved',StatusChangedAt=SYSUTCDATETIME()
   WHERE Id=@TableId AND Status='Available' AND @start<=DATEADD(minute,30,SYSUTCDATETIME());
  DECLARE @payload nvarchar(max)=(SELECT Code,CustomerName,GuestCount,StartsAt,EndsAt,@tableCode AS TableCode
    FROM dbo.Reservations WHERE Id=@ReservationId FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
  INSERT dbo.EmailOutbox(ReservationId,MessageType,Recipient,Subject,PayloadJson,DedupeKey,Status,LastError)
   VALUES(@ReservationId,'BookingConfirmed',ISNULL(@email,N''),N'Xác nhận đặt bàn',@payload,
    CONCAT('BookingConfirmed:',@ReservationId),CASE WHEN NULLIF(LTRIM(RTRIM(@email)),N'') IS NULL THEN 'Failed' ELSE 'Pending' END,
    CASE WHEN NULLIF(LTRIM(RTRIM(@email)),N'') IS NULL THEN N'Khách chưa cung cấp email. Lượt đặt vẫn đã được xác nhận.' ELSE NULL END);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
-- Read the future timetable separately from the current operational table status.
CREATE OR ALTER PROCEDURE dbo.usp_ReservedTableSlots @ActorUserId int,@Day date
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
 DECLARE @start datetime2(3)=DATEADD(hour,-7,CONVERT(datetime2(3),@Day));
 SELECT r.Id,r.Code AS ReservationCode,t.Code AS TableCode,t.MaxCapacity,a.Name AS AreaName,r.StartsAt,r.EndsAt,r.GuestCount
 FROM dbo.Reservations r JOIN dbo.DiningTables t ON t.Id=r.TableId JOIN dbo.Areas a ON a.Id=t.AreaId
 WHERE r.Status IN ('Confirmed','Arrived') AND r.StartsAt<DATEADD(day,1,@start) AND r.EndsAt>@start
 ORDER BY r.StartsAt,t.Code,r.Id;
END;
GO
-- Claim only confirmation emails. Other notifications remain owned by their workflows.
CREATE OR ALTER PROCEDURE dbo.usp_ClaimConfirmationEmail
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  -- A worker that died on the final attempt must not leave a permanent Processing row.
  UPDATE dbo.EmailOutbox SET Status='Failed',LockedUntil=NULL,LastError=N'Quá số lần thử; phiên gửi cuối bị gián đoạn.'
   WHERE MessageType='BookingConfirmed' AND Status='Processing' AND LockedUntil<SYSUTCDATETIME() AND AttemptCount>=4;
  DECLARE @id bigint=(SELECT TOP(1) Id FROM dbo.EmailOutbox WHERE MessageType='BookingConfirmed' AND AttemptCount<4
    AND ((Status='Pending' AND NextAttemptAt<=SYSUTCDATETIME()) OR (Status='Processing' AND LockedUntil<SYSUTCDATETIME()))
    ORDER BY NextAttemptAt,Id);
  IF @id IS NOT NULL
   UPDATE dbo.EmailOutbox SET Status='Processing',AttemptCount=AttemptCount+1,LockedUntil=DATEADD(minute,2,SYSUTCDATETIME())
    OUTPUT inserted.Id,inserted.AttemptCount,inserted.Recipient,inserted.Subject,inserted.PayloadJson WHERE Id=@id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
GRANT EXECUTE ON dbo.usp_PendingReservations TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ReservationConfirmationDetails TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ReservedTableSlots TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ClaimConfirmationEmail TO restaurant_worker;
GO
