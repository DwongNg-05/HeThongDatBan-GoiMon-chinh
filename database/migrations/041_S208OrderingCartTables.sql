-- S2-08 Task 4: màn hình Gọi món (/Ordering) lưu giỏ món đã gọi vào dbo.DonHangs / dbo.DongHangs (EF: Order, OrderLine,
-- RestaurantDbContext) nhưng chưa migration SQL nào tạo hai bảng này, nên trên SQL Server "Gọi món" lỗi và không kiểm được
-- "món còn hàng nhận order trở lại". Tạo đúng cấu trúc EF mong đợi. Chạy lại vẫn an toàn.
IF OBJECT_ID('dbo.DonHangs','U') IS NULL
    CREATE TABLE dbo.DonHangs (
        Id int IDENTITY CONSTRAINT PK_DonHangs PRIMARY KEY,
        CreatedAt datetime2 NOT NULL,
        IsOpen bit NOT NULL
    );
GO
IF OBJECT_ID('dbo.DongHangs','U') IS NULL
    CREATE TABLE dbo.DongHangs (
        Id int IDENTITY CONSTRAINT PK_DongHangs PRIMARY KEY,
        OrderId int NOT NULL CONSTRAINT FK_DongHangs_DonHangs REFERENCES dbo.DonHangs(Id),
        DishNameSnapshot nvarchar(max) NOT NULL,
        UnitPriceVnd int NOT NULL,
        Unit nvarchar(max) NOT NULL,
        Quantity int NOT NULL CONSTRAINT CK_DongHangs_Quantity CHECK (Quantity BETWEEN 1 AND 1000)
    );
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_DongHangs_OrderId')
    CREATE INDEX IX_DongHangs_OrderId ON dbo.DongHangs(OrderId);
GO
GRANT SELECT, INSERT, UPDATE ON dbo.DonHangs TO restaurant_app;
GRANT SELECT, INSERT, UPDATE ON dbo.DongHangs TO restaurant_app;
GO
