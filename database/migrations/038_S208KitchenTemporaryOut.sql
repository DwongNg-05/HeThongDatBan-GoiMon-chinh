-- S2-08 Task 1: Bếp và Quản lý bật/tắt "Tạm hết" cho món ngay trên danh sách món trong ngày (/Kitchen/Dishes).
-- Chốt với PO: quyền mới Menu.TemporarilyOut (chỉ Quản lý, Bếp). Quyền Menu.Availability ("Báo hết" trong
-- Quản lý món, S2-01 Task 3) giữ nguyên chỉ Quản lý (migration 031, 032) — Bếp chỉ bật/tắt được "Tạm hết".
-- Chạy lại vẫn an toàn.
IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE Code='Menu.TemporarilyOut')
 INSERT dbo.Permissions(Code,Description) VALUES('Menu.TemporarilyOut',N'Bật/tắt món tạm hết trong danh sách món trong ngày');
INSERT dbo.RolePermissions(RoleId,PermissionId)
SELECT r.Id,p.Id FROM dbo.Roles r CROSS JOIN dbo.Permissions p
WHERE r.Code IN ('Manager','Kitchen') AND p.Code='Menu.TemporarilyOut'
  AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
GO

-- Bật/tắt tạm hết (migration 006) nay kiểm tra quyền Menu.TemporarilyOut và trả về tên món + trạng thái sau khi đổi.
-- Chỉ món đang bán thuộc nhóm đang dùng mới bật/tắt được (51040 = không tìm thấy).
CREATE OR ALTER PROCEDURE dbo.usp_SetMenuTemporarilyOut
    @MenuItemId int,
    @IsTemporarilyOut bit,
    @ActorUserId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId,'Menu.TemporarilyOut';

        IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems m JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
                       WHERE m.Id=@MenuItemId AND m.IsActive=1 AND c.IsActive=1)
            THROW 51040,N'Món không tồn tại hoặc đã ngừng bán.',1;

        DECLARE @old bit = (SELECT IsTemporarilyOut FROM dbo.MenuItems WITH (UPDLOCK) WHERE Id=@MenuItemId);
        IF @old<>@IsTemporarilyOut
        BEGIN
            UPDATE dbo.MenuItems
            SET IsTemporarilyOut=@IsTemporarilyOut,UpdatedAt=SYSUTCDATETIME()
            WHERE Id=@MenuItemId;
            INSERT dbo.MenuTemporaryOutEvents(MenuItemId,OldIsTemporarilyOut,IsTemporarilyOut,ChangedBy)
            VALUES(@MenuItemId,@old,@IsTemporarilyOut,@ActorUserId);
        END;
        COMMIT;
        SELECT Name,IsTemporarilyOut FROM dbo.MenuItems WHERE Id=@MenuItemId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO
GRANT EXECUTE ON dbo.usp_SetMenuTemporarilyOut TO restaurant_app;
GO
