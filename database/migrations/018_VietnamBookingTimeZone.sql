-- SQL Server Windows zone corresponding to Asia/Ho_Chi_Minh.
CREATE OR ALTER PROCEDURE dbo.usp_ValidateBookingSchedule @StartsAt datetime2(3)
AS
BEGIN
 SET NOCOUNT ON;
 IF @StartsAt IS NULL OR @StartsAt<=SYSUTCDATETIME()
  THROW 51003,N'Giờ đặt bàn phải ở tương lai.',1;
 DECLARE @local datetime2(3)=CONVERT(datetime2(3),(@StartsAt AT TIME ZONE 'UTC') AT TIME ZONE 'SE Asia Standard Time');
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
