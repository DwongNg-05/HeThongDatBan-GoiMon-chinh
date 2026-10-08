-- S1-05 Task 1: nhật ký bảo mật lưu trong kho riêng, tách khỏi dbo.AuditLogs nghiệp vụ.
-- Ghi nhận đăng nhập thành công, đăng nhập thất bại và sửa giá món kèm thời điểm (UTC), tài khoản, vai trò, IP.
CREATE TABLE dbo.SecurityAuditLogs (
 Id bigint IDENTITY PRIMARY KEY,
 OccurredAt datetime2(3) NOT NULL CONSTRAINT DF_SecurityAuditLogs_OccurredAt DEFAULT SYSUTCDATETIME(),
 UserId int NULL REFERENCES dbo.Users(Id),
 UserName nvarchar(50) NOT NULL,
 RoleCode varchar(20) NULL,
 Action varchar(30) NOT NULL CONSTRAINT CK_SecurityAuditLogs_Action CHECK(Action IN ('LoginSucceeded','LoginFailed','PriceChanged')),
 Detail nvarchar(300) NULL,
 IpAddress varchar(45) NOT NULL,
 CONSTRAINT CK_SecurityAuditLogs_UserName CHECK(LEN(UserName)>0)
);
CREATE INDEX IX_SecurityAuditLogs_Newest ON dbo.SecurityAuditLogs(OccurredAt DESC,Id DESC)
 INCLUDE(UserName,RoleCode,Action,IpAddress);
GO
CREATE OR ALTER TRIGGER dbo.tr_SecurityAuditLogs_AppendOnly ON dbo.SecurityAuditLogs INSTEAD OF UPDATE,DELETE
AS
BEGIN
 THROW 51110,N'Nhật ký bảo mật chỉ được thêm, không được sửa hoặc xoá.',1;
END;
GO
-- Tài khoản và vai trò của tài khoản có thật được lấy từ dbo.Users/dbo.Roles, không tin giá trị từ ứng dụng.
-- @UserName chỉ dùng khi định danh không khớp tài khoản nào (ứng dụng truyền dạng đã che bớt).
CREATE OR ALTER PROCEDURE dbo.usp_WriteLoginAudit
 @UserId int=NULL, @UserName nvarchar(50), @Succeeded bit, @IpAddress varchar(45)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.SecurityAuditLogs(UserId,UserName,RoleCode,Action,IpAddress)
 SELECT u.Id,
  COALESCE(u.UserName,NULLIF(LTRIM(RTRIM(@UserName)),N''),N'(trống)'),
  r.Code,
  CASE WHEN @Succeeded=1 THEN 'LoginSucceeded' ELSE 'LoginFailed' END,
  COALESCE(NULLIF(LTRIM(RTRIM(@IpAddress)),''),'unknown')
 FROM (SELECT 1 AS One) d
 LEFT JOIN dbo.Users u ON u.Id=@UserId
 LEFT JOIN dbo.Roles r ON r.Id=u.RoleId;
END;
GO
-- Giữ nguyên nghiệp vụ của 002_BusinessOperations; bổ sung @IpAddress (tuỳ chọn) và ghi nhật ký bảo mật
-- trong cùng giao dịch khi giá thực sự thay đổi.
CREATE OR ALTER PROCEDURE dbo.usp_UpdateMenuPrice @MenuItemId int,@Price decimal(18,0),@ActorUserId int,@IpAddress varchar(45)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';

  DECLARE @old decimal(18,0),@name nvarchar(150);
  SELECT @old=Price,@name=Name FROM dbo.MenuItems WHERE Id=@MenuItemId;
  IF @old IS NULL THROW 51039,N'Món không tồn tại.',1;
  UPDATE dbo.MenuItems SET Price=@Price,UpdatedAt=SYSUTCDATETIME() WHERE Id=@MenuItemId;
  INSERT dbo.MenuPriceHistory(MenuItemId,OldPrice,NewPrice,ChangedBy) VALUES(@MenuItemId,@old,@Price,@ActorUserId);
  INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,OldValues,NewValues)
   VALUES(@ActorUserId,'PriceChanged','MenuItem',@MenuItemId,CONCAT('{"price":',@old,'}'),CONCAT('{"price":',@Price,'}'));
  IF @old<>@Price
   INSERT dbo.SecurityAuditLogs(UserId,UserName,RoleCode,Action,Detail,IpAddress)
   SELECT u.Id,u.UserName,r.Code,'PriceChanged',
    LEFT(CONCAT(@name,N' (#',@MenuItemId,N'): ',
     REPLACE(REPLACE(CONVERT(varchar(30),CAST(@old AS money),1),'.00',''),',','.'),N' ₫ → ',
     REPLACE(REPLACE(CONVERT(varchar(30),CAST(@Price AS money),1),'.00',''),',','.'),N' ₫'),300),
    COALESCE(NULLIF(LTRIM(RTRIM(@IpAddress)),''),'unknown')
   FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE u.Id=@ActorUserId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO
-- Danh sách nhật ký cho màn hình quản lý: mới nhất lên đầu. Kiểm tra quyền Audit.Read ở cả database.
CREATE OR ALTER PROCEDURE dbo.usp_SecurityAuditList @ActorUserId int,@Top int=200
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Audit.Read';
 SELECT TOP(CASE WHEN @Top BETWEEN 1 AND 1000 THEN @Top ELSE 200 END)
  l.Id,l.OccurredAt,l.UserName,l.RoleCode,r.Name AS RoleName,l.Action,l.Detail,l.IpAddress
 FROM dbo.SecurityAuditLogs l
 LEFT JOIN dbo.Roles r ON r.Code=l.RoleCode
 ORDER BY l.OccurredAt DESC,l.Id DESC;
END;
GO
GRANT EXECUTE ON dbo.usp_WriteLoginAudit TO restaurant_app;
GRANT EXECUTE ON dbo.usp_UpdateMenuPrice TO restaurant_app;
GRANT EXECUTE ON dbo.usp_SecurityAuditList TO restaurant_app;
GO
