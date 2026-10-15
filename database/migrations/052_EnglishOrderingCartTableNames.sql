-- Đồng bộ tên bảng sang tiếng Anh cho giỏ món của màn hình Gọi món (EF: Order / OrderLine, RestaurantDbContext):
--   dbo.DonHangs  -> dbo.Orders
--   dbo.DongHangs -> dbo.OrderLines
-- Đổi tên luôn khoá chính, khoá ngoại, ràng buộc CHECK/DEFAULT và chỉ mục còn mang tên cũ
-- (PK_DonHangs -> PK_Orders, FK_DongHangs_DonHangs -> FK_OrderLines_Orders, IX_DongHangs_OrderId -> IX_OrderLines_OrderId…).
-- Chỉ đổi tên: dữ liệu, cột và quyền (GRANT cho restaurant_app) giữ nguyên. Chạy lại vẫn an toàn.
-- Không sửa 041_S208OrderingCartTables.sql (migration đã áp dụng được khoá bằng checksum).

IF OBJECT_ID('dbo.DonHangs','U') IS NOT NULL AND OBJECT_ID('dbo.Orders','U') IS NOT NULL
    THROW 51400, N'Đang có cả dbo.DonHangs và dbo.Orders. Hãy kiểm tra, gộp dữ liệu bằng tay rồi chạy lại migrate.', 1;
IF OBJECT_ID('dbo.DongHangs','U') IS NOT NULL AND OBJECT_ID('dbo.OrderLines','U') IS NOT NULL
    THROW 51400, N'Đang có cả dbo.DongHangs và dbo.OrderLines. Hãy kiểm tra, gộp dữ liệu bằng tay rồi chạy lại migrate.', 1;
GO

-- 1) Đổi tên bảng.
IF OBJECT_ID('dbo.DongHangs','U') IS NOT NULL
    EXEC sys.sp_rename @objname = N'dbo.DongHangs', @newname = N'OrderLines', @objtype = N'OBJECT';
IF OBJECT_ID('dbo.DonHangs','U') IS NOT NULL
    EXEC sys.sp_rename @objname = N'dbo.DonHangs', @newname = N'Orders', @objtype = N'OBJECT';
GO

-- 2) Đổi tên ràng buộc (PK, FK, CHECK, DEFAULT, UNIQUE) còn chứa tên cũ.
DECLARE @oldName sysname, @newName sysname, @qualified nvarchar(400);
DECLARE constraint_names CURSOR LOCAL STATIC READ_ONLY FORWARD_ONLY FOR
    SELECT o.name,
           REPLACE(REPLACE(o.name, N'DongHangs', N'OrderLines'), N'DonHangs', N'Orders'),
           QUOTENAME(SCHEMA_NAME(o.schema_id)) + N'.' + QUOTENAME(o.name)
    FROM sys.objects AS o
    WHERE o.parent_object_id IN (OBJECT_ID(N'dbo.Orders'), OBJECT_ID(N'dbo.OrderLines'))
      AND o.type IN ('PK', 'F', 'C', 'D', 'UQ')
      AND (o.name LIKE N'%DonHangs%' OR o.name LIKE N'%DongHangs%');
OPEN constraint_names;
FETCH NEXT FROM constraint_names INTO @oldName, @newName, @qualified;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + QUOTENAME(@newName)) IS NULL
        EXEC sys.sp_rename @objname = @qualified, @newname = @newName, @objtype = N'OBJECT';
    FETCH NEXT FROM constraint_names INTO @oldName, @newName, @qualified;
END;
CLOSE constraint_names;
DEALLOCATE constraint_names;
GO

-- 3) Đổi tên chỉ mục thường (chỉ mục của PK/UNIQUE đã đổi theo ràng buộc ở bước 2).
DECLARE @indexName sysname, @newIndexName sysname, @tableName sysname, @qualifiedIndex nvarchar(400);
DECLARE index_names CURSOR LOCAL STATIC READ_ONLY FORWARD_ONLY FOR
    SELECT i.name,
           REPLACE(REPLACE(i.name, N'DongHangs', N'OrderLines'), N'DonHangs', N'Orders'),
           OBJECT_NAME(i.object_id)
    FROM sys.indexes AS i
    WHERE i.object_id IN (OBJECT_ID(N'dbo.Orders'), OBJECT_ID(N'dbo.OrderLines'))
      AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
      AND i.name IS NOT NULL
      AND (i.name LIKE N'%DonHangs%' OR i.name LIKE N'%DongHangs%');
OPEN index_names;
FETCH NEXT FROM index_names INTO @indexName, @newIndexName, @tableName;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@tableName)) AND name = @newIndexName)
    BEGIN
        SET @qualifiedIndex = N'dbo.' + QUOTENAME(@tableName) + N'.' + QUOTENAME(@indexName);
        EXEC sys.sp_rename @objname = @qualifiedIndex, @newname = @newIndexName, @objtype = N'INDEX';
    END;
    FETCH NEXT FROM index_names INTO @indexName, @newIndexName, @tableName;
END;
CLOSE index_names;
DEALLOCATE index_names;
GO

-- 4) Database chưa từng có hai bảng (ví dụ ai đó đã xoá tay): tạo mới đúng cấu trúc EF mong đợi, tên tiếng Anh.
IF OBJECT_ID('dbo.Orders','U') IS NULL
    CREATE TABLE dbo.Orders (
        Id int IDENTITY CONSTRAINT PK_Orders PRIMARY KEY,
        CreatedAt datetime2 NOT NULL,
        IsOpen bit NOT NULL
    );
GO
IF OBJECT_ID('dbo.OrderLines','U') IS NULL
    CREATE TABLE dbo.OrderLines (
        Id int IDENTITY CONSTRAINT PK_OrderLines PRIMARY KEY,
        OrderId int NOT NULL CONSTRAINT FK_OrderLines_Orders REFERENCES dbo.Orders(Id),
        DishNameSnapshot nvarchar(max) NOT NULL,
        UnitPriceVnd int NOT NULL,
        Unit nvarchar(max) NOT NULL,
        Quantity int NOT NULL CONSTRAINT CK_OrderLines_Quantity CHECK (Quantity BETWEEN 1 AND 1000)
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.OrderLines') AND name = N'IX_OrderLines_OrderId')
    CREATE INDEX IX_OrderLines_OrderId ON dbo.OrderLines(OrderId);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.Orders TO restaurant_app;
GRANT SELECT, INSERT, UPDATE ON dbo.OrderLines TO restaurant_app;
GO
