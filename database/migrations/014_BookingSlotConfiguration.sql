CREATE OR ALTER PROCEDURE dbo.usp_SaveOpeningHours
 @ActorUserId int, @Days dbo.OpeningHoursWeek READONLY, @DefaultBookingMinutes int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId, 'Catalog.Manage';
 IF @DefaultBookingMinutes IS NULL OR @DefaultBookingMinutes NOT BETWEEN 30 AND 360
  THROW 51502,N'Thời lượng giữ bàn phải từ 30 đến 360 phút.',1;
 IF (SELECT COUNT(*) FROM @Days)<>7
    OR EXISTS(SELECT 1 FROM @Days WHERE DayOfWeek NOT BETWEEN 1 AND 7
       OR (IsClosed=0 AND (OpensAt IS NULL OR ClosesAt IS NULL OR ClosesAt<=OpensAt)))
  THROW 51501,N'Cần đủ 7 ngày; giờ đóng cửa phải lớn hơn giờ mở cửa cho các ngày hoạt động.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  UPDATE h SET IsClosed=d.IsClosed,
   OpensAt=CASE WHEN d.IsClosed=1 THEN NULL ELSE d.OpensAt END,
   ClosesAt=CASE WHEN d.IsClosed=1 THEN NULL ELSE d.ClosesAt END
  FROM dbo.OpeningHours h JOIN @Days d ON h.DayOfWeek=d.DayOfWeek;
  INSERT dbo.OpeningHours(DayOfWeek,IsClosed,OpensAt,ClosesAt)
  SELECT d.DayOfWeek,d.IsClosed,CASE WHEN d.IsClosed=1 THEN NULL ELSE d.OpensAt END,
   CASE WHEN d.IsClosed=1 THEN NULL ELSE d.ClosesAt END
  FROM @Days d WHERE NOT EXISTS(SELECT 1 FROM dbo.OpeningHours h WHERE h.DayOfWeek=d.DayOfWeek);
  UPDATE dbo.RestaurantSettings SET DefaultBookingMinutes=@DefaultBookingMinutes WHERE Id=1;
  IF @@ROWCOUNT<>1 THROW 51502,N'Không tìm thấy cấu hình nhà hàng.',1;
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END;

