-- S2-03 Task 1: reservations made by a manager from the table schedule.
-- Pending and Confirmed reservations hold a table through the 15-minute cleaning buffer.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Reservations') AND name=N'IX_Reservations_TableHold')
    CREATE INDEX IX_Reservations_TableHold ON dbo.Reservations(TableId, Status, StartsAt, EndsAt) WHERE TableId IS NOT NULL;
GO

CREATE OR ALTER TRIGGER dbo.trg_Reservations_PreventTableHoldOverlap
ON dbo.Reservations
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted WHERE TableId IS NOT NULL AND Status IN ('Pending', 'Confirmed'))
        RETURN;

    -- This is also acquired by the reservation procedures. It serializes direct SQL writes,
    -- so two concurrent inserts cannot both pass the overlap check.
    EXEC dbo.usp_LockOperations;

    IF EXISTS (
        SELECT 1
        FROM inserted AS proposed
        JOIN dbo.Reservations AS existing WITH (UPDLOCK, HOLDLOCK, INDEX(IX_Reservations_TableHold))
          ON existing.TableId = proposed.TableId
         AND existing.Id <> proposed.Id
         AND existing.Status IN ('Pending', 'Confirmed')
        WHERE proposed.TableId IS NOT NULL
          AND proposed.Status IN ('Pending', 'Confirmed')
          -- Each hold includes 15 minutes after its own end. Equality at the boundary is allowed.
          AND proposed.StartsAt < DATEADD(minute, 15, existing.EndsAt)
          AND existing.StartsAt < DATEADD(minute, 15, proposed.EndsAt)
    )
        THROW 51060, N'Bàn vừa có người đặt trong khung giờ này hoặc 15 phút dọn bàn.', 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_CreateManagedTableReservation
    @TableId int,
    @StartsAt datetime2(3),
    @CustomerName nvarchar(100),
    @Phone varchar(10),
    @InitialStatus varchar(20),
    @ActorUserId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Reservations.Manage';

        IF @InitialStatus NOT IN ('Pending', 'Confirmed')
            THROW 51061, N'Trạng thái ban đầu phải là Chờ xác nhận hoặc Đã xác nhận.', 1;
        IF @StartsAt IS NULL OR DATEPART(second, @StartsAt) <> 0 OR DATEPART(millisecond, @StartsAt) <> 0
            THROW 51062, N'Giờ bắt đầu phải đúng theo phút.', 1;
        IF LEN(LTRIM(RTRIM(@CustomerName))) = 0 OR LEN(LTRIM(RTRIM(@CustomerName))) > 100
            THROW 51002, N'Tên khách hàng không hợp lệ.', 1;
        IF LEN(@Phone) <> 10 OR @Phone LIKE '%[^0-9]%'
            THROW 51002, N'Số điện thoại phải gồm đúng 10 chữ số.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.DiningTables WHERE Id=@TableId AND IsActive=1)
            THROW 51063, N'Bàn không tồn tại hoặc đã ngừng sử dụng.', 1;

        DECLARE @duration int = (SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1);
        IF @duration IS NULL OR @duration <= 0 SET @duration = 90;
        DECLARE @endsAt datetime2(3) = DATEADD(minute, @duration, @StartsAt);
        DECLARE @areaId int = (SELECT AreaId FROM dbo.DiningTables WHERE Id=@TableId);
        DECLARE @alphabet varchar(32) = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
        DECLARE @code char(6);
        WHILE 1=1
        BEGIN
            SET @code = CONCAT(
                SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1),
                SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1),
                SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1),
                SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1),
                SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1),
                SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1));
            IF NOT EXISTS (SELECT 1 FROM dbo.Reservations WHERE Code=@code) BREAK;
        END;

        INSERT dbo.Reservations(Code, CustomerName, Phone, GuestCount, PreferredAreaId, TableId, StartsAt, EndsAt,
            Status, ConfirmedAt, ConfirmedBy)
        VALUES(@code, LTRIM(RTRIM(@CustomerName)), @Phone, 1, @areaId, @TableId, @StartsAt, @endsAt,
            @InitialStatus,
            CASE WHEN @InitialStatus='Confirmed' THEN SYSUTCDATETIME() END,
            CASE WHEN @InitialStatus='Confirmed' THEN @ActorUserId END);

        DECLARE @reservationId bigint = SCOPE_IDENTITY();
        INSERT dbo.ReservationEvents(ReservationId, ToStatus, NewTableId, ActorUserId)
        VALUES(@reservationId, @InitialStatus, @TableId, @ActorUserId);
        SELECT @reservationId AS ReservationId, @code AS Code, @endsAt AS EndsAt;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_CreateManagedTableReservation TO restaurant_app;
