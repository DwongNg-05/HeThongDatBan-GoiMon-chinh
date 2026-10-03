-- S2-01 Task 1: opt-in development fixture for the public menu (/ThucDon). Never load into production.
-- Repeatable: adds missing groups/dishes by name only. Existing prices, descriptions, images and
-- sale status edited by managers are never overwritten. Transaction supplied by DbTool.
SET NOCOUNT ON;

DECLARE @Nhom TABLE(
 Name nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
 SortOrder int NOT NULL,
 ImagePath nvarchar(500) NOT NULL);
INSERT @Nhom(Name,SortOrder,ImagePath) VALUES
 (N'Khai vị',1,N'/images/thuc-don/khai-vi.svg'),
 (N'Món chính',2,N'/images/thuc-don/mon-chinh.svg'),
 (N'Lẩu',3,N'/images/thuc-don/lau.svg'),
 (N'Tráng miệng',4,N'/images/thuc-don/trang-mieng.svg'),
 (N'Đồ uống',5,N'/images/thuc-don/do-uong.svg');

INSERT dbo.MenuCategories(Name,SortOrder,DefaultImagePath)
SELECT n.Name,n.SortOrder,n.ImagePath FROM @Nhom n
WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuCategories c
                 WHERE c.NormalizedName COLLATE DATABASE_DEFAULT=UPPER(LTRIM(RTRIM(n.Name))));

-- Existing groups (for example from seed-demo) only receive a default image when they have none.
UPDATE c SET DefaultImagePath=n.ImagePath
FROM dbo.MenuCategories c
JOIN @Nhom n ON c.NormalizedName COLLATE DATABASE_DEFAULT=UPPER(LTRIM(RTRIM(n.Name)))
WHERE c.DefaultImagePath IS NULL;

DECLARE @Mon TABLE(
 CategoryName nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
 Name nvarchar(150) COLLATE Vietnamese_100_CI_AI NOT NULL,
 Price decimal(18,0) NOT NULL,
 Unit nvarchar(30) NOT NULL,
 Description nvarchar(1000) NOT NULL,
 PrepMinutes int NOT NULL,
 ImagePath nvarchar(500) NOT NULL,
 SortOrder int NOT NULL,
 IsActive bit NOT NULL);
INSERT @Mon(CategoryName,Name,Price,Unit,Description,PrepMinutes,ImagePath,SortOrder,IsActive) VALUES
 (N'Khai vị',N'Gỏi cuốn tôm thịt',45000,N'Phần',N'2 cuốn tôm, thịt, bún và rau sống, chấm tương đậu phộng.',10,N'/images/thuc-don/khai-vi.svg',1,1),
 (N'Khai vị',N'Chả giò rế',55000,N'Phần',N'Chả giò vỏ rế giòn rụm, nhân thịt và khoai môn, ăn kèm rau sống.',15,N'/images/thuc-don/khai-vi.svg',2,1),
 (N'Khai vị',N'Gỏi ngó sen tôm thịt',75000,N'Đĩa',N'Ngó sen giòn trộn tôm, thịt ba chỉ, rau thơm và đậu phộng rang.',12,N'/images/thuc-don/khai-vi.svg',3,1),
 (N'Món chính',N'Cơm chiên hải sản',85000,N'Đĩa',N'Cơm chiên tôm mực, trứng và rau củ, đảo lửa lớn.',15,N'/images/thuc-don/mon-chinh.svg',1,1),
 (N'Món chính',N'Bò lúc lắc',145000,N'Đĩa',N'Thăn bò áp chảo với ớt chuông, hành tây; kèm salad và cơm trắng.',20,N'/images/thuc-don/mon-chinh.svg',2,1),
 (N'Món chính',N'Cá kho tộ',95000,N'Nồi',N'Cá basa kho tộ đậm vị nước màu, tiêu xanh; ăn kèm cơm trắng.',25,N'/images/thuc-don/mon-chinh.svg',3,1),
 -- S2-01 Task 2: món dùng để demo tìm không dấu ("com rang" → "Cơm rang dưa bò").
 (N'Món chính',N'Cơm rang dưa bò',75000,N'Đĩa',N'Cơm rang giòn hạt với dưa cải chua và thịt bò xào tỏi.',15,N'/images/thuc-don/mon-chinh.svg',5,1),
 -- Ngừng bán: dùng để kiểm tra món ngừng bán không xuất hiện trên thực đơn công khai.
 (N'Món chính',N'Cua rang me',320000,N'Phần',N'Cua thịt rang sốt me chua ngọt. Món theo mùa, hiện ngừng bán.',30,N'/images/thuc-don/mon-chinh.svg',4,0),
 (N'Lẩu',N'Lẩu Thái hải sản',280000,N'Nồi',N'Nước lẩu chua cay, tôm, mực, nghêu, nấm và rau ăn kèm; cho 2–3 người.',25,N'/images/thuc-don/lau.svg',1,1),
 (N'Lẩu',N'Lẩu gà lá é',260000,N'Nồi',N'Gà ta, lá é thơm, măng chua và bún tươi; cho 2–3 người.',25,N'/images/thuc-don/lau.svg',2,1),
 (N'Lẩu',N'Lẩu nấm chay',190000,N'Nồi',N'Nước dùng rau củ ngọt thanh với 6 loại nấm, đậu hũ non.',20,N'/images/thuc-don/lau.svg',3,1),
 (N'Tráng miệng',N'Chè hạt sen long nhãn',30000,N'Chén',N'Hạt sen bùi, long nhãn ngọt dịu, nước đường phèn thanh mát.',5,N'/images/thuc-don/trang-mieng.svg',1,1),
 (N'Tráng miệng',N'Bánh flan',25000,N'Phần',N'Bánh flan trứng sữa mềm mịn, phủ caramel và cà phê.',5,N'/images/thuc-don/trang-mieng.svg',2,1),
 (N'Tráng miệng',N'Rau câu dừa',25000,N'Phần',N'Rau câu hai lớp nước cốt dừa và nước dừa tươi.',5,N'/images/thuc-don/trang-mieng.svg',3,1),
 (N'Đồ uống',N'Trà đào cam sả',35000,N'Ly',N'Trà đen ủ lạnh với đào miếng, cam tươi và sả.',5,N'/images/thuc-don/do-uong.svg',1,1),
 (N'Đồ uống',N'Cà phê sữa đá',29000,N'Ly',N'Cà phê phin pha sữa đặc, uống cùng đá.',5,N'/images/thuc-don/do-uong.svg',2,1),
 (N'Đồ uống',N'Nước ép cam',40000,N'Ly',N'Cam vắt tươi, không thêm đường.',5,N'/images/thuc-don/do-uong.svg',3,1);

INSERT dbo.MenuItems(CategoryId,Name,Price,Unit,Description,EstimatedPrepMinutes,ImagePath,SortOrder,IsActive)
SELECT c.Id,m.Name,m.Price,m.Unit,m.Description,m.PrepMinutes,m.ImagePath,m.SortOrder,m.IsActive
FROM @Mon m
JOIN dbo.MenuCategories c ON c.NormalizedName COLLATE DATABASE_DEFAULT=UPPER(LTRIM(RTRIM(m.CategoryName)))
WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuItems i WHERE i.CategoryId=c.Id AND i.Name=m.Name);
