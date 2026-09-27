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
  IF @StartsAt<=SYSUTCDATETIME() THROW 51003,N'Giờ đặt bàn phải ở tương lai.',1;
  DECLARE @local datetime2=DATEADD(hour,7,@StartsAt),@duration int,@ends datetime2(3),@open time(0),@close time(0),@closed bit;
  SELECT @duration=DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1;
  SET @ends=DATEADD(minute,@duration,@StartsAt);
  DECLARE @day date=CONVERT(date,@local),@weekday int=((DATEDIFF(day,CONVERT(date,'19000101'),CONVERT(date,@local))%7)+1);
  IF EXISTS(SELECT 1 FROM dbo.SpecialHolidays WHERE HolidayDate=@day AND IsActive=1)
   THROW 51410,N'Ngày bạn chọn là ngày nghỉ đặc biệt. Nhà hàng không nhận đặt bàn trong ngày này.',1;
  SELECT @open=OpensAt,@close=ClosesAt,@closed=IsClosed FROM dbo.OpeningHours WHERE DayOfWeek=@weekday;
  SELECT @open=OpensAt,@close=ClosesAt,@closed=IsClosed FROM dbo.SpecialDates WHERE BusinessDate=@day;
  IF @closed IS NULL OR @closed=1 OR CONVERT(time,@local)<@open
    OR CONVERT(date,DATEADD(hour,7,@ends))<>@day OR CONVERT(time,DATEADD(hour,7,@ends))>@close
    OR DATEPART(minute,@local)%30<>0 OR DATEPART(second,@local)<>0 OR DATEPART(millisecond,@local)<>0
   THROW 51004,N'Khung giờ không nằm trong giờ mở cửa hoặc không đúng bước 30 phút.',1;
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
