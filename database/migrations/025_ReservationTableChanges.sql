CREATE TABLE dbo.ReservationTableChanges(
 Id bigint IDENTITY PRIMARY KEY,
 ReservationId bigint NOT NULL REFERENCES dbo.Reservations(Id),
 OldTableId int NOT NULL REFERENCES dbo.DiningTables(Id),
 NewTableId int NOT NULL REFERENCES dbo.DiningTables(Id),
 OldTableCode varchar(20) NOT NULL, NewTableCode varchar(20) NOT NULL,
 ActorUserId int NOT NULL REFERENCES dbo.Users(Id), ActorName nvarchar(100) NOT NULL,
 ChangedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), Reason nvarchar(500) NULL,
 CONSTRAINT CK_ReservationTableChanges_Different CHECK(OldTableId<>NewTableId)
);
CREATE INDEX IX_ReservationTableChanges_History ON dbo.ReservationTableChanges(ReservationId,ChangedAt DESC,Id DESC);
GO
CREATE OR ALTER PROCEDURE dbo.usp_ChangeReservationTable
 @ReservationId bigint,@TableId int,@ExpectedTableId int,@ActorUserId int,@Reason nvarchar(max)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
  DECLARE @status varchar(20),@old int,@start datetime2(3),@end datetime2(3),@guests int,@newCode varchar(20),@now datetime2(3)=SYSUTCDATETIME();
  SELECT @status=Status,@old=TableId,@start=StartsAt,@end=EndsAt,@guests=GuestCount
   FROM dbo.Reservations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ReservationId;
  IF @status IS NULL THROW 51701,N'Không tìm thấy lượt đặt.',1;
  IF @status<>'Confirmed' THROW 51702,N'Chỉ có thể đổi bàn cho lượt đã xác nhận.',1;
  IF @start<=@now THROW 51703,N'Đã đến hoặc quá giờ hẹn. Không thể đổi bàn.',1;
  IF @ExpectedTableId IS NULL OR @old<>@ExpectedTableId THROW 51704,N'Bàn đã được nhân viên khác thay đổi. Vui lòng kiểm tra thông tin mới.',1;
  IF @TableId=@old THROW 51705,N'Vui lòng chọn bàn khác với bàn đang giữ.',1;
  IF DATALENGTH(@Reason)>1000 THROW 51706,N'Lý do đổi bàn tối đa 500 ký tự.',1;
  SELECT @newCode=t.Code FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
   WHERE t.Id=@TableId AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@guests;
  IF @newCode IS NULL THROW 51008,N'Bàn không còn hoạt động hoặc không đủ chỗ. Vui lòng chọn bàn khác.',1;
  IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=@TableId AND Id<>@ReservationId AND Status IN ('Confirmed','Arrived') AND StartsAt<@end AND EndsAt>@start)
   THROW 51009,N'Bàn vừa được giữ trong khung giờ này. Vui lòng chọn bàn khác.',1;
  UPDATE dbo.Reservations SET TableId=@TableId WHERE Id=@ReservationId;
  INSERT dbo.ReservationTableChanges(ReservationId,OldTableId,NewTableId,OldTableCode,NewTableCode,ActorUserId,ActorName,ChangedAt,Reason)
   SELECT @ReservationId,@old,@TableId,t.Code,@newCode,@ActorUserId,u.UserName,@now,NULLIF(LTRIM(RTRIM(@Reason)),N'')
   FROM dbo.DiningTables t CROSS JOIN dbo.Users u WHERE t.Id=@old AND u.Id=@ActorUserId;
  -- Preserve the operational map's existing 30-minute window; future holds belong to the timetable.
  UPDATE t SET Status=CASE WHEN EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status IN ('Confirmed','Arrived')
    AND r.StartsAt<=DATEADD(minute,30,@now) AND r.EndsAt>@now) THEN 'Reserved' ELSE 'Available' END,
    StatusChangedAt=@now
   FROM dbo.DiningTables t WHERE t.Id IN (@old,@TableId) AND t.Status IN ('Available','Reserved');
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ReservationTableChangeDetails @ActorUserId int,@ReservationId bigint
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
 SELECT t.Id,t.Code,t.MaxCapacity,a.Name AS AreaName FROM dbo.Reservations r
 CROSS JOIN dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
 WHERE r.Id=@ReservationId AND r.Status='Confirmed' AND r.StartsAt>SYSUTCDATETIME()
 AND t.Id<>r.TableId AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=r.GuestCount
 AND NOT EXISTS(SELECT 1 FROM dbo.Reservations other WHERE other.TableId=t.Id AND other.Id<>r.Id
  AND other.Status IN ('Confirmed','Arrived') AND other.StartsAt<r.EndsAt AND other.EndsAt>r.StartsAt)
 ORDER BY CASE WHEN t.AreaId=r.PreferredAreaId THEN 0 ELSE 1 END,t.MaxCapacity,t.SortOrder,t.Code,t.Id;
 SELECT Id,OldTableCode,NewTableCode,ActorName,ChangedAt,Reason FROM dbo.ReservationTableChanges
 WHERE ReservationId=@ReservationId ORDER BY ChangedAt DESC,Id DESC;

END;
GO
GRANT EXECUTE ON dbo.usp_ChangeReservationTable TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ReservationTableChangeDetails TO restaurant_app;
DENY INSERT,UPDATE,DELETE ON dbo.ReservationTableChanges TO restaurant_app;
GO
CREATE TRIGGER dbo.tr_ReservationTableChanges_Immutable ON dbo.ReservationTableChanges
AFTER UPDATE,DELETE AS
BEGIN
 IF EXISTS(SELECT 1 FROM deleted) THROW 51707,N'Không được sửa hoặc xóa lịch sử đổi bàn.',1;
END;
GO


CREATE OR ALTER PROCEDURE dbo.usp_ReservationConfirmationDetails @ActorUserId int,@ReservationId bigint
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Reservations.Manage';
 SELECT r.Id,r.Code,r.CustomerName,r.Phone,r.Email,r.GuestCount,r.StartsAt,r.EndsAt,r.Status,
  COALESCE(r.AreaNameSnapshot,a.Name) AS AreaName,t.Code AS TableCode,
  r.TableId,r.RejectionReason,o.Status AS EmailStatus,o.LastError AS EmailError,o.AttemptCount
 FROM dbo.Reservations r LEFT JOIN dbo.Areas a ON a.Id=r.PreferredAreaId
 LEFT JOIN dbo.DiningTables t ON t.Id=r.TableId
 LEFT JOIN dbo.EmailOutbox o ON o.DedupeKey=CONCAT('BookingConfirmed:',r.Id)
 WHERE r.Id=@ReservationId;
 -- Preferred area is a preference: show all eligible areas, preferred first.
 SELECT t.Id,t.Code,t.MaxCapacity,a.Name AS AreaName
 FROM dbo.Reservations r CROSS JOIN dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
 WHERE r.Id=@ReservationId AND r.Status='Pending' AND r.StartsAt>SYSUTCDATETIME()
 AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=r.GuestCount
 AND NOT EXISTS(SELECT 1 FROM dbo.Reservations other WHERE other.TableId=t.Id
   AND other.Status IN ('Confirmed','Arrived') AND other.StartsAt<r.EndsAt AND other.EndsAt>r.StartsAt)
 ORDER BY CASE WHEN t.AreaId=r.PreferredAreaId THEN 0 ELSE 1 END,t.MaxCapacity,t.SortOrder,t.Code,t.Id;
END;
GO
