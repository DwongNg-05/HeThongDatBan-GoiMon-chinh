-- Bổ sung 50 món ăn, đồ uống cho thực đơn (lệnh DbTool: seed-menu-extra).
-- Chạy lại an toàn: chỉ thêm nhóm/món còn thiếu theo tên. Giá, mô tả, ảnh và trạng thái bán
-- do quản lý đã chỉnh sửa không bị ghi đè. Transaction do DbTool cung cấp.
-- File độc lập với PublicMenuDemo.sql để không làm thay đổi số món mà bài kiểm thử verify mong đợi.
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
 (N'Đồ uống',5,N'/images/thuc-don/do-uong.svg'),
 (N'Món nướng',6,N'/images/thuc-don/mon-chinh.svg'),
 (N'Hải sản',7,N'/images/thuc-don/lau.svg'),
 (N'Món chay',8,N'/images/thuc-don/khai-vi.svg');

INSERT dbo.MenuCategories(Name,SortOrder,DefaultImagePath)
SELECT n.Name,n.SortOrder,n.ImagePath FROM @Nhom n
WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuCategories c
                 WHERE c.NormalizedName COLLATE DATABASE_DEFAULT=UPPER(LTRIM(RTRIM(n.Name))));

-- Nhóm đã có nhưng chưa có ảnh mặc định thì được gán ảnh.
UPDATE c SET DefaultImagePath=n.ImagePath
FROM dbo.MenuCategories c
JOIN @Nhom n ON c.NormalizedName COLLATE DATABASE_DEFAULT=UPPER(LTRIM(RTRIM(n.Name)))
WHERE c.DefaultImagePath IS NULL;

-- Lệnh hide-menu-200 có thể đã tắt 3 nhóm này; bật lại vì nay nhóm đã có món thật.
UPDATE dbo.MenuCategories SET IsActive=1
WHERE Name IN (N'Món nướng',N'Hải sản',N'Món chay') AND IsActive=0;

DECLARE @Mon TABLE(
 CategoryName nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
 Name nvarchar(150) COLLATE Vietnamese_100_CI_AI NOT NULL,
 Price decimal(18,0) NOT NULL,
 Unit nvarchar(30) NOT NULL,
 Description nvarchar(1000) NOT NULL,
 PrepMinutes int NOT NULL,
 SortOrder int NOT NULL);
