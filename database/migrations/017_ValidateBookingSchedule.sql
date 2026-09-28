CREATE OR ALTER PROCEDURE dbo.usp_ValidateBookingSchedule @StartsAt datetime2(3)
AS
BEGIN
 SET NOCOUNT ON;
 IF @StartsAt IS NULL OR @StartsAt<=SYSUTCDATETIME()
  THROW 51003,N'Giờ đặt bàn phải ở tương lai.',1;
 DECLARE @local datetime2(3)=DATEADD(hour,7,@StartsAt);
 DECLARE @day date=CONVERT(date,@local), @time time(3)=CONVERT(time(3),@local);
 DECLARE @weekday int=(DATEDIFF(day,CONVERT(date,'19000101'),@day)%7)+1;
 IF EXISTS(SELECT 1 FROM dbo.SpecialHolidays WHERE HolidayDate=@day AND IsActive=1)
  THROW 51410,N'Ngày bạn chọn là ngày nghỉ đặc biệt. Nhà hàng không nhận đặt bàn trong ngày này.',1;
 DECLARE @open time(0),@close time(0),@closed bit;
 SELECT @open=OpensAt,@close=ClosesAt,@closed=IsClosed FROM dbo.OpeningHours WHERE DayOfWeek=@weekday;
 IF @closed=1
  THROW 51411,N'Ngày bạn chọn là ngày nghỉ trong tuần. Nhà hàng không nhận đặt bàn.',1;
 IF @closed IS NULL OR @open IS NULL OR @close IS NULL OR @time<@open OR @time>=@close
  THROW 51004,N'Giờ nhận bàn phải từ giờ mở cửa và trước giờ đóng cửa theo cấu hình của ngày đã chọn.',1;
 IF DATEDIFF(minute,@open,@time)%30<>0 OR DATEPART(second,@time)<>0 OR DATEPART(millisecond,@time)<>0
  THROW 51412,N'Giờ nhận bàn phải cách giờ mở cửa một số nguyên lần 30 phút.',1;
END;
GO
GRANT EXECUTE ON dbo.usp_ValidateBookingSchedule TO restaurant_app;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CreateReservation @CustomerName nvarchar(100),@Phone varchar(10),@GuestCount int,@StartsAt datetime2(3),
 @PreferredAreaId int=NULL,@Email nvarchar(254)=NULL,@Notes nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  IF @PreferredAreaId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE Id=@PreferredAreaId AND IsActive=1)
   THROW 51407,N'Khu vực bạn chọn đã ngừng sử dụng hoặc không tồn tại.',1;
  IF LEN(LTRIM(RTRIM(@CustomerName)))=0 OR @GuestCount NOT BETWEEN 1 AND 20 OR LEN(@Phone)<>10 OR @Phone LIKE '%[^0-9]%'
   THROW 51002,N'Thông tin khách đặt bàn không hợp lệ.',1;
  EXEC dbo.usp_ValidateBookingSchedule @StartsAt;
  DECLARE @duration int=(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1);
  DECLARE @ends datetime2(3)=DATEADD(minute,@duration,@StartsAt);
  IF (SELECT COUNT(*) FROM dbo.Reservations WHERE Phone=@Phone AND Status='Pending')>=3
   THROW 51005,N'Mỗi số điện thoại chỉ được có tối đa 3 đặt bàn chờ xác nhận.',1;
  -- Pending requests do not allocate tables. Availability is checked by usp_ConfirmReservation.
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
