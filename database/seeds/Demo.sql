-- Opt-in development data, repeatable. Never load into production.
-- Public-reservation migrations create default areas and DEMO-20 on a fresh
-- database. They are schema defaults, not an indication that demo data exists.
IF EXISTS(SELECT 1 FROM dbo.Users)
 OR EXISTS(SELECT 1 FROM dbo.DiningTables WHERE Code <> N'DEMO-20')
 OR EXISTS(SELECT 1 FROM dbo.MenuItems)
 OR EXISTS(SELECT 1 FROM dbo.Reservations)
 THROW 51200,N'Dữ liệu mẫu chỉ được nạp vào database mới, chưa có dữ liệu nghiệp vụ.',1;

INSERT dbo.Users(RoleId,FullName,UserName,Phone,IsActive) VALUES
 (1,N'Quản lý mẫu','manager','0900000001',0),(2,N'Phục vụ mẫu','waiter','0900000002',0),
 (3,N'Nhân viên bếp mẫu','kitchen','0900000003',0),(4,N'Thu ngân mẫu','cashier','0900000004',0);
-- The DbTool hashes a password supplied through RM_DEMO_PASSWORD and activates these accounts.

IF NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE NormalizedName=N'TẦNG MỘT')
 INSERT dbo.Areas(Name,SortOrder) VALUES(N'Tầng một',1);
IF NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE NormalizedName=N'TẦNG HAI')
 INSERT dbo.Areas(Name,SortOrder) VALUES(N'Tầng hai',2);
IF NOT EXISTS(SELECT 1 FROM dbo.Areas WHERE NormalizedName=N'SÂN VƯỜN')
 INSERT dbo.Areas(Name,SortOrder) VALUES(N'Sân vườn',3);

DECLARE @floorOneAreaId int=(SELECT Id FROM dbo.Areas WHERE NormalizedName=N'TẦNG MỘT');
DECLARE @floorTwoAreaId int=(SELECT Id FROM dbo.Areas WHERE NormalizedName=N'TẦNG HAI');
DECLARE @gardenAreaId int=(SELECT Id FROM dbo.Areas WHERE NormalizedName=N'SÂN VƯỜN');
DECLARE @n int=1;
WHILE @n<=60
BEGIN
 INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,SortOrder)
 VALUES(CASE WHEN @n<=20 THEN @floorOneAreaId WHEN @n<=40 THEN @floorTwoAreaId ELSE @gardenAreaId END,
  CONCAT(CASE WHEN @n<=20 THEN 'A' WHEN @n<=40 THEN 'B' ELSE 'C' END,
   RIGHT(CONCAT('0',CASE WHEN @n<=20 THEN @n WHEN @n<=40 THEN @n-20 ELSE @n-40 END),2)),
  1,CASE WHEN @n%10=0 THEN 8 WHEN @n%3=0 THEN 6 ELSE 4 END,@n);
 SET @n+=1;
END;
INSERT dbo.MenuCategories(Name,SortOrder,DefaultImagePath) VALUES
 (N'Khai vị',1,N'/images/thuc-don/khai-vi.svg'),
 (N'Món chính',2,N'/images/thuc-don/mon-chinh.svg'),
 (N'Lẩu',3,N'/images/thuc-don/lau.svg'),
 (N'Tráng miệng',4,N'/images/thuc-don/trang-mieng.svg'),
 (N'Đồ uống',5,N'/images/thuc-don/do-uong.svg');
SET @n=1;
WHILE @n<=60
BEGIN
 INSERT dbo.MenuItems(CategoryId,Name,Price,Unit,Description,EstimatedPrepMinutes,SortOrder)
 VALUES((@n-1)/12+1,CONCAT(CASE (@n-1)/12 WHEN 0 THEN N'Gỏi khai vị ' WHEN 1 THEN N'Cơm rang ' WHEN 2 THEN N'Lẩu nấm ' WHEN 3 THEN N'Chè trái cây ' ELSE N'Nước ép ' END,@n),
  20000+@n*5000,N'Phần',N'Món mẫu cho môi trường phát triển',10+@n%20,@n);
 SET @n+=1;
END;

DECLARE @base date=CONVERT(date,DATEADD(day,1,DATEADD(hour,7,SYSUTCDATETIME())));
SET @n=1;
WHILE @n<=20
BEGIN
 DECLARE @start datetime2(3)=DATEADD(hour,12,CONVERT(datetime2(3),DATEADD(day,(@n-1)%7,@base)));
 INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,StartsAt,EndsAt)
 VALUES(CONCAT('D',RIGHT(CONCAT('00000',@n),5)),CONCAT(N'Khách mẫu ',@n),CONCAT('091',RIGHT(CONCAT('0000000',@n),7)),
  CASE WHEN @n=3 THEN NULL ELSE CONCAT('khach',RIGHT(CONCAT('00',@n),2),'@example.com') END,
  2,@floorOneAreaId,@start,DATEADD(minute,90,@start));
 SET @n+=1;
END;
GO