INSERT @Mon(CategoryName,Name,Price,Unit,Description,PrepMinutes,SortOrder) VALUES
 -- Khai vị (6)
 (N'Khai vị',N'Nem chua rán',55000,N'Đĩa',N'Nem chua Thanh Hoá lăn bột chiên giòn, chấm tương ớt.',10,10),
 (N'Khai vị',N'Gỏi bò bóp thấu',85000,N'Đĩa',N'Bò tái chanh trộn hành tây, rau răm, đậu phộng và nước mắm chua ngọt.',12,11),
 (N'Khai vị',N'Súp cua trứng bắc thảo',45000,N'Chén',N'Súp cua sánh nhẹ với trứng bắc thảo, nấm tuyết và ngò.',10,12),
 (N'Khai vị',N'Đậu hũ chiên sả ớt',45000,N'Đĩa',N'Đậu hũ non chiên vàng, rim sả ớt thơm cay.',10,13),
 (N'Khai vị',N'Khoai tây chiên bơ tỏi',45000,N'Phần',N'Khoai tây cắt múi chiên giòn, đảo bơ tỏi và lá mùi tây.',10,14),
 (N'Khai vị',N'Nộm sứa hoa chuối',65000,N'Đĩa',N'Sứa giòn, hoa chuối, rau thơm, trộn chua ngọt và lạc rang.',10,15),
 -- Món chính (8)
 (N'Món chính',N'Gà rang gừng',115000,N'Đĩa',N'Gà ta chặt miếng rang gừng, nước màu sánh, ăn kèm cơm trắng.',25,10),
 (N'Món chính',N'Sườn xào chua ngọt',125000,N'Đĩa',N'Sườn non chiên rồi xào sốt chua ngọt cùng dứa, cà chua, ớt chuông.',25,11),
 (N'Món chính',N'Thịt kho trứng',95000,N'Nồi',N'Thịt ba chỉ kho nước dừa với trứng vịt, mềm thơm, đậm đà.',30,12),
 (N'Món chính',N'Canh chua cá lóc',110000,N'Tô',N'Cá lóc nấu chua với me, dứa, cà chua, đậu bắp, giá và rau om.',20,13),
 (N'Món chính',N'Bò xào hành cần',105000,N'Đĩa',N'Thịt bò mềm xào nhanh với hành tây và cần tây.',15,14),
 (N'Món chính',N'Vịt quay Bắc Kinh',260000,N'Nửa con',N'Vịt quay da giòn, ăn kèm bánh tráng mỏng, dưa leo, hành boa-rô và sốt hoisin.',35,15),
 (N'Món chính',N'Mì xào giòn hải sản',95000,N'Đĩa',N'Mì trứng chiên giòn phủ tôm, mực, cải ngọt và nấm sốt sánh.',20,16),
 (N'Món chính',N'Cơm tấm sườn bì chả',75000,N'Đĩa',N'Sườn cốt lết nướng, bì, chả trứng, mỡ hành, nước mắm pha.',15,17),
 -- Lẩu (4)
 (N'Lẩu',N'Lẩu riêu cua bắp bò',290000,N'Nồi',N'Riêu cua đồng, bắp bò, đậu phụ chiên, chuối xanh và rau sống; cho 2–3 người.',25,10),
 (N'Lẩu',N'Lẩu cá kèo lá giang',270000,N'Nồi',N'Cá kèo tươi, nước lẩu chua dịu lá giang, ăn kèm bún và rau đồng; cho 2–3 người.',25,11),
 (N'Lẩu',N'Lẩu dê thuốc bắc',320000,N'Nồi',N'Thịt dê, nước dùng thuốc bắc ngọt thơm, đậu hũ ky và rau tần ô; cho 2–3 người.',30,12),
 (N'Lẩu',N'Lẩu bò nhúng giấm',299000,N'Nồi',N'Thăn bò thái mỏng nhúng giấm dừa, cuốn bánh tráng, rau sống; cho 2–3 người.',20,13),
 -- Món nướng (6)
 (N'Món nướng',N'Ba chỉ heo nướng mật ong',115000,N'Phần',N'Ba chỉ ướp mật ong, tỏi, nướng than hoa, ăn kèm kim chi.',20,10),
 (N'Món nướng',N'Bò cuộn nấm kim châm nướng',125000,N'Phần',N'Thịt bò Mỹ cuộn nấm kim châm, nướng sốt teriyaki.',15,11),
 (N'Món nướng',N'Gà nướng muối ớt',145000,N'Nửa con',N'Gà ta nướng muối ớt, da giòn thơm, chấm muối tiêu chanh lá é.',30,12),
 (N'Món nướng',N'Sườn heo nướng BBQ',155000,N'Phần',N'Sườn non nướng sốt BBQ khói, kèm bắp nướng và salad.',30,13),
 (N'Món nướng',N'Chân gà nướng sa tế',75000,N'Phần',N'Chân gà rút xương ướp sa tế cay, nướng than.',15,14),
 (N'Món nướng',N'Nem lụi Huế',85000,N'Phần',N'Nem lụi sả nướng than, cuốn bánh tráng, rau sống, chấm tương gan.',15,15),
 -- Hải sản (6)
 (N'Hải sản',N'Tôm sú hấp nước dừa',185000,N'Đĩa',N'Tôm sú tươi hấp nước dừa xiêm, chấm muối tiêu chanh.',15,10),
 (N'Hải sản',N'Mực chiên nước mắm',145000,N'Đĩa',N'Mực ống chiên giòn, rim nước mắm tỏi ớt.',15,11),
 (N'Hải sản',N'Nghêu hấp sả',95000,N'Tô',N'Nghêu trắng hấp sả, gừng, ớt và lá chanh.',12,12),
 (N'Hải sản',N'Hàu nướng mỡ hành',120000,N'Phần',N'6 con hàu sữa nướng mỡ hành, đậu phộng rang.',15,13),
 (N'Hải sản',N'Ốc hương xào bơ tỏi',165000,N'Đĩa',N'Ốc hương xào bơ tỏi thơm béo, ăn kèm bánh mì.',15,14),
 (N'Hải sản',N'Cá chẽm hấp xì dầu',245000,N'Con',N'Cá chẽm nguyên con hấp xì dầu, gừng và hành lá.',25,15),
 -- Món chay (5)
 (N'Món chay',N'Đậu hũ non sốt nấm',75000,N'Đĩa',N'Đậu hũ non hấp, rưới sốt nấm đông cô và nấm rơm.',15,10),
 (N'Món chay',N'Rau củ xào thập cẩm',65000,N'Đĩa',N'Bông cải, cà rốt, đậu Hà Lan, nấm và bắp non xào tỏi.',12,11),
 (N'Món chay',N'Cơm chiên chay ngũ sắc',70000,N'Đĩa',N'Cơm chiên với bắp, cà rốt, đậu que, nấm và hạt điều.',15,12),
 (N'Món chay',N'Gỏi cuốn chay',40000,N'Phần',N'2 cuốn đậu hũ, nấm, bún và rau sống, chấm tương đậu.',10,13),
 (N'Món chay',N'Canh nấm hạt sen',60000,N'Tô',N'Canh thanh ngọt từ nấm các loại, hạt sen và cà rốt.',15,14),
 -- Tráng miệng (5)
 (N'Tráng miệng',N'Chè khúc bạch',35000,N'Chén',N'Khúc bạch sữa hạnh nhân, nhãn, vải và hạnh nhân lát.',5,10),
 (N'Tráng miệng',N'Kem dừa',39000,N'Phần',N'Kem dừa mát lạnh trong trái dừa, rắc đậu phộng và cơm dừa.',5,11),
 (N'Tráng miệng',N'Sữa chua nếp cẩm',30000,N'Hũ',N'Sữa chua tự làm với nếp cẩm dẻo thơm.',5,12),
 (N'Tráng miệng',N'Bánh chuối nướng',35000,N'Phần',N'Bánh chuối nướng thơm, ăn kèm nước cốt dừa ấm.',5,13),
 (N'Tráng miệng',N'Trái cây thập cẩm',59000,N'Đĩa',N'Đĩa trái cây tươi theo mùa: dưa hấu, xoài, thanh long, nho.',5,14),
 -- Đồ uống (10)
 (N'Đồ uống',N'Bạc xỉu',32000,N'Ly',N'Sữa tươi, sữa đặc và chút cà phê phin, vị béo nhẹ.',5,10),
 (N'Đồ uống',N'Cà phê muối',39000,N'Ly',N'Cà phê phin phủ lớp kem muối béo mặn kiểu Huế.',5,11),
 (N'Đồ uống',N'Trà sữa trân châu đường đen',45000,N'Ly',N'Trà sữa đậm vị với trân châu nấu đường đen.',5,12),
 (N'Đồ uống',N'Trà vải hoa hồng',42000,N'Ly',N'Trà xanh ủ lạnh với vải ngâm và hương hoa hồng.',5,13),
 (N'Đồ uống',N'Nước dừa xiêm',35000,N'Trái',N'Dừa xiêm Bến Tre nguyên trái, ngọt mát tự nhiên.',3,14),
 (N'Đồ uống',N'Sinh tố bơ',45000,N'Ly',N'Bơ sáp xay cùng sữa đặc và đá.',5,15),
 (N'Đồ uống',N'Nước ép dưa hấu',38000,N'Ly',N'Dưa hấu ép tươi, không thêm đường.',5,16),
 (N'Đồ uống',N'Chanh dây đá',30000,N'Ly',N'Chanh dây tươi pha mật ong, chua ngọt mát lạnh.',3,17),
 (N'Đồ uống',N'Sữa đậu nành lá dứa',25000,N'Ly',N'Sữa đậu nành nấu với lá dứa, ít ngọt.',3,18),
 (N'Đồ uống',N'Bia Sài Gòn Special',30000,N'Chai',N'Bia lager chai 330 ml, phục vụ lạnh.',2,19);

INSERT dbo.MenuItems(CategoryId,Name,Price,Unit,Description,EstimatedPrepMinutes,ImagePath,SortOrder,IsActive)
SELECT c.Id,m.Name,m.Price,m.Unit,m.Description,m.PrepMinutes,n.ImagePath,m.SortOrder,1
FROM @Mon m
JOIN @Nhom n ON n.Name=m.CategoryName
JOIN dbo.MenuCategories c ON c.NormalizedName COLLATE DATABASE_DEFAULT=UPPER(LTRIM(RTRIM(m.CategoryName)))
WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuItems i WHERE i.CategoryId=c.Id AND i.Name=m.Name);
