-- Email đặt bàn chỉ gửi khi Quản lý / Phục vụ XÁC NHẬN lượt đặt (không gửi lúc khách vừa đặt).
--
-- Luồng: khách đặt bàn (chưa có email, lượt "Chờ xác nhận") -> nhân viên xác nhận và chọn bàn (usp_ConfirmReservation
-- xếp email 'BookingConfirmed') -> web gửi ngay email kèm MÃ ĐẶT BÀN (6 ký tự, dùng để tra cứu) và bàn đã giữ
-- -> khách tra cứu bằng mã đặt bàn + số điện thoại. Khách tra cứu trước khi được xác nhận thì thấy "Chờ xác nhận".
--
--  * usp_QueueBookingEmail: bỏ qua 'BookingReceived' (mọi thủ tục tạo lượt đặt đang gọi nó lúc khách đặt);
--    tiêu đề mới dạng "Bếp Nhà - Xác nhận đặt bàn ED095C" (tên nhà hàng trước, mã đặt bàn sau).
--  * Email 'BookingReceived' còn chờ gửi / chờ gửi lại từ trước: dừng gửi (Cancelled).
--  * Gửi lại tự động (usp_ClaimDueBookingEmail) và trạng thái email trên lượt đặt (usp_SyncReservationEmailStatus)
--    áp dụng cho email xác nhận của nhân viên ('BookingConfirmed').
-- Chạy lại an toàn.

CREATE OR ALTER PROCEDURE dbo.usp_QueueBookingEmail @ReservationId bigint,@Kind varchar(30)
AS
BEGIN
 SET NOCOUNT ON;
 -- Khách vừa đặt bàn: chưa gửi email; email chỉ gửi sau khi nhân viên xác nhận (BookingConfirmed).
 IF @Kind='BookingReceived' RETURN;
 DECLARE @email nvarchar(254),@code char(6),@restaurant nvarchar(150),@payload nvarchar(max),@key varchar(100)=CONCAT(@Kind,':',@ReservationId);
 SELECT @email=r.Email,@code=r.Code FROM dbo.Reservations r WHERE r.Id=@ReservationId;
 IF @email IS NULL RETURN;
 SET @restaurant=COALESCE(NULLIF(LTRIM(RTRIM((SELECT Name FROM dbo.RestaurantSettings WHERE Id=1))),N''),N'Bếp Nhà');
 SET @payload=(
  SELECT r.Code,t.Code AS TableCode,r.CustomerName,r.StartsAt,r.EndsAt,r.GuestCount,r.TableId,r.Status,r.CancelReason,
         COALESCE(r.AreaNameSnapshot,a.Name,ta.Name) AS AreaName,
         s.Name AS RestaurantName,s.Address AS RestaurantAddress,s.Phone AS RestaurantPhone
  FROM dbo.Reservations r
  LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId
  LEFT JOIN dbo.Areas ta ON ta.Id=t.AreaId
  LEFT JOIN dbo.RestaurantSettings s ON s.Id=1
  LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
  WHERE r.Id=@ReservationId
  FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 IF NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE DedupeKey=@key)
  INSERT dbo.EmailOutbox(ReservationId,MessageType,Recipient,Subject,PayloadJson,DedupeKey)
  VALUES(@ReservationId,@Kind,@email,
   CASE @Kind WHEN 'BookingConfirmed' THEN CONCAT(@restaurant,N' - Xác nhận đặt bàn ',@code)
              WHEN 'BookingCancelled' THEN CONCAT(@restaurant,N' - Đã huỷ đặt bàn ',@code)
              ELSE CONCAT(@restaurant,N' - Thông tin đặt bàn ',@code) END,
   @payload,@key);
END;
GO

-- Email "đã ghi nhận" cũ chưa gửi xong: không gửi nữa (khách sẽ nhận email khi nhân viên xác nhận).
UPDATE r SET ConfirmationEmailStatus='Cancelled',ConfirmationEmailUpdatedAt=SYSUTCDATETIME()
FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id
WHERE e.MessageType='BookingReceived' AND e.Status='Pending';
UPDATE dbo.EmailOutbox SET Status='Cancelled',LockedUntil=NULL
WHERE MessageType='BookingReceived' AND Status='Pending';
GO

-- Trạng thái email trên lượt đặt: theo email xác nhận của nhân viên (và email cũ 'BookingReceived' nếu có).
CREATE OR ALTER PROCEDURE dbo.usp_SyncReservationEmailStatus @EmailId bigint
AS
BEGIN
 SET NOCOUNT ON;
 UPDATE r SET
  ConfirmationEmailStatus=CASE e.Status WHEN 'Sent' THEN 'Sent' WHEN 'Failed' THEN 'Failed' WHEN 'Cancelled' THEN 'Cancelled'
   WHEN 'Processing' THEN 'Sending' ELSE CASE WHEN e.AttemptCount>0 THEN 'Retrying' ELSE 'Pending' END END,
  ConfirmationEmailAttempts=e.AttemptCount,
  ConfirmationEmailUpdatedAt=SYSUTCDATETIME()
 FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.ReservationId=r.Id
 WHERE e.Id=@EmailId AND e.MessageType IN ('BookingReceived','BookingConfirmed');
END;
GO

-- Gửi lại tự động (5 phút / lần, tối đa 3 lần) cả cho email xác nhận của nhân viên.
CREATE OR ALTER PROCEDURE dbo.usp_ClaimDueBookingEmail
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_ExpireStaleEmailAttempts;
  DECLARE @id bigint=(SELECT TOP(1) Id FROM dbo.EmailOutbox
   WHERE Status='Pending' AND AttemptCount BETWEEN 1 AND 3 AND NextAttemptAt<=SYSUTCDATETIME()
    AND MessageType IN ('BookingReceived','BookingConfirmed','BookingCancelled')
   ORDER BY NextAttemptAt,Id);
  IF @id IS NOT NULL EXEC dbo.usp_StartEmailAttempt @id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
