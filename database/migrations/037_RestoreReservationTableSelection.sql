-- Restore the table-selection signature after integrating older reservation migrations.
CREATE OR ALTER PROCEDURE dbo.usp_CreateReservation @CustomerName nvarchar(100),@Phone varchar(10),@GuestCount int,@StartsAt datetime2(3),
 @PreferredAreaId int=NULL,@Email nvarchar(254)=NULL,@Notes nvarchar(500)=NULL,@TableId int=NULL
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
  EXEC dbo.usp_ValidateBookingSchedule @StartsAt;
  DECLARE @duration int=(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1);
  DECLARE @ends datetime2(3)=DATEADD(minute,@duration,@StartsAt);
  IF (SELECT COUNT(*) FROM dbo.Reservations WHERE Phone=@Phone AND Status='Pending')>=3
   THROW 51005,N'Mỗi số điện thoại chỉ được có tối đa 3 đặt bàn chờ xác nhận.',1;

  DECLARE @tableCode varchar(20)=NULL;
  IF @TableId IS NOT NULL
  BEGIN
   DECLARE @tableArea int;
   SELECT @tableCode=t.Code,@tableArea=t.AreaId FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
    WHERE t.Id=@TableId AND t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=@GuestCount;
   IF @tableCode IS NULL OR (@PreferredAreaId IS NOT NULL AND @tableArea<>@PreferredAreaId)
    THROW 51413,N'Bàn bạn chọn không còn sử dụng, không đủ chỗ cho số khách hoặc không thuộc khu vực đã chọn.',1;
   -- Đang giữ khoá usp_LockOperations nên hai khách không thể cùng giữ một bàn trong khung giờ giao nhau.
   IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE TableId=@TableId AND Status IN ('Pending','Confirmed','Arrived')
     AND StartsAt<@ends AND EndsAt>@StartsAt)
    THROW 51414,N'Bàn bạn chọn vừa có khách khác đặt trong khung giờ này. Vui lòng chọn bàn khác.',1;
   SET @PreferredAreaId=COALESCE(@PreferredAreaId,@tableArea);
  END;

  DECLARE @code char(6);
  SET @code=UPPER(LEFT(CONVERT(varchar(64),CRYPT_GEN_RANDOM(16),2),6));
  WHILE EXISTS(SELECT 1 FROM dbo.Reservations WHERE Code=@code)
   SET @code=UPPER(LEFT(CONVERT(varchar(64),CRYPT_GEN_RANDOM(16),2),6));
  INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,TableId,StartsAt,EndsAt,Notes)
   VALUES(@code,LTRIM(RTRIM(@CustomerName)),@Phone,NULLIF(LTRIM(RTRIM(@Email)),''),@GuestCount,@PreferredAreaId,@TableId,@StartsAt,@ends,@Notes);
  DECLARE @id bigint=SCOPE_IDENTITY();
  INSERT dbo.ReservationEvents(ReservationId,ToStatus,NewTableId) VALUES(@id,'Pending',@TableId);
  EXEC dbo.usp_QueueBookingEmail @id,'BookingReceived';
  SELECT @id AS ReservationId,@code AS Code,@tableCode AS TableCode;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
