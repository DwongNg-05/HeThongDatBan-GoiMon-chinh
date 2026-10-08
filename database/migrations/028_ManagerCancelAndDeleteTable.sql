-- Quản lý huỷ lượt đặt bàn và xoá bàn. Cần migration 027.

-- 1) Email: thêm lý do huỷ vào thông tin lưu kèm và tiêu đề riêng cho email huỷ (giống 027, thêm CancelReason).
CREATE OR ALTER PROCEDURE dbo.usp_QueueBookingEmail @ReservationId bigint,@Kind varchar(30)
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @email nvarchar(254),@code char(6),@tableCode varchar(20),@payload nvarchar(max),@key varchar(100)=CONCAT(@Kind,':',@ReservationId);
 SELECT @email=r.Email,@code=r.Code,@tableCode=t.Code FROM dbo.Reservations r LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId WHERE r.Id=@ReservationId;
 IF @email IS NULL RETURN;
 SET @payload=(
  SELECT r.Code,t.Code AS TableCode,r.CustomerName,r.StartsAt,r.EndsAt,r.GuestCount,r.TableId,r.Status,r.CancelReason,
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
              WHEN 'BookingCancelled' THEN CONCAT(N'Đã huỷ đặt bàn ',COALESCE(@tableCode,@code))
              ELSE N'Thông tin đặt bàn' END,
   @payload,@key);
END;
GO

-- 2) Nhân viên có quyền Reservations.Manage huỷ một lượt đặt bàn (giao diện chỉ mở cho Quản lý).
--    Khác usp_CancelReservation của khách: không cần số điện thoại, không giới hạn giờ huỷ, bắt buộc ghi lý do.
CREATE OR ALTER PROCEDURE dbo.usp_StaffCancelReservation @ReservationId bigint,@Reason nvarchar(500),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';

  SET @Reason=NULLIF(LTRIM(RTRIM(@Reason)),N'');
  IF @Reason IS NULL THROW 51015,N'Vui lòng nhập lý do huỷ đặt bàn.',1;
  DECLARE @status varchar(20),@table int;
  SELECT @status=Status,@table=TableId FROM dbo.Reservations WITH(UPDLOCK) WHERE Id=@ReservationId;
  IF @status IS NULL THROW 51012,N'Không tìm thấy lượt đặt bàn.',1;
  IF @status NOT IN ('Pending','Confirmed')
   THROW 51016,N'Chỉ huỷ được lượt đặt bàn đang chờ xác nhận hoặc đã xác nhận.',1;

  UPDATE dbo.Reservations SET Status='Cancelled',CancelledAt=SYSUTCDATETIME(),CancelReason=@Reason WHERE Id=@ReservationId;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,ActorUserId,Reason) VALUES(@ReservationId,@status,'Cancelled',@ActorUserId,@Reason);
  UPDATE dbo.EmailOutbox SET Status='Cancelled' WHERE ReservationId=@ReservationId AND MessageType='BookingReminder' AND Status IN ('Pending','Processing');
  EXEC dbo.usp_QueueBookingEmail @ReservationId,'BookingCancelled';
  -- Bàn được trả lại cho khách khác (danh sách bàn trống chỉ tính lượt Chờ xác nhận/Đã xác nhận/Đã đến).
  UPDATE dbo.DiningTables SET Status='Available',StatusChangedAt=SYSUTCDATETIME()
   WHERE Id=@table AND Status='Reserved' AND NOT EXISTS(SELECT 1 FROM dbo.Reservations
    WHERE TableId=@table AND Status='Confirmed' AND StartsAt<=DATEADD(minute,30,SYSUTCDATETIME()) AND EndsAt>SYSUTCDATETIME());
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_StaffCancelReservation TO restaurant_app;
GO

-- 3) Quản lý xoá bàn (quyền Catalog.Manage).
--    - Bàn đang phục vụ hoặc còn lượt đặt sắp tới (Chờ xác nhận/Đã xác nhận) thì không xoá được.
--    - Bàn chưa từng dùng (không có đặt bàn, phiên phục vụ, món đã gọi): xoá hẳn cùng mã QR.
--    - Bàn đã có lịch sử: không xoá dữ liệu cũ mà chuyển sang "Ngừng sử dụng" và thu hồi mã QR.
--    Trả về Result = 'Deleted' hoặc 'Deactivated'.
CREATE OR ALTER PROCEDURE dbo.usp_DeleteTable @TableId int,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';

  DECLARE @code varchar(20),@status varchar(20);
  SELECT @code=Code,@status=Status FROM dbo.DiningTables WITH(UPDLOCK) WHERE Id=@TableId;
  IF @code IS NULL THROW 51420,N'Không tìm thấy bàn.',1;
  IF @status='Serving' OR EXISTS(SELECT 1 FROM dbo.SessionTables st JOIN dbo.DiningSessions s ON s.Id=st.SessionId
     WHERE st.TableId=@TableId AND st.ReleasedAt IS NULL AND s.Status<>'Closed')
   THROW 51421,N'Bàn đang phục vụ khách, chưa thể xoá.',1;
  DECLARE @next datetime2(3)=(SELECT MIN(StartsAt) FROM dbo.Reservations
     WHERE TableId=@TableId AND Status IN ('Pending','Confirmed') AND EndsAt>SYSUTCDATETIME());
  IF @next IS NOT NULL
  BEGIN
   DECLARE @message nvarchar(300)=CONCAT(N'Bàn ',@code,N' còn lượt đặt bàn sắp tới (',FORMAT(DATEADD(hour,7,@next),'dd/MM/yyyy HH:mm'),
     N'). Hãy huỷ hoặc chuyển các lượt đặt đó sang bàn khác trước khi xoá.');
   THROW 51422,@message,1;
  END;

  IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=@TableId)
     OR EXISTS(SELECT 1 FROM dbo.ReservationEvents WHERE OldTableId=@TableId OR NewTableId=@TableId)
     OR EXISTS(SELECT 1 FROM dbo.SessionTables WHERE TableId=@TableId)
     OR EXISTS(SELECT 1 FROM dbo.OrderItems WHERE OriginalTableId=@TableId)
  BEGIN
   UPDATE dbo.TableQrCodes SET RevokedAt=SYSUTCDATETIME() WHERE TableId=@TableId AND RevokedAt IS NULL;
   UPDATE dbo.DiningTables SET IsActive=0 WHERE Id=@TableId;
   SELECT 'Deactivated' AS Result,@code AS Code;
  END
  ELSE
  BEGIN
   DELETE dbo.TableQrCodes WHERE TableId=@TableId;
   DELETE dbo.DiningTables WHERE Id=@TableId;
   SELECT 'Deleted' AS Result,@code AS Code;
  END;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_DeleteTable TO restaurant_app;
GO
