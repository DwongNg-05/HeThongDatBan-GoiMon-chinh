-- Opt-in development fixture. Parameters and transaction supplied by DbTool.
-- Never reset an existing account or overwrite a menu change during a rerun.
DECLARE @role int=(SELECT Id FROM dbo.Roles WHERE Code='Manager');
IF @role IS NULL THROW 51201,N'Run migrations before loading the login demo.',1;
IF EXISTS(SELECT 1 FROM dbo.Users WHERE NormalizedUserName=UPPER(@UserName))
BEGIN
 IF NOT EXISTS(SELECT 1 FROM dbo.Users WHERE NormalizedUserName=UPPER(@UserName)
               AND Phone=@Phone AND RoleId=@role AND IsActive=1 AND PasswordHash IS NOT NULL)
  THROW 51202,N'Existing account does not match the requested active demo manager. No changes made.',1;
END
ELSE
BEGIN
 IF EXISTS(SELECT 1 FROM dbo.Users WHERE Phone=@Phone OR NormalizedUserName=UPPER(@Phone) OR Phone=@UserName)
  THROW 51203,N'Demo identifiers conflict with an existing account. Choose different identifiers.',1;
 INSERT dbo.Users(RoleId,FullName,UserName,Phone,PasswordHash,MustChangePassword,IsActive,PasswordChangedAt)
 VALUES(@role,N'Quản lý demo',@UserName,@Phone,@Hash,0,1,SYSUTCDATETIME());
END;
DECLARE @category int=(SELECT Id FROM dbo.MenuCategories WHERE NormalizedName=UPPER(N'Thực đơn demo đăng nhập'));
IF @category IS NULL
BEGIN
 INSERT dbo.MenuCategories(Name,SortOrder) VALUES(N'Thực đơn demo đăng nhập',100);
 SET @category=CONVERT(int,SCOPE_IDENTITY());
END;
INSERT dbo.MenuItems(CategoryId,Name,Price,Unit,Description,EstimatedPrepMinutes,SortOrder)
SELECT @category,d.Name,d.Price,N'Phần',N'Dữ liệu demo đăng nhập và truy vết',10,d.SortOrder
FROM (VALUES(N'Cơm chiên demo',45000,1),(N'Gỏi cuốn demo',35000,2),(N'Trà đào demo',25000,3)) d(Name,Price,SortOrder)
WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuItems m WHERE m.CategoryId=@category AND m.Name=d.Name);
