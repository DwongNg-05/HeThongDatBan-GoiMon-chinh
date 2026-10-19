-- S2-03: chống trùng bàn — đồng bộ khoảng dọn bàn 15 phút cho luồng khách tự chọn bàn.
--
-- Quy ước (chốt với PO, xem docs/S2-03-ChongTrungBan.md):
--   * Giờ kết thúc lượt đặt = giờ bắt đầu + RestaurantSettings.DefaultBookingMinutes (mặc định 90 phút).
--   * Mỗi lượt giữ bàn thêm 15 phút dọn bàn SAU giờ kết thúc: [StartsAt, EndsAt + 15 phút).
--   * Hai lượt cùng bàn xung đột khi A.StartsAt < B.EndsAt + 15 phút VÀ B.StartsAt < A.EndsAt + 15 phút.
--     Bắt đầu đúng mốc hết 15 phút dọn bàn thì KHÔNG xung đột.
--   * Chỉ lượt Chờ xác nhận (Pending) và Đã xác nhận (Confirmed) giữ bàn trong ràng buộc database
--     (trg_Reservations_PreventTableHoldOverlap, migration 028); kiểm tra trước khi ghi ở dưới tính thêm lượt
--     khách đã tới (Arrived). Huỷ / Không tới / Từ chối không giữ bàn.
--
-- Trước migration này, danh sách bàn trống (usp_AvailableTables) và bước kiểm tra của usp_CreateReservation
-- chỉ so khung giờ, không tính 15 phút dọn bàn: khách vẫn thấy và chọn được một bàn mà trigger sẽ từ chối.
-- Trigger database không đổi và vẫn là chốt chặn cuối, kể cả khi ghi SQL trực tiếp.

CREATE OR ALTER PROCEDURE dbo.usp_AvailableTables @StartsAt datetime2(3),@GuestCount int,@PreferredAreaId int=NULL
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @duration int=(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1);
 IF @duration IS NULL OR @duration<=0 SET @duration=90;
 DECLARE @ends datetime2(3)=DATEADD(minute,@duration,@StartsAt);
 SELECT t.Id,t.Code,a.Id AS AreaId,a.Name AS AreaName,t.MinCapacity,t.MaxCapacity
 FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
 WHERE t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@GuestCount
   AND (@PreferredAreaId IS NULL OR t.AreaId=@PreferredAreaId)
   AND NOT EXISTS(SELECT 1 FROM dbo.Reservations r
     WHERE r.TableId=t.Id AND r.Status IN ('Pending','Confirmed','Arrived')
       AND r.StartsAt<DATEADD(minute,15,@ends) AND @StartsAt<DATEADD(minute,15,r.EndsAt))
 ORDER BY a.SortOrder,a.Id,t.SortOrder,t.Code;
END;
GO

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
   -- Mỗi lượt giữ bàn từ giờ bắt đầu tới giờ kết thúc + 15 phút dọn bàn (cùng quy tắc với trigger
   -- trg_Reservations_PreventTableHoldOverlap); bắt đầu đúng mốc hết 15 phút dọn bàn thì được nhận.
   IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=@TableId AND Status IN ('Pending','Confirmed','Arrived')
     AND StartsAt<DATEADD(minute,15,@ends) AND @StartsAt<DATEADD(minute,15,EndsAt))
    THROW 51414,N'Bàn bạn chọn vừa có khách khác đặt trong khung giờ này (tính cả 15 phút dọn bàn). Vui lòng chọn bàn hoặc giờ khác.',1;
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
