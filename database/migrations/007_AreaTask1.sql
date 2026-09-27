-- Task1: ignore case, preserve whitespace and Vietnamese accents.
DROP INDEX UX_Areas_Name ON dbo.Areas;
ALTER TABLE dbo.Areas ADD SimpleName AS (Name COLLATE Vietnamese_100_CI_AS) PERSISTED,
 NameBytes AS DATALENGTH(Name) PERSISTED;
CREATE UNIQUE INDEX UX_Areas_Name ON dbo.Areas(SimpleName, NameBytes);
GO
CREATE OR ALTER PROCEDURE dbo.usp_ListAreas
AS
BEGIN
 SET NOCOUNT ON;
 SELECT Id, Name, SortOrder, Notes, IsActive FROM dbo.Areas
 WHERE IsActive=1 ORDER BY SortOrder, Name, Id;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_CreateArea
 @ActorUserId int, @Name nvarchar(max), @SortOrder int, @Notes nvarchar(500)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId, 'Catalog.Manage';
 IF @Name IS NULL OR LEN(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@Name,NCHAR(9),N' '),NCHAR(10),N' '),NCHAR(13),N' '))))=0 OR DATALENGTH(@Name)>160
  THROW 51401,N'Tên khu vực phải có từ 1 đến 80 ký tự.',1;
 IF @SortOrder IS NULL OR @SortOrder<0
  THROW 51403,N'Thứ tự hiển thị phải là số nguyên không âm.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  IF EXISTS(SELECT 1 FROM dbo.Areas WHERE SimpleName=@Name COLLATE Vietnamese_100_CI_AS AND NameBytes=DATALENGTH(@Name))
   THROW 51402,N'Tên khu vực đã tồn tại.',1;
  INSERT dbo.Areas(Name,SortOrder,IsActive) VALUES(@Name,@SortOrder,1);
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END;
GO
