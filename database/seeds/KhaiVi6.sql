-- Thêm 6 món vào nhóm "Khai vị" (lệnh DbTool: seed-khai-vi). Trạng thái mặc định: đang bán.
-- Chạy lại an toàn: món đã có cùng tên trong nhóm thì giữ nguyên (không ghi đè giá, mô tả, trạng thái do quản lý sửa).
-- Có thể chạy trực tiếp trong SSMS trên database của web, hoặc qua DbTool (có transaction).
SET NOCOUNT ON;

IF NOT EXISTS(SELECT 1 FROM dbo.MenuCategories WHERE NormalizedName = N'KHAI VỊ')
    INSERT dbo.MenuCategories(Name, SortOrder, DefaultImagePath) VALUES(N'Khai vị', 1, N'/images/thuc-don/khai-vi.svg');

DECLARE @CategoryId int = (SELECT TOP(1) Id FROM dbo.MenuCategories WHERE NormalizedName = N'KHAI VỊ' ORDER BY Id);
DECLARE @NextSort int = (SELECT COALESCE(MAX(SortOrder), 0) FROM dbo.MenuItems WHERE CategoryId = @CategoryId);

DECLARE @Mon TABLE(
 No int NOT NULL,
 Name nvarchar(150) COLLATE Vietnamese_100_CI_AI NOT NULL,
 Price decimal(18,0) NOT NULL,
 Unit nvarchar(30) NOT NULL,
 Description nvarchar(1000) NOT NULL,
 PrepMinutes int NOT NULL);
INSERT @Mon(No, Name, Price, Unit, Description, PrepMinutes) VALUES
 (1, N'Khoai tây chiên',       35000, N'Phần', N'Khoai tây cắt thanh chiên vàng giòn, rắc muối, kèm tương cà.', 10),
 (2, N'Ngô chiên',             39000, N'Đĩa',  N'Hạt ngô ngọt chiên giòn, đảo bơ và hành lá.', 10),
 (3, N'Phô mai que',           49000, N'Phần', N'6 que phô mai mozzarella tẩm bột chiên giòn, chấm tương ớt.', 10),
 (4, N'Gỏi tai heo',           69000, N'Đĩa',  N'Tai heo thái mỏng trộn hành tây, rau răm, thính và nước mắm chua ngọt.', 12),
 (5, N'Mực chiên giòn',        99000, N'Đĩa',  N'Mực ống tẩm bột chiên giòn, chấm tương ớt hoặc sốt mayonnaise.', 15),
 (6, N'Hành tây chiên giòn',   39000, N'Phần', N'Hành tây khoanh tẩm bột xù chiên vàng, chấm sốt mayonnaise.', 10);

INSERT dbo.MenuItems(CategoryId, Name, Price, Unit, Description, EstimatedPrepMinutes, ImagePath, SortOrder, IsActive, IsSoldOut)
SELECT @CategoryId, m.Name, m.Price, m.Unit, m.Description, m.PrepMinutes, N'/images/thuc-don/khai-vi.svg', @NextSort + m.No, 1, 0
FROM @Mon m
WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuItems i WHERE i.CategoryId = @CategoryId AND i.Name = m.Name)
ORDER BY m.No;

SELECT i.Id, i.Name, i.Price, i.Unit, i.EstimatedPrepMinutes, i.IsActive
FROM dbo.MenuItems i JOIN @Mon m ON m.Name = i.Name
WHERE i.CategoryId = @CategoryId ORDER BY i.SortOrder;
