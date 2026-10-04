-- Quản lý xoá hẳn một lượt đặt bàn đã kết thúc (Đã huỷ / Bị từ chối / Khách không đến) khỏi danh sách.
-- Lượt còn hiệu lực (Chờ xác nhận / Đã xác nhận) phải được huỷ trước; lượt khách đã đến (có phiên phục vụ) không xoá.
-- Xoá kèm lịch sử trạng thái và email của lượt đó, giống usp_RunMaintenance khi xoá dữ liệu đặt bàn quá hạn.
CREATE OR ALTER PROCEDURE dbo.usp_DeleteReservation @ReservationId bigint,@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';

  DECLARE @status varchar(20);
  SELECT @status=Status FROM dbo.Reservations WITH(UPDLOCK) WHERE Id=@ReservationId;
  IF @status IS NULL THROW 51012,N'Không tìm thấy lượt đặt bàn.',1;
  IF @status NOT IN ('Cancelled','Rejected','NoShow')
   THROW 51017,N'Chỉ xoá được lượt đặt bàn đã huỷ, bị từ chối hoặc khách không đến. Hãy huỷ lượt đặt bàn trước khi xoá.',1;
  IF EXISTS(SELECT 1 FROM dbo.DiningSessions WHERE ReservationId=@ReservationId AND Status<>'Closed')
   THROW 51018,N'Lượt đặt bàn đang gắn với một phiên phục vụ chưa đóng, chưa thể xoá.',1;

  UPDATE dbo.DiningSessions SET ReservationId=NULL WHERE ReservationId=@ReservationId;
  DELETE dbo.EmailOutbox WHERE ReservationId=@ReservationId;
  DELETE dbo.ReservationEvents WHERE ReservationId=@ReservationId;
  DELETE dbo.Reservations WHERE Id=@ReservationId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_DeleteReservation TO restaurant_app;
GO
