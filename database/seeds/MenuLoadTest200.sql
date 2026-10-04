-- S2-01 Task 4: dữ liệu kiểm thử hiệu năng — 200 món thuộc 8 nhóm món (chỉ dùng cho môi trường phát triển/kiểm thử).
-- Tên món có hậu tố " (mẫu 001)" … " (mẫu 200)" để nhận biết và ẩn lại bằng lệnh hide-menu-200.
-- Chạy lại an toàn: món đã có thì chỉ được mở bán lại (IsActive=1), không nhân bản. Transaction do DbTool cung cấp.
-- Có chủ đích: mỗi 10 món có 1 món "hết trong ngày" và mỗi nhóm có 1 món tên/mô tả dài để kiểm tra bố cục 360px.
SET NOCOUNT ON;

DECLARE @Nhom TABLE(
 No int NOT NULL PRIMARY KEY,
 Name nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
 SortOrder int NOT NULL,
 ImagePath nvarchar(500) NOT NULL,
 Unit nvarchar(30) NOT NULL);
INSERT @Nhom(No,Name,SortOrder,ImagePath,Unit) VALUES
 (1,N'Khai vị',1,N'/images/thuc-don/khai-vi.svg',N'Phần'),
 (2,N'Món chính',2,N'/images/thuc-don/mon-chinh.svg',N'Đĩa'),
 (3,N'Lẩu',3,N'/images/thuc-don/lau.svg',N'Nồi'),
 (4,N'Tráng miệng',4,N'/images/thuc-don/trang-mieng.svg',N'Chén'),
 (5,N'Đồ uống',5,N'/images/thuc-don/do-uong.svg',N'Ly'),
 (6,N'Món nướng',6,N'/images/thuc-don/mon-chinh.svg',N'Phần'),
 (7,N'Hải sản',7,N'/images/thuc-don/lau.svg',N'Đĩa'),
 (8,N'Món chay',8,N'/images/thuc-don/khai-vi.svg',N'Phần');

INSERT dbo.MenuCategories(Name,SortOrder,DefaultImagePath)
SELECT n.Name,n.SortOrder,n.ImagePath FROM @Nhom n
WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuCategories c WHERE c.NormalizedName COLLATE DATABASE_DEFAULT=UPPER(LTRIM(RTRIM(n.Name))));
UPDATE c SET IsActive=1,DefaultImagePath=COALESCE(c.DefaultImagePath,n.ImagePath)
FROM dbo.MenuCategories c JOIN @Nhom n ON c.NormalizedName COLLATE DATABASE_DEFAULT=UPPER(LTRIM(RTRIM(n.Name)));

DECLARE @Ten TABLE(GroupNo int NOT NULL, Slot int NOT NULL, Name nvarchar(100) NOT NULL, Description nvarchar(300) NOT NULL);
INSERT @Ten(GroupNo,Slot,Name,Description) VALUES
 (1,0,N'Gỏi cuốn',N'Bánh tráng cuốn tôm, thịt, bún và rau sống.'),
 (1,1,N'Chả giò',N'Chả giò chiên giòn, nhân thịt và khoai môn.'),
 (1,2,N'Nộm đu đủ',N'Đu đủ bào sợi trộn chua ngọt, lạc rang.'),
 (1,3,N'Đậu hũ chiên sả',N'Đậu hũ non chiên vàng, sả ớt thơm.'),
 (1,4,N'Súp cua',N'Súp cua trứng bắc thảo, nấm tuyết.'),
 (2,0,N'Cơm rang',N'Cơm rang trứng, rau củ, đảo lửa lớn.'),
 (2,1,N'Bò lúc lắc',N'Thăn bò áp chảo với ớt chuông, hành tây.'),
 (2,2,N'Cá kho tộ',N'Cá kho tộ đậm vị, ăn kèm cơm trắng.'),
 (2,3,N'Gà rang gừng',N'Gà ta rang gừng, nước màu sánh.'),
 (2,4,N'Sườn xào chua ngọt',N'Sườn non sốt chua ngọt, dứa và cà chua.'),
 (3,0,N'Lẩu Thái',N'Nước lẩu chua cay, hải sản và nấm.'),
 (3,1,N'Lẩu gà lá é',N'Gà ta, lá é, măng chua và bún tươi.'),
 (3,2,N'Lẩu nấm',N'Nước dùng rau củ với nhiều loại nấm.'),
 (3,3,N'Lẩu riêu cua',N'Riêu cua, đậu hũ, bắp bò và rau muống.'),
 (3,4,N'Lẩu cá kèo',N'Cá kèo, lá giang, nước lẩu chua thanh.'),
 (4,0,N'Chè hạt sen',N'Hạt sen bùi, nước đường phèn thanh mát.'),
 (4,1,N'Bánh flan',N'Bánh flan trứng sữa, phủ caramel.'),
 (4,2,N'Rau câu dừa',N'Rau câu hai lớp nước cốt dừa.'),
 (4,3,N'Chè khúc bạch',N'Khúc bạch phô mai, nhãn và hạnh nhân.'),
 (4,4,N'Sữa chua nếp cẩm',N'Sữa chua mát lạnh với nếp cẩm dẻo.'),
 (5,0,N'Trà đào',N'Trà đen ủ lạnh với đào miếng và sả.'),
 (5,1,N'Cà phê sữa đá',N'Cà phê phin pha sữa đặc, uống cùng đá.'),
 (5,2,N'Nước ép cam',N'Cam vắt tươi, không thêm đường.'),
 (5,3,N'Sinh tố bơ',N'Bơ sáp xay cùng sữa tươi.'),
 (5,4,N'Trà chanh',N'Trà xanh, chanh tươi và mật ong.'),
 (6,0,N'Ba chỉ nướng',N'Ba chỉ ướp mật ong, nướng than hoa.'),
 (6,1,N'Gà nướng muối ớt',N'Gà ta nướng muối ớt, da giòn.'),
 (6,2,N'Bò cuốn lá lốt',N'Bò băm cuốn lá lốt nướng thơm.'),
 (6,3,N'Mực nướng sa tế',N'Mực ống nướng sa tế cay nồng.'),
 (6,4,N'Nem lụi',N'Nem lụi nướng sả, chấm tương đậu.'),
 (7,0,N'Tôm hấp bia',N'Tôm sú hấp bia, chấm muối tiêu chanh.'),
 (7,1,N'Nghêu hấp sả',N'Nghêu hấp sả, ớt và lá chanh.'),
 (7,2,N'Cua sốt me',N'Cua thịt sốt me chua ngọt.'),
 (7,3,N'Ốc hương xào bơ tỏi',N'Ốc hương xào bơ tỏi thơm béo.'),
 (7,4,N'Cá hồi áp chảo',N'Cá hồi áp chảo sốt chanh dây.'),
 (8,0,N'Đậu hũ sốt cà',N'Đậu hũ non sốt cà chua, hành lá.'),
 (8,1,N'Rau củ xào nấm',N'Rau củ theo mùa xào nấm đông cô.'),
 (8,2,N'Cơm chiên chay',N'Cơm chiên rau củ, nấm và đậu Hà Lan.'),
 (8,3,N'Canh rong biển',N'Canh rong biển đậu hũ thanh nhẹ.'),
 (8,4,N'Bún xào chay',N'Bún xào rau củ, nấm và giá đỗ.');

