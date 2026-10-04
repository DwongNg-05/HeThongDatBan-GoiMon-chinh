-- S2-09 Task 1: email xác nhận đặt bàn gửi ngay sau khi lượt đặt bàn được ghi nhận.
-- Nội dung đã chốt với PO: mã đặt bàn, ngày giờ, số khách, địa chỉ quán (kèm tên khách, tên quán, số điện thoại quán).
-- Cần migration 024 (cột EmailOutbox.LastAttemptAt).

-- 1) Lưu sẵn thông tin dùng cho email vào PayloadJson ngay khi xếp hàng (ảnh chụp tại thời điểm đặt bàn),
--    gồm cả tên/địa chỉ/điện thoại quán từ RestaurantSettings. Tiêu đề email chứa mã đặt bàn.
CREATE OR ALTER PROCEDURE dbo.usp_QueueBookingEmail @ReservationId bigint,@Kind varchar(30)
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @email nvarchar(254),@code char(6),@payload nvarchar(max),@key varchar(100)=CONCAT(@Kind,':',@ReservationId);
 SELECT @email=Email,@code=Code FROM dbo.Reservations WHERE Id=@ReservationId;
 IF @email IS NULL RETURN;
 SET @payload=(
  SELECT r.Code,r.CustomerName,r.StartsAt,r.EndsAt,r.GuestCount,r.TableId,r.Status,
         COALESCE(r.AreaNameSnapshot,a.Name) AS AreaName,
         s.Name AS RestaurantName,s.Address AS RestaurantAddress,s.Phone AS RestaurantPhone
  FROM dbo.Reservations r
  LEFT JOIN dbo.RestaurantSettings s ON s.Id=1
  LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
  WHERE r.Id=@ReservationId
  FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 IF NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE DedupeKey=@key)
  INSERT dbo.EmailOutbox(ReservationId,MessageType,Recipient,Subject,PayloadJson,DedupeKey)
  VALUES(@ReservationId,@Kind,@email,
   CASE @Kind WHEN 'BookingReceived' THEN CONCAT(N'Xác nhận đặt bàn ',@code)
              WHEN 'BookingConfirmed' THEN CONCAT(N'Đặt bàn ',@code,N' đã được xác nhận')
              ELSE N'Thông tin đặt bàn' END,
   @payload,@key);
END;
GO
-- 2) Web nhận đúng email của lượt đặt bàn vừa tạo để gửi ngay (không chờ worker).
--    Cùng quy tắc với usp_ClaimEmail: tăng AttemptCount, khoá 2 phút, ghi LastAttemptAt.
CREATE OR ALTER PROCEDURE dbo.usp_ClaimReservationEmail @ReservationId bigint,@MessageType varchar(30)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  DECLARE @id bigint=(SELECT TOP(1) Id FROM dbo.EmailOutbox
   WHERE ReservationId=@ReservationId AND MessageType=@MessageType AND Status='Pending' AND AttemptCount<4 ORDER BY Id);
  IF @id IS NOT NULL
   UPDATE dbo.EmailOutbox SET Status='Processing',AttemptCount=AttemptCount+1,LockedUntil=DATEADD(minute,2,SYSUTCDATETIME()),
    LastAttemptAt=SYSUTCDATETIME()
    OUTPUT inserted.Id,inserted.AttemptCount,inserted.Recipient,inserted.Subject,inserted.PayloadJson,inserted.MessageType
    WHERE Id=@id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_ClaimReservationEmail TO restaurant_app;
GRANT EXECUTE ON dbo.usp_CompleteEmail TO restaurant_app;
GO
