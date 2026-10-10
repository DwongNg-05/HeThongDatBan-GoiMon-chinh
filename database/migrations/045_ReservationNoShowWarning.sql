-- S3-09: rolling 90*24 hours, inclusive endpoints, recorded no-show time (UTC).
CREATE OR ALTER FUNCTION dbo.fn_RecentNoShowCount(@phone nvarchar(100),@asOf datetime2(3)) RETURNS int
AS
BEGIN
 RETURN (SELECT COUNT(*) FROM dbo.ReservationNoShowHistory
 WHERE NormalizedPhone=dbo.fn_NoShowPhone(@phone)
 AND NoShowAt>=DATEADD(day,-90,@asOf) AND NoShowAt<=@asOf);
END;
GO
GRANT EXECUTE ON dbo.fn_RecentNoShowCount TO restaurant_app;
GO
-- Restore the table-selection signature after integrating older reservation migrations.
CREATE OR ALTER PROCEDURE dbo.usp_CreateReservation @CustomerName nvarchar(100),@Phone varchar(10),@GuestCount int,@StartsAt datetime2(3),
 @PreferredAreaId int=NULL,@Email nvarchar(254)=NULL,@Notes nvarchar(500)=NULL,@TableId int=NULL,@AcknowledgedNoShowCount int=-1
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  DECLARE @recentNoShows int=dbo.fn_RecentNoShowCount(@Phone,SYSUTCDATETIME());
  IF @recentNoShows>=3 AND (@AcknowledgedNoShowCount IS NULL OR @AcknowledgedNoShowCount<@recentNoShows)
   THROW 51066,N'Số điện thoại có ít nhất 3 lần khách không tới trong 90 ngày. Vui lòng đọc và xác nhận cảnh báo mới nhất để tiếp tục đặt bàn.',1;

  IF @PreferredAreaId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE Id=@PreferredAreaId AND IsActive=1)
   THROW 51407,N'Khu vực bạn chọn đã ngừng sử dụng hoặc không tồn tại.',1;
  IF LEN(LTRIM(RTRIM(@CustomerName)))=0 OR @GuestCount NOT BETWEEN 1 AND 20 OR LEN(@Phone)<>10 OR @Phone LIKE '%[^0-9]%'
   THROW 51002,N'Thông tin khách đặt bàn không hợp lệ.',1;
  EXEC dbo.usp_ValidateBookingSchedule @StartsAt;
  DECLARE @duration int=(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1);
  DECLARE @ends datetime2(3)=DATEADD(minute,@duration,@StartsAt);
  IF (SELECT COUNT(*) FROM dbo.Reservations WHERE Phone=@Phone AND Status='Pending')>=3
   THROW 51005,N'Mỗi số điện thoại chỉ được có tối đa 3 đặt bàn chờ xác nhận.',1;

  DECLARE @tableCode varchar(20)=NULL;
  IF @TableId IS NOT NULL
  BEGIN
   DECLARE @tableArea int;
   SELECT @tableCode=t.Code,@tableArea=t.AreaId FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
    WHERE t.Id=@TableId AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@GuestCount;
   IF @tableCode IS NULL OR (@PreferredAreaId IS NOT NULL AND @tableArea<>@PreferredAreaId)
    THROW 51413,N'Bàn bạn chọn không còn sử dụng, không đủ chỗ cho số khách hoặc không thuộc khu vực đã chọn.',1;
   -- Đang giữ khoá usp_LockOperations nên hai khách không thể cùng giữ một bàn trong khung giờ giao nhau.
   IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=@TableId AND Status IN ('Pending','Confirmed','Arrived')
     AND StartsAt<@ends AND EndsAt>@StartsAt)
    THROW 51414,N'Bàn bạn chọn vừa có khách khác đặt trong khung giờ này. Vui lòng chọn bàn khác.',1;
   SET @PreferredAreaId=COALESCE(@PreferredAreaId,@tableArea);
  END;

  DECLARE @code char(6);
  SET @code=UPPER(LEFT(CONVERT(varchar(64),CRYPT_GEN_RANDOM(16),2),6));
  WHILE EXISTS(SELECT 1 FROM dbo.Reservations WHERE Code=@code)
   SET @code=UPPER(LEFT(CONVERT(varchar(64),CRYPT_GEN_RANDOM(16),2),6));
  INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,TableId,StartsAt,EndsAt,Notes)
   VALUES(@code,LTRIM(RTRIM(@CustomerName)),@Phone,NULLIF(LTRIM(RTRIM(@Email)),''),@GuestCount,@PreferredAreaId,@TableId,@StartsAt,@ends,@Notes);
  DECLARE @id bigint=SCOPE_IDENTITY();
  INSERT dbo.ReservationEvents(ReservationId,ToStatus,NewTableId) VALUES(@id,'Pending',@TableId);
  EXEC dbo.usp_QueueBookingEmail @id,'BookingReceived';
  SELECT @id AS ReservationId,@code AS Code,@tableCode AS TableCode;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_CreateManagedTableReservation
    @TableId int,
    @StartsAt datetime2(3),
    @CustomerName nvarchar(100),
    @Phone varchar(10),
    @InitialStatus varchar(20),
    @ActorUserId int,@AcknowledgedNoShowCount int=-1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Reservations.Manage';
  DECLARE @recentNoShows int=dbo.fn_RecentNoShowCount(@Phone,SYSUTCDATETIME());
  IF @recentNoShows>=3 AND (@AcknowledgedNoShowCount IS NULL OR @AcknowledgedNoShowCount<@recentNoShows)
   THROW 51066,N'Số điện thoại có ít nhất 3 lần khách không tới trong 90 ngày. Vui lòng đọc và xác nhận cảnh báo mới nhất để tiếp tục đặt bàn.',1;

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
