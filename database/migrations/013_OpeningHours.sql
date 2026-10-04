CREATE TYPE dbo.OpeningHoursWeek AS TABLE (
 DayOfWeek tinyint NOT NULL PRIMARY KEY,
 IsClosed bit NOT NULL,
 OpensAt time(0) NULL,
 ClosesAt time(0) NULL
);
GO
CREATE OR ALTER PROCEDURE dbo.usp_SaveOpeningHours
 @ActorUserId int, @Days dbo.OpeningHoursWeek READONLY
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId, 'Catalog.Manage';
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
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END;
