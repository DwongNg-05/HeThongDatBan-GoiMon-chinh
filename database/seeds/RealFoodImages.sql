-- Ảnh thực của món ăn cho môi trường demo. Ảnh được nhúng bằng URL HTTPS của
-- trang nguồn (không sao chép file về dự án); thay bằng ảnh do quán sở hữu trước
-- khi dùng thương mại. Chạy lại an toàn và chỉ sửa đường dẫn ảnh của dữ liệu mẫu.
SET NOCOUNT ON;

DECLARE @Images TABLE(CategoryName nvarchar(50) COLLATE DATABASE_DEFAULT PRIMARY KEY, ImagePath nvarchar(500) NOT NULL);
INSERT @Images(CategoryName,ImagePath) VALUES
 (N'Khai vị',N'https://planomagazine.com/wp-content/uploads/2022/03/101519_PhoisforLovers_KathyTran_QK6A9057.jpg'),
 (N'Món chính',N'https://wanderon-images.gumlet.io/gallery/new/2025/08/08/1754659281394-vietnam-house-menu.webp?auto=compress,format&w=768'),
 (N'Lẩu',N'https://images.squarespace-cdn.com/content/v1/596d151b37c5811b31d41cb1/1502119822580-8968NKTQKLO2E4BAL9SD/DSC_0659.JPG?format=1000w'),
 (N'Tráng miệng',N'https://media-cdn.grubhub.com/image/upload/d_search:browse-images:default.jpg/w_1200,q_auto,fl_lossy,dpr_auto,c_fill,f_auto,h_800,g_auto/b4n0nubgvncgnyuejpxr'),
 (N'Đồ uống',N'https://assets.simpleviewinc.com/simpleview/image/upload/c_limit,q_75,w_1200/v1/crm/dallasites101/Pho_Is_For_Lovers_1086_954d3aa6-5056-a36a-0ad4628fa26bb936.png');

-- Nhóm hiển thị ảnh thật cho bất kỳ món nào chưa có ảnh riêng.
UPDATE c SET DefaultImagePath=i.ImagePath
FROM dbo.MenuCategories c JOIN @Images i ON i.CategoryName=c.Name COLLATE DATABASE_DEFAULT;

-- Bộ 60 món nạp bởi seed-demo: gắn ảnh theo nhóm để không còn dùng SVG minh hoạ.
UPDATE m SET ImagePath=i.ImagePath, UpdatedAt=SYSUTCDATETIME()
FROM dbo.MenuItems m
JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
JOIN @Images i ON i.CategoryName=c.Name COLLATE DATABASE_DEFAULT
WHERE m.Name LIKE N'Gỏi khai vị %'
   OR m.Name LIKE N'Cơm rang %'
   OR m.Name LIKE N'Lẩu nấm %'
   OR m.Name LIKE N'Chè trái cây %'
   OR m.Name LIKE N'Nước ép %';

-- Các món diễn tập/public-menu cũng nhận ảnh thật theo nhóm nếu trước đó dùng SVG.
UPDATE m SET ImagePath=i.ImagePath, UpdatedAt=SYSUTCDATETIME()
FROM dbo.MenuItems m
JOIN dbo.MenuCategories c ON c.Id=m.CategoryId
JOIN @Images i ON i.CategoryName=c.Name COLLATE DATABASE_DEFAULT
WHERE m.ImagePath LIKE N'/images/thuc-don/%';
