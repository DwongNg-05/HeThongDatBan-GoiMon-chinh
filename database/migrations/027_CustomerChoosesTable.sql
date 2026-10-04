-- Khách tự chọn bàn khi đặt: mã bàn (DiningTables.Code, ví dụ A05) là mã khách nhận trên trang xác nhận và trong email.
-- Bàn đã có lượt đặt (Chờ xác nhận / Đã xác nhận / Khách đã đến) trùng khung giờ thì không chọn được nữa.
-- Gọi usp_CreateReservation không truyền @TableId vẫn chạy như cũ (lượt chờ xác nhận chưa có bàn) để tương thích dữ liệu/kiểm thử cũ.
-- Cần migration 026.

-- 1) Danh sách bàn còn trống cho một khung giờ, số khách và khu vực (nếu có).
CREATE OR ALTER PROCEDURE dbo.usp_AvailableTables @StartsAt datetime2(3),@GuestCount int,@PreferredAreaId int=NULL
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @ends datetime2(3)=DATEADD(minute,(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1),@StartsAt);
 SELECT t.Id,t.Code,a.Id AS AreaId,a.Name AS AreaName,t.MinCapacity,t.MaxCapacity
 FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
 WHERE t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@GuestCount
   AND (@PreferredAreaId IS NULL OR t.AreaId=@PreferredAreaId)
   AND NOT EXISTS(SELECT 1 FROM dbo.Reservations r
     WHERE r.TableId=t.Id AND r.Status IN ('Pending','Confirmed','Arrived') AND r.StartsAt<@ends AND r.EndsAt>@StartsAt)
 ORDER BY a.SortOrder,a.Id,t.SortOrder,t.Code;
END;
GO
GRANT EXECUTE ON dbo.usp_AvailableTables TO restaurant_app;
GO

-- 2) Tạo lượt đặt bàn với bàn khách đã chọn (giống 017, thêm @TableId và trả về TableCode).
CREATE OR ALTER PROCEDURE dbo.usp_CreateReservation @CustomerName nvarchar(100),@Phone varchar(10),@GuestCount int,@StartsAt datetime2(3),
 @PreferredAreaId int=NULL,@Email nvarchar(254)=NULL,@Notes nvarchar(500)=NULL,@TableId int=NULL
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

-- 3) Email: thêm mã bàn vào thông tin lưu kèm (giống 026, thêm TableCode).
CREATE OR ALTER PROCEDURE dbo.usp_QueueBookingEmail @ReservationId bigint,@Kind varchar(30)
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @email nvarchar(254),@code char(6),@tableCode varchar(20),@payload nvarchar(max),@key varchar(100)=CONCAT(@Kind,':',@ReservationId);
 SELECT @email=r.Email,@code=r.Code,@tableCode=t.Code FROM dbo.Reservations r LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId WHERE r.Id=@ReservationId;
 IF @email IS NULL RETURN;
 SET @payload=(
  SELECT r.Code,t.Code AS TableCode,r.CustomerName,r.StartsAt,r.EndsAt,r.GuestCount,r.TableId,r.Status,
         COALESCE(r.AreaNameSnapshot,a.Name) AS AreaName,
         s.Name AS RestaurantName,s.Address AS RestaurantAddress,s.Phone AS RestaurantPhone
  FROM dbo.Reservations r
  LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId
  LEFT JOIN dbo.RestaurantSettings s ON s.Id=1
  LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
  WHERE r.Id=@ReservationId
  FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 IF NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE DedupeKey=@key)
  INSERT dbo.EmailOutbox(ReservationId,MessageType,Recipient,Subject,PayloadJson,DedupeKey)
  VALUES(@ReservationId,@Kind,@email,
   CASE @Kind WHEN 'BookingReceived' THEN CONCAT(N'Xác nhận đặt bàn ',COALESCE(@tableCode,@code))
              WHEN 'BookingConfirmed' THEN CONCAT(N'Đặt bàn ',COALESCE(@tableCode,@code),N' đã được xác nhận')
              ELSE N'Thông tin đặt bàn' END,
   @payload,@key);
END;
GO
