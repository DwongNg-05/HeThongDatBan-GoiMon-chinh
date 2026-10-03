-- S2-02 Task 1: public reservation flow.
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE NormalizedName = N'TRONG NHÀ')
    INSERT dbo.Areas(Name, SortOrder, IsActive) VALUES (N'Trong nhà', 10, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE NormalizedName = N'NGOÀI TRỜI')
    INSERT dbo.Areas(Name, SortOrder, IsActive) VALUES (N'Ngoài trời', 20, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE NormalizedName = N'PHÒNG RIÊNG')
    INSERT dbo.Areas(Name, SortOrder, IsActive) VALUES (N'Phòng riêng', 30, 1);
GO

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
  IF @PreferredAreaId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE Id = @PreferredAreaId AND IsActive = 1)
   THROW 51407, N'Khu vực bạn chọn đã ngừng sử dụng hoặc không tồn tại.', 1;
  EXEC dbo.usp_ValidateBookingSchedule @StartsAt;
  DECLARE @duration int = (SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id = 1);
  DECLARE @ends datetime2(3) = DATEADD(minute, @duration, @StartsAt);
  IF (SELECT COUNT(*) FROM dbo.Reservations WHERE Phone = @Phone AND Status = 'Pending') >= 3
   THROW 51005, N'Mỗi số điện thoại chỉ được có tối đa 3 đặt bàn chờ xác nhận.', 1;
  DECLARE @alphabet varchar(32) = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  DECLARE @code char(6);
  WHILE 1 = 1
  BEGIN
   SET @code = CONCAT(
    SUBSTRING(@alphabet, 1 + ABS(CONVERT(bigint, CHECKSUM(NEWID()))) % LEN(@alphabet), 1),
    SUBSTRING(@alphabet, 1 + ABS(CONVERT(bigint, CHECKSUM(NEWID()))) % LEN(@alphabet), 1),
    SUBSTRING(@alphabet, 1 + ABS(CONVERT(bigint, CHECKSUM(NEWID()))) % LEN(@alphabet), 1),
    SUBSTRING(@alphabet, 1 + ABS(CONVERT(bigint, CHECKSUM(NEWID()))) % LEN(@alphabet), 1),
    SUBSTRING(@alphabet, 1 + ABS(CONVERT(bigint, CHECKSUM(NEWID()))) % LEN(@alphabet), 1),
    SUBSTRING(@alphabet, 1 + ABS(CONVERT(bigint, CHECKSUM(NEWID()))) % LEN(@alphabet), 1));
   IF NOT EXISTS (SELECT 1 FROM dbo.Reservations WHERE Code = @code) BREAK;
  END;
  INSERT dbo.Reservations(Code, CustomerName, Phone, Email, GuestCount, PreferredAreaId, StartsAt, EndsAt, Notes)
  VALUES(@code, LTRIM(RTRIM(@CustomerName)), @Phone, NULLIF(LTRIM(RTRIM(@Email)), ''), @GuestCount, @PreferredAreaId, @StartsAt, @ends, NULLIF(LTRIM(RTRIM(@Notes)), ''));
  DECLARE @id bigint = SCOPE_IDENTITY();
  INSERT dbo.ReservationEvents(ReservationId, ToStatus) VALUES(@id, 'Pending');
  EXEC dbo.usp_QueueBookingEmail @id, 'BookingReceived';
  SELECT @id AS ReservationId, @code AS Code;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_CreateReservation TO restaurant_app;
GO