GO

-- Public booking selects a table using the same 15-minute hold interval as the database trigger.
CREATE OR ALTER PROCEDURE dbo.usp_CreateReservation
 @CustomerName nvarchar(100), @Phone varchar(10), @GuestCount int, @StartsAt datetime2(3),
 @PreferredAreaId int = NULL, @Email nvarchar(254) = NULL, @Notes nvarchar(500) = NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  IF LEN(LTRIM(RTRIM(@CustomerName))) = 0 OR @GuestCount NOT BETWEEN 1 AND 20 OR LEN(@Phone) <> 10 OR @Phone LIKE '%[^0-9]%'
   THROW 51002, N'Thông tin khách đặt bàn không hợp lệ.', 1;
  IF @PreferredAreaId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE Id=@PreferredAreaId AND IsActive=1)
   THROW 51407, N'Khu vực bạn chọn đã ngừng sử dụng hoặc không tồn tại.', 1;
  EXEC dbo.usp_ValidateBookingSchedule @StartsAt;
  DECLARE @duration int = (SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1);
  DECLARE @ends datetime2(3) = DATEADD(minute, @duration, @StartsAt);
  IF (SELECT COUNT(*) FROM dbo.Reservations WHERE Phone=@Phone AND Status='Pending') >= 3
   THROW 51005, N'Mỗi số điện thoại chỉ được có tối đa 3 đặt bàn chờ xác nhận.', 1;
  DECLARE @tableId int;
  SELECT TOP (1) @tableId=t.Id
  FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
  WHERE t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@GuestCount
    AND (@PreferredAreaId IS NULL OR t.AreaId=@PreferredAreaId)
    AND NOT EXISTS (
      SELECT 1 FROM dbo.Reservations r
      WHERE r.TableId=t.Id AND r.Status IN ('Pending','Confirmed','Arrived')
        AND r.StartsAt < DATEADD(minute, 15, @ends)
        AND @StartsAt < DATEADD(minute, 15, r.EndsAt))
  ORDER BY t.MaxCapacity, t.SortOrder, t.Id;
  IF @tableId IS NULL
   THROW 51006, N'Khung giờ này vừa hết bàn phù hợp. Vui lòng chọn khung giờ hoặc khu vực khác.', 1;
  DECLARE @alphabet varchar(32) = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  DECLARE @code char(6);
  WHILE 1=1
  BEGIN
   SET @code = CONCAT(
     SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1),
     SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1),
     SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1),
     SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1),
     SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1),
     SUBSTRING(@alphabet,1+ABS(CONVERT(bigint,CHECKSUM(NEWID())))%LEN(@alphabet),1));
   IF NOT EXISTS (SELECT 1 FROM dbo.Reservations WHERE Code=@code) BREAK;
  END;
  INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,TableId,StartsAt,EndsAt,Notes)
  VALUES(@code,LTRIM(RTRIM(@CustomerName)),@Phone,NULLIF(LTRIM(RTRIM(@Email)),''),@GuestCount,@PreferredAreaId,@tableId,@StartsAt,@ends,NULLIF(LTRIM(RTRIM(@Notes)),''));
  DECLARE @id bigint=SCOPE_IDENTITY();
  INSERT dbo.ReservationEvents(ReservationId,ToStatus,NewTableId) VALUES(@id,'Pending',@tableId);
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
GRANT EXECUTE ON dbo.usp_CreateReservation TO restaurant_app;
GO
