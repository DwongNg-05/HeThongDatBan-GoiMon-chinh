CREATE OR ALTER PROCEDURE dbo.usp_FindLogin @Identifier nvarchar(50)
AS
BEGIN
 SET NOCOUNT ON;
 SELECT u.Id,u.UserName,u.FullName,u.PasswordHash,u.IsActive,r.Code AS Role
 FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId
 WHERE u.NormalizedUserName=UPPER(LTRIM(RTRIM(@Identifier))) OR u.Phone=@Identifier;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ManagementMenu @ActorUserId int
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Catalog.Manage';
 SELECT Id,Name,Price,IsSoldOut FROM dbo.MenuItems ORDER BY SortOrder,Id;
 SELECT TOP(20) h.ChangedAt,u.UserName,m.Name,h.ChangeDescription
 FROM (
  SELECT ChangedAt,ChangedBy,MenuItemId,CONCAT(N'Đổi giá: ',OldPrice,N' → ',NewPrice,N' đ') AS ChangeDescription FROM dbo.MenuPriceHistory
  UNION ALL
  SELECT ChangedAt,ChangedBy,MenuItemId,CASE WHEN IsSoldOut=1 THEN N'Đánh dấu tạm hết' ELSE N'Đánh dấu còn món' END FROM dbo.MenuAvailabilityEvents
 ) h JOIN dbo.Users u ON u.Id=h.ChangedBy JOIN dbo.MenuItems m ON m.Id=h.MenuItemId
 ORDER BY h.ChangedAt DESC;
END;
GO
GRANT EXECUTE ON dbo.usp_FindLogin TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ManagementMenu TO restaurant_app;
GO
