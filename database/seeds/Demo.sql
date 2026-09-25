-- Opt-in development data, repeatable. Never load into production.
IF EXISTS(SELECT 1 FROM dbo.Users) OR EXISTS(SELECT 1 FROM dbo.Areas)
 THROW 51200,N'Dữ liệu mẫu chỉ được nạp vào database mới, chưa có người dùng hoặc khu vực.',1;
INSERT dbo.Users(RoleId,FullName,UserName,Phone,IsActive) VALUES
 (1,N'Quản lý mẫu','manager','0900000001',0),(2,N'Phục vụ mẫu','waiter','0900000002',0),
 (3,N'Nhân viên bếp mẫu','kitchen','0900000003',0),(4,N'Thu ngân mẫu','cashier','0900000004',0);
-- The DbTool hashes a password supplied through RM_DEMO_PASSWORD and activates these accounts.
INSERT dbo.Areas(Name,SortOrder) VALUES(N'Tầng một',1),(N'Tầng hai',2),(N'Sân vườn',3);
DECLARE @n int=1;
WHILE @n<=25
BEGIN
 INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,SortOrder)
 VALUES(CASE WHEN @n<=10 THEN 1 WHEN @n<=20 THEN 2 ELSE 3 END,CONCAT('A',RIGHT(CONCAT('0',@n),2)),1,CASE WHEN @n%5=0 THEN 12 ELSE 4 END,@n);
 SET @n+=1;
END;
INSERT dbo.MenuCategories(Name,SortOrder) VALUES(N'Khai vị',1),(N'Món chính',2),(N'Lẩu',3),(N'Tráng miệng',4),(N'Đồ uống',5);
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
 INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,PreferredAreaId,StartsAt,EndsAt)
 VALUES(CONCAT('D',RIGHT(CONCAT('00000',@n),5)),CONCAT(N'Khách mẫu ',@n),CONCAT('091',RIGHT(CONCAT('0000000',@n),7)),2,1,@start,DATEADD(minute,90,@start));
 SET @n+=1;
END;
GO
