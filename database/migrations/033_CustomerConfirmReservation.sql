-- Khách tự xác nhận lượt đặt bàn bằng nút "Xác nhận đặt bàn" trong email (không cần đăng nhập).
-- Web chỉ gọi thủ tục này sau khi đã kiểm tra mã bảo mật trong liên kết email (ASP.NET Data Protection),
-- nên thủ tục nhận mã đặt bàn (Reservations.Code) thay cho tài khoản nhân viên.
-- Chờ xác nhận -> Đã xác nhận; bấm lại khi đã xác nhận thì báo "đã xác nhận" (không lỗi). Chạy lại vẫn an toàn.
-- Cần migration 027 (khách chọn bàn khi đặt).
CREATE OR ALTER PROCEDURE dbo.usp_CustomerConfirmReservation @Code char(6)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;

  DECLARE @id bigint,@status varchar(20),@table int,@start datetime2(3),@end datetime2(3),@guests int;
  SELECT @id=Id,@status=Status,@table=TableId,@start=StartsAt,@end=EndsAt,@guests=GuestCount
  FROM dbo.Reservations WITH(UPDLOCK) WHERE Code=@Code;

  IF @id IS NULL THROW 51440,N'Không tìm thấy lượt đặt bàn.',1;
  IF @status IN ('Confirmed','Arrived')
  BEGIN
   SELECT @id AS ReservationId,'AlreadyConfirmed' AS Result;
   COMMIT;
   RETURN;
  END;
  IF @status<>'Pending' THROW 51441,N'Lượt đặt bàn này đã bị huỷ hoặc không còn chờ xác nhận.',1;
  IF @start<=SYSUTCDATETIME() THROW 51442,N'Đã quá giờ đặt bàn nên không thể xác nhận nữa. Vui lòng đặt bàn mới.',1;
  IF @table IS NULL THROW 51443,N'Lượt đặt bàn chưa có bàn. Nhà hàng sẽ liên hệ để sắp xếp và xác nhận giúp bạn.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
   WHERE t.Id=@table AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@guests)
   THROW 51444,N'Bàn bạn chọn không còn phục vụ. Vui lòng liên hệ nhà hàng hoặc đặt bàn mới.',1;
  IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE Id<>@id AND TableId=@table
   AND Status IN ('Confirmed','Arrived') AND StartsAt<@end AND EndsAt>@start)
   THROW 51445,N'Bàn đã được xác nhận cho khách khác trong khung giờ này. Vui lòng liên hệ nhà hàng.',1;

  UPDATE dbo.Reservations SET Status='Confirmed',ConfirmedAt=SYSUTCDATETIME() WHERE Id=@id;
  INSERT dbo.ReservationEvents(ReservationId,FromStatus,ToStatus,NewTableId,Reason)
   VALUES(@id,'Pending','Confirmed',@table,N'Khách xác nhận qua email');
  -- Giống usp_ConfirmReservation: bàn chuyển "Đã đặt trước" khi giờ nhận bàn còn trong vòng 30 phút.
  UPDATE dbo.DiningTables SET Status='Reserved',StatusChangedAt=SYSUTCDATETIME()
   WHERE Id=@table AND Status='Available' AND @start<=DATEADD(minute,30,SYSUTCDATETIME());

  SELECT @id AS ReservationId,'Confirmed' AS Result;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_CustomerConfirmReservation TO restaurant_app;
GO
