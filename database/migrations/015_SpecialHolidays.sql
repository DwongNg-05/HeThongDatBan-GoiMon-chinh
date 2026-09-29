-- Management calendar only; legacy SpecialDates belongs to reservation enforcement.
CREATE TABLE dbo.SpecialHolidays (
 Id int IDENTITY PRIMARY KEY,
 HolidayDate date NOT NULL CONSTRAINT UQ_SpecialHolidays_Date UNIQUE,
 Name nvarchar(150) NOT NULL CHECK(LEN(LTRIM(RTRIM(Name)))>0),
 IsActive bit NOT NULL DEFAULT 1
);
GO
CREATE OR ALTER PROCEDURE dbo.usp_SaveSpecialHoliday
 @ActorUserId int, @Id int=NULL, @HolidayDate date, @Name nvarchar(max), @IsActive bit
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId, 'Catalog.Manage';
 IF @HolidayDate IS NULL OR @Name IS NULL OR LEN(LTRIM(RTRIM(@Name)))=0 OR DATALENGTH(@Name)>300 OR @IsActive IS NULL
  THROW 51511,N'Vui lòng nhập ngày nghỉ và tên ngày nghỉ từ 1 đến 150 ký tự.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  IF @Id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.SpecialHolidays WHERE Id=@Id)
   THROW 51514,N'Không tìm thấy ngày nghỉ.',1;
  IF EXISTS(SELECT 1 FROM dbo.SpecialHolidays WHERE HolidayDate=@HolidayDate AND (@Id IS NULL OR Id<>@Id))
   THROW 51512,N'Ngày nghỉ này đã tồn tại.',1;
  IF @Id IS NULL
   INSERT dbo.SpecialHolidays(HolidayDate,Name,IsActive) VALUES(@HolidayDate,LTRIM(RTRIM(@Name)),@IsActive);
  ELSE
   UPDATE dbo.SpecialHolidays SET HolidayDate=@HolidayDate,Name=LTRIM(RTRIM(@Name)),IsActive=@IsActive WHERE Id=@Id;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_DeleteSpecialHoliday @ActorUserId int, @Id int
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId, 'Catalog.Manage';
 DELETE dbo.SpecialHolidays WHERE Id=@Id;
 IF @@ROWCOUNT=0 THROW 51514,N'Không tìm thấy ngày nghỉ.',1;
END;