DECLARE @today date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
DECLARE @Mon TABLE(
 N int NOT NULL PRIMARY KEY, GroupNo int NOT NULL,
 Name nvarchar(150) COLLATE Vietnamese_100_CI_AI NOT NULL, Description nvarchar(1000) NOT NULL,
 Price decimal(18,0) NOT NULL, SortOrder int NOT NULL, SoldOut bit NOT NULL);
DECLARE @n int=1,@g int,@slot int;
WHILE @n<=200
BEGIN
 SELECT @g=(@n-1)%8+1,@slot=((@n-1)/8)%5;
 INSERT @Mon(N,GroupNo,Name,Description,Price,SortOrder,SoldOut)
 SELECT @n,@g,
  CASE WHEN (@n-1)/8=12
       THEN CONCAT(t.Name,N' đặc biệt kiểu nhà làm với nhiều topping ăn kèm theo mùa (mẫu ',RIGHT(CONCAT('00',@n),3),N')')
       ELSE CONCAT(t.Name,N' (mẫu ',RIGHT(CONCAT('00',@n),3),N')') END,
  CASE WHEN (@n-1)/8=12
       THEN CONCAT(t.Description,N' Món có phần mô tả dài để kiểm tra việc cắt dòng trên màn hình nhỏ: nguyên liệu tươi mỗi ngày, ',
                   N'nước sốt pha theo công thức riêng của bếp, phục vụ kèm rau sống, đồ chua và nước chấm; có thể yêu cầu ít cay hoặc không hành.')
       ELSE t.Description END,
  CONVERT(decimal(18,0),20000+((@n*37)%60)*5000+CASE WHEN @n%50=0 THEN 1200000 ELSE 0 END),
  100+@n,
  CASE WHEN @n%10=0 THEN 1 ELSE 0 END
 FROM @Ten t WHERE t.GroupNo=@g AND t.Slot=@slot;
 SET @n+=1;
END;

INSERT dbo.MenuItems(CategoryId,Name,Price,Unit,Description,EstimatedPrepMinutes,ImagePath,SortOrder,IsActive,IsSoldOut,SoldOutBusinessDate)
SELECT c.Id,m.Name,m.Price,g.Unit,m.Description,10+m.N%20,NULL,m.SortOrder,1,m.SoldOut,CASE WHEN m.SoldOut=1 THEN @today END
FROM @Mon m
JOIN @Nhom g ON g.No=m.GroupNo
JOIN dbo.MenuCategories c ON c.NormalizedName COLLATE DATABASE_DEFAULT=UPPER(LTRIM(RTRIM(g.Name)))
WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuItems i WHERE i.CategoryId=c.Id AND i.Name=m.Name);

-- Chạy lại: mở bán lại các món mẫu đã bị ẩn và đặt lại trạng thái hết trong ngày theo kịch bản.
UPDATE i SET IsActive=1,IsSoldOut=m.SoldOut,SoldOutBusinessDate=CASE WHEN m.SoldOut=1 THEN @today ELSE i.SoldOutBusinessDate END,UpdatedAt=SYSUTCDATETIME()
FROM dbo.MenuItems i
JOIN @Mon m ON i.Name=m.Name
JOIN @Nhom g ON g.No=m.GroupNo
JOIN dbo.MenuCategories c ON c.Id=i.CategoryId AND c.NormalizedName COLLATE DATABASE_DEFAULT=UPPER(LTRIM(RTRIM(g.Name)));
