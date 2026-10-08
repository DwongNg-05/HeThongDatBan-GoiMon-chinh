-- S1-05 Task 3: nhật ký chỉ đọc tuyệt đối.
-- Các bảng nhật ký: dbo.SecurityAuditLogs (màn hình Nhật ký hệ thống), dbo.AuditLogs (nhật ký nghiệp vụ),
-- dbo.MenuPriceHistory (màn hình Nhật ký thay đổi giá). Chỉ được thêm qua stored procedure.

-- 1) Lịch sử giá trước đây chưa có trigger chỉ-thêm như hai bảng còn lại.
CREATE OR ALTER TRIGGER dbo.tr_MenuPriceHistory_AppendOnly ON dbo.MenuPriceHistory INSTEAD OF UPDATE,DELETE
AS
BEGIN
 THROW 51112,N'Lịch sử giá chỉ được thêm, không được sửa hoặc xoá.',1;
END;
GO
-- 2) TRUNCATE TABLE không kích hoạt trigger. SQL Server từ chối TRUNCATE bảng đang được khoá ngoại tham chiếu
--    (lỗi 4712), kể cả với chủ database, nên tạo một bảng chặn luôn rỗng tham chiếu tới cả ba bảng nhật ký.
CREATE TABLE dbo.AuditTruncateGuard (
 Id int NOT NULL CONSTRAINT PK_AuditTruncateGuard PRIMARY KEY,
 SecurityAuditLogId bigint NULL CONSTRAINT FK_AuditTruncateGuard_SecurityAuditLogs REFERENCES dbo.SecurityAuditLogs(Id),
 AuditLogId bigint NULL CONSTRAINT FK_AuditTruncateGuard_AuditLogs REFERENCES dbo.AuditLogs(Id),
 MenuPriceHistoryId bigint NULL CONSTRAINT FK_AuditTruncateGuard_MenuPriceHistory REFERENCES dbo.MenuPriceHistory(Id),
 -- Không bao giờ có dòng nào: bảng chỉ tồn tại để giữ các khoá ngoại.
 CONSTRAINT CK_AuditTruncateGuard_AlwaysEmpty CHECK(Id<0 AND Id>0)
);
GO
-- 3) Tài khoản ứng dụng (role restaurant_app) không được đụng trực tiếp vào bảng nhật ký:
--    ghi/đọc chỉ qua stored procedure (ownership chaining vẫn cho thủ tục dbo chạy bình thường).
--    DENY ALTER chặn cả TRUNCATE và ALTER TABLE ... DISABLE TRIGGER.
--    SELECT trên MenuPriceHistory vẫn cho phép vì màn hình Nhật ký thay đổi giá đọc trực tiếp.
DENY SELECT, INSERT, UPDATE, DELETE, ALTER ON dbo.SecurityAuditLogs TO restaurant_app;
DENY UPDATE, DELETE, ALTER ON dbo.AuditLogs TO restaurant_app;
DENY UPDATE, DELETE, ALTER ON dbo.MenuPriceHistory TO restaurant_app;
DENY INSERT, UPDATE, DELETE, ALTER ON dbo.AuditTruncateGuard TO restaurant_app;
GO
