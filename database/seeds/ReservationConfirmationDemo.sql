-- Opt-in confirmation demo. Never changes existing accounts, tables or reservations.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
EXEC dbo.usp_LockOperations;
IF NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE Name=N'Demo xác nhận')
 INSERT dbo.Areas(Name,SortOrder) VALUES(N'Demo xác nhận',999);
DECLARE @area int=(SELECT Id FROM dbo.Areas WHERE Name=N'Demo xác nhận' AND IsActive=1);
IF @area IS NULL THROW 51510,N'Khu vực demo đã ngừng sử dụng; không tự kích hoạt lại.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.DiningTables WHERE Code='DEMO-5')
 INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,SortOrder) VALUES(@area,'DEMO-5',1,4,5);
IF NOT EXISTS(SELECT 1 FROM dbo.DiningTables WHERE Code='DEMO-2')
 INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,SortOrder) VALUES(@area,'DEMO-2',1,2,2);
IF NOT EXISTS(SELECT 1 FROM dbo.Reservations WHERE Code='CF0001')
BEGIN
 DECLARE @s datetime2(3)=DATEADD(hour,11,CONVERT(datetime2(3),CONVERT(date,DATEADD(day,1,DATEADD(hour,7,SYSUTCDATETIME())))));
 DECLARE @duration int=(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1);
 INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,StartsAt,EndsAt,Notes)
 VALUES('CF0001',N'Khách demo xác nhận','0900000099','demo@example.test',4,@area,@s,DATEADD(minute,ISNULL(@duration,90),@s),N'Dữ liệu demo; email giả, không gửi thư thật.');
END;
COMMIT;
GO

