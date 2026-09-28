-- S1-06 slices 2-4. Never rewrite applied migrations or merge existing areas.
CREATE OR ALTER FUNCTION dbo.fn_NormalizeAreaName(@Name nvarchar(80))
RETURNS nvarchar(80) WITH SCHEMABINDING
AS
BEGIN
 DECLARE @result nvarchar(80)=ISNULL(@Name,N'');
 DECLARE @code int=9;
 WHILE @code<=13
 BEGIN
  SET @result=REPLACE(@result,NCHAR(@code),N' ');
  SET @code+=1;
 END;
 SET @result=REPLACE(REPLACE(REPLACE(@result,NCHAR(160),N' '),NCHAR(8239),N' '),NCHAR(12288),N' ');
 SET @code=8192;
 WHILE @code<=8202
 BEGIN
  SET @result=REPLACE(@result,NCHAR(@code),N' ');
  SET @code+=1;
 END;
 SET @result=LTRIM(RTRIM(@result));
 WHILE CHARINDEX(N'  ',@result)>0 SET @result=REPLACE(@result,N'  ',N' ');
 RETURN @result;
END;
GO
IF EXISTS(SELECT dbo.fn_NormalizeAreaName(Name) COLLATE Vietnamese_100_CI_AS
 FROM dbo.Areas GROUP BY dbo.fn_NormalizeAreaName(Name) COLLATE Vietnamese_100_CI_AS HAVING COUNT(*)>1)
 THROW 51406,N'Có tên khu vực cũ trùng sau chuẩn hóa. Hãy đổi tên các khu vực trùng trước khi chạy lại migration; không tự gộp hoặc xóa dữ liệu.',1;
DROP INDEX UX_Areas_Name ON dbo.Areas;
ALTER TABLE dbo.Areas ADD CanonicalName AS (dbo.fn_NormalizeAreaName(Name) COLLATE Vietnamese_100_CI_AS) PERSISTED;
CREATE UNIQUE INDEX UX_Areas_Name ON dbo.Areas(CanonicalName);
ALTER TABLE dbo.Areas ADD CONSTRAINT CK_Areas_SortOrder CHECK(SortOrder>=0);
GO
CREATE OR ALTER PROCEDURE dbo.usp_ListAreas
AS
BEGIN
 SET NOCOUNT ON;
 SELECT Id,Name,SortOrder,Notes,IsActive FROM dbo.Areas ORDER BY SortOrder,Name,Id;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CreateArea
 @ActorUserId int,@Name nvarchar(max),@SortOrder int,@Notes nvarchar(max)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';
 IF @Name IS NULL OR DATALENGTH(@Name)>160 OR LEN(dbo.fn_NormalizeAreaName(@Name))=0
  THROW 51401,N'Tên khu vực phải có từ 1 đến 80 ký tự.',1;
 IF @SortOrder IS NULL OR @SortOrder<0 THROW 51403,N'Thứ tự hiển thị phải là số nguyên không âm.',1;
 IF DATALENGTH(@Notes)>1000 THROW 51405,N'Ghi chú không được vượt quá 500 ký tự.',1;
 DECLARE @normalized nvarchar(80)=dbo.fn_NormalizeAreaName(@Name);
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  IF EXISTS(SELECT 1 FROM dbo.Areas WHERE CanonicalName=@normalized COLLATE Vietnamese_100_CI_AS)
   THROW 51402,N'Tên khu vực đã tồn tại.',1;
  INSERT dbo.Areas(Name,SortOrder,Notes,IsActive) VALUES(@normalized,@SortOrder,@Notes,1);
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_UpdateArea
 @ActorUserId int,@AreaId int,@Name nvarchar(max),@SortOrder int,@Notes nvarchar(max)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';
 IF @Name IS NULL OR DATALENGTH(@Name)>160 OR LEN(dbo.fn_NormalizeAreaName(@Name))=0
  THROW 51401,N'Tên khu vực phải có từ 1 đến 80 ký tự.',1;
 IF @SortOrder IS NULL OR @SortOrder<0 THROW 51403,N'Thứ tự hiển thị phải là số nguyên không âm.',1;
 IF DATALENGTH(@Notes)>1000 THROW 51405,N'Ghi chú không được vượt quá 500 ký tự.',1;
 DECLARE @normalized nvarchar(80)=dbo.fn_NormalizeAreaName(@Name);
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  IF NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE Id=@AreaId) THROW 51404,N'Không tìm thấy khu vực.',1;
  IF EXISTS(SELECT 1 FROM dbo.Areas WHERE Id<>@AreaId AND CanonicalName=@normalized COLLATE Vietnamese_100_CI_AS)
   THROW 51402,N'Tên khu vực đã tồn tại.',1;
  UPDATE dbo.Areas SET Name=@normalized,SortOrder=@SortOrder,Notes=@Notes WHERE Id=@AreaId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_DeactivateArea @ActorUserId int,@AreaId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  UPDATE dbo.Areas SET IsActive=0 WHERE Id=@AreaId;
  IF @@ROWCOUNT=0 THROW 51404,N'Không tìm thấy khu vực.',1;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_DeleteArea @ActorUserId int,@AreaId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  IF NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE Id=@AreaId) THROW 51404,N'Không tìm thấy khu vực.',1;
  IF EXISTS(SELECT 1 FROM dbo.DiningTables WHERE AreaId=@AreaId)
   THROW 51007,N'Khu vực đã có bàn nên không thể xóa. Vui lòng chuyển sang ngừng sử dụng.',1;
  IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE PreferredAreaId=@AreaId)
   THROW 51008,N'Khu vực đã có đặt bàn nên không thể xóa. Vui lòng chuyển sang ngừng sử dụng.',1;
  DELETE dbo.Areas WHERE Id=@AreaId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
