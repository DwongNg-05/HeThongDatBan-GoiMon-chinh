-- Email báo từ chối đặt bàn: khi Quản lý / Phục vụ từ chối một lượt chờ xác nhận, khách nhận email kèm lý do
-- (Hết bàn / Ngoài giờ phục vụ / Không liên lạc được).
--   * usp_QueueBookingEmail: thêm RejectionReason vào nội dung email và tiêu đề cho loại 'BookingRejected'.
--   * usp_RejectReservation: xếp email 'BookingRejected' vào dbo.EmailOutbox cùng giao dịch từ chối.
--   * usp_ClaimDueBookingEmail: email từ chối gửi lỗi cũng được tự gửi lại (5 phút / lần, tối đa 3 lần).

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
  SELECT r.Code,t.Code AS TableCode,r.CustomerName,r.StartsAt,r.EndsAt,r.GuestCount,r.TableId,r.Status,r.CancelReason,r.RejectionReason,
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
              WHEN 'BookingRejected' THEN CONCAT(@restaurant,N' - Không thể nhận đặt bàn ',@code)
              ELSE CONCAT(@restaurant,N' - Thông tin đặt bàn ',@code) END,
   @payload,@key);
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_RejectReservation
 @ReservationId bigint,@Reason nvarchar(100),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
  IF @Reason IS NULL OR NOT EXISTS(SELECT 1 FROM (VALUES(N'NoTable'),(N'OutsideHours'),(N'Unreachable')) reasons(Code)
    WHERE Code COLLATE Latin1_General_100_BIN2=@Reason COLLATE Latin1_General_100_BIN2 AND DATALENGTH(Code)=DATALENGTH(@Reason))
   THROW 51010,N'Vui lòng chọn một trong ba lý do từ chối hợp lệ.',1;
  DECLARE @tableId int,@found bit=0;
  SELECT @tableId=TableId,@found=1 FROM dbo.Reservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId AND Status='Pending';
  IF @found=0
   THROW 51011,N'Chỉ có thể từ chối lượt đặt đang chờ. Lượt đặt đã được xử lý hoặc không tồn tại.',1;
  UPDATE dbo.Reservations SET Status='Rejected',RejectionReason=@Reason,TableId=NULL WHERE Id=@ReservationId;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,OldTableId,ActorUserId,Reason)
   VALUES(@ReservationId,'Pending','Rejected',@tableId,@ActorUserId,@Reason);
  -- Email báo từ chối kèm lý do (khách không để lại email thì bỏ qua). Web gửi ngay sau khi lệnh này thành công.
  EXEC dbo.usp_QueueBookingEmail @ReservationId,'BookingRejected';
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO

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
    AND MessageType IN ('BookingReceived','BookingConfirmed','BookingCancelled','BookingRejected')
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
