-- S2-08 Task 2: nhật ký bật/tắt "Tạm hết" (dbo.MenuTemporaryOutEvents, migration 006) chỉ được ghi thêm, không sửa/xoá.
-- Mỗi dòng: người thực hiện (ChangedBy), món (MenuItemId), trạng thái trước/sau, thời điểm (ChangedAt, UTC).
-- Dòng chỉ được ghi khi trạng thái thật sự đổi. Chạy lại vẫn an toàn.
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_MenuTemporaryOutEvents_Changed')
   AND NOT EXISTS(SELECT 1 FROM dbo.MenuTemporaryOutEvents WHERE OldIsTemporarilyOut=IsTemporarilyOut)
    ALTER TABLE dbo.MenuTemporaryOutEvents WITH CHECK
        ADD CONSTRAINT CK_MenuTemporaryOutEvents_Changed CHECK (OldIsTemporarilyOut IS NULL OR OldIsTemporarilyOut<>IsTemporarilyOut);
GO
-- Tài khoản ứng dụng: được đọc và ghi thêm nhật ký, không được sửa hay xoá.
GRANT SELECT, INSERT ON dbo.MenuTemporaryOutEvents TO restaurant_app;
DENY UPDATE, DELETE ON dbo.MenuTemporaryOutEvents TO restaurant_app;
GO