ALTER TABLE dbo.Reservations ADD AreaNameSnapshot nvarchar(80) NULL;
GO
-- Older records can only be backfilled from the currently known name.
UPDATE r SET AreaNameSnapshot=a.Name FROM dbo.Reservations r JOIN dbo.Areas a ON a.Id=r.PreferredAreaId;
GO
-- Populate snapshots for inserts from seed/import as well as the booking procedure.
CREATE OR ALTER TRIGGER dbo.tr_Reservations_AreaSnapshot ON dbo.Reservations AFTER INSERT
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.Areas a WITH(UPDLOCK,HOLDLOCK) ON a.Id=i.PreferredAreaId WHERE a.IsActive=0)
  THROW 51407,N'Khu vực bạn chọn đã ngừng sử dụng.',1;
 UPDATE r SET AreaNameSnapshot=a.Name
 FROM dbo.Reservations r JOIN inserted i ON i.Id=r.Id JOIN dbo.Areas a ON a.Id=i.PreferredAreaId;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CreateReservation @CustomerName nvarchar(100),@Phone varchar(10),@GuestCount int,@StartsAt datetime2(3),
 @PreferredAreaId int=NULL,@Email nvarchar(254)=NULL,@Notes nvarchar(500)=NULL
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
  IF @StartsAt<=SYSUTCDATETIME() THROW 51003,N'Giờ đặt bàn phải ở tương lai.',1;
  DECLARE @local datetime2=DATEADD(hour,7,@StartsAt),@duration int,@ends datetime2(3),@open time(0),@close time(0),@closed bit;
  SELECT @duration=DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1;
  SET @ends=DATEADD(minute,@duration,@StartsAt);
  DECLARE @day date=CONVERT(date,@local),@weekday int=((DATEDIFF(day,CONVERT(date,'19000101'),CONVERT(date,@local))%7)+1);
  SELECT @open=OpensAt,@close=ClosesAt,@closed=IsClosed FROM dbo.OpeningHours WHERE DayOfWeek=@weekday;
  SELECT @open=OpensAt,@close=ClosesAt,@closed=IsClosed FROM dbo.SpecialDates WHERE BusinessDate=@day;
  IF @closed IS NULL OR @closed=1 OR CONVERT(time,@local)<@open
    OR CONVERT(date,DATEADD(hour,7,@ends))<>@day OR CONVERT(time,DATEADD(hour,7,@ends))>@close
    OR DATEPART(minute,@local)%30<>0 OR DATEPART(second,@local)<>0 OR DATEPART(millisecond,@local)<>0
   THROW 51004,N'Khung giờ không nằm trong giờ mở cửa hoặc không đúng bước 30 phút.',1;
  IF (SELECT COUNT(*) FROM dbo.Reservations WHERE Phone=@Phone AND Status='Pending')>=3
   THROW 51005,N'Mỗi số điện thoại chỉ được có tối đa 3 đặt bàn chờ xác nhận.',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
   WHERE t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@GuestCount AND (@PreferredAreaId IS NULL OR t.AreaId=@PreferredAreaId)
   AND NOT EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status IN ('Confirmed','Arrived')
       AND r.StartsAt<@ends AND r.EndsAt>@StartsAt))
   THROW 51006,N'Không còn bàn phù hợp trong khung giờ này.',1;
  DECLARE @code char(6);
  SET @code=UPPER(LEFT(CONVERT(varchar(64),CRYPT_GEN_RANDOM(16),2),6));
  WHILE EXISTS(SELECT 1 FROM dbo.Reservations WHERE Code=@code)
   SET @code=UPPER(LEFT(CONVERT(varchar(64),CRYPT_GEN_RANDOM(16),2),6));
  INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,StartsAt,EndsAt,Notes)
   VALUES(@code,LTRIM(RTRIM(@CustomerName)),@Phone,NULLIF(LTRIM(RTRIM(@Email)),''),@GuestCount,@PreferredAreaId,@StartsAt,@ends,@Notes);
  DECLARE @id bigint=SCOPE_IDENTITY();
  INSERT dbo.ReservationEvents(ReservationId,ToStatus) VALUES(@id,'Pending');
  EXEC dbo.usp_QueueBookingEmail @id,'BookingReceived';
  SELECT @id AS ReservationId,@code AS Code;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

