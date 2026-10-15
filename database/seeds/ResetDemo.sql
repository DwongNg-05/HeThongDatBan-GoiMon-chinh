-- Reset only the opt-in demo database. Called inside a serializable transaction.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Name='DEMO_DATA')
    THROW 51210,N'Không tìm thấy dấu nhận diện DEMO_DATA. Hãy tạo dữ liệu mẫu trước.',1;

-- Never reset a demo database after it has been used for real dining/payment activity.
IF EXISTS (SELECT 1 FROM dbo.DiningSessions)
 OR EXISTS (SELECT 1 FROM dbo.SessionTables)
 OR EXISTS (SELECT 1 FROM dbo.Invoices)
    THROW 51211,N'Database có phiên phục vụ hoặc hoá đơn. Không thể khôi phục mẫu an toàn.',1;

DECLARE @now datetime2(3)=SYSUTCDATETIME();

-- Restore the four sample accounts. Preserve password hashes, but require a fresh password change.
UPDATE u SET RoleId=r.Id,FullName=d.FullName,UserName=d.UserName,Phone=d.Phone,Email=NULL,
 IsActive=1,MustChangePassword=1,FailedLoginCount=0,FailureWindowStartedAt=NULL,LockedUntil=NULL,
 SecurityStamp=NEWID(),PasswordChangedAt=NULL,LastLoginAt=NULL,UpdatedAt=@now
FROM dbo.Users u
JOIN (VALUES
 (1,'Manager',N'Quản lý mẫu',N'manager','0900000001'),
 (2,'Waiter',N'Phục vụ mẫu',N'waiter','0900000002'),
 (3,'Kitchen',N'Nhân viên bếp mẫu',N'kitchen','0900000003'),
 (4,'Cashier',N'Thu ngân mẫu',N'cashier','0900000004')
) d(UserId,RoleCode,FullName,UserName,Phone) ON u.Id=d.UserId
JOIN dbo.Roles r ON r.Code=d.RoleCode;
IF @@ROWCOUNT<>4 THROW 51212,N'Không đủ bốn tài khoản mẫu để khôi phục.',1;
UPDATE dbo.Users SET IsActive=0,UpdatedAt=@now WHERE UserName NOT IN (N'manager',N'waiter',N'kitchen',N'cashier');
UPDATE dbo.LoginSessions SET RevokedAt=@now
WHERE UserId IN (SELECT Id FROM dbo.Users WHERE UserName IN (N'manager',N'waiter',N'kitchen',N'cashier')) AND RevokedAt IS NULL;

-- Restore the canonical areas; additional areas are hidden from the demo.
DECLARE @Areas TABLE(Name nvarchar(80) COLLATE DATABASE_DEFAULT PRIMARY KEY,SortOrder int NOT NULL);
INSERT @Areas VALUES(N'Tầng một',1),(N'Tầng hai',2),(N'Sân vườn',3);
UPDATE a SET Name=d.Name,SortOrder=d.SortOrder,Notes=NULL,IsActive=1
FROM dbo.Areas a JOIN @Areas d ON a.NormalizedName=UPPER(d.Name);
INSERT dbo.Areas(Name,SortOrder)
SELECT d.Name,d.SortOrder FROM @Areas d
WHERE NOT EXISTS(SELECT 1 FROM dbo.Areas a WHERE a.NormalizedName=UPPER(d.Name));
UPDATE dbo.Areas SET IsActive=0 WHERE NormalizedName NOT IN (N'TẦNG MỘT',N'TẦNG HAI',N'SÂN VƯỜN');

-- Restore five menu groups.
DECLARE @Categories TABLE(Name nvarchar(50) COLLATE DATABASE_DEFAULT PRIMARY KEY,SortOrder int NOT NULL,ImagePath nvarchar(500) NOT NULL);
INSERT @Categories VALUES
 (N'Khai vị',1,N'/images/thuc-don/khai-vi.svg'),(N'Món chính',2,N'/images/thuc-don/mon-chinh.svg'),
 (N'Lẩu',3,N'/images/thuc-don/lau.svg'),(N'Tráng miệng',4,N'/images/thuc-don/trang-mieng.svg'),
 (N'Đồ uống',5,N'/images/thuc-don/do-uong.svg');
UPDATE c SET Name=d.Name,SortOrder=d.SortOrder,DefaultImagePath=d.ImagePath,IsActive=1
FROM dbo.MenuCategories c JOIN @Categories d ON c.NormalizedName=UPPER(d.Name);
INSERT dbo.MenuCategories(Name,SortOrder,DefaultImagePath)
SELECT d.Name,d.SortOrder,d.ImagePath FROM @Categories d
WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuCategories c WHERE c.NormalizedName=UPPER(d.Name));
UPDATE dbo.MenuCategories SET IsActive=0 WHERE NormalizedName NOT IN (N'KHAI VỊ',N'MÓN CHÍNH',N'LẨU',N'TRÁNG MIỆNG',N'ĐỒ UỐNG');

-- Build the exact 25-table catalogue (9/8/8) and restore its status and capacities.
DECLARE @Tables TABLE(Code varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY,AreaName nvarchar(80) COLLATE DATABASE_DEFAULT,TableNo int,Capacity int);
;WITH N AS (SELECT 1 AS Number UNION ALL SELECT Number+1 FROM N WHERE Number<25)
INSERT @Tables(Code,AreaName,TableNo,Capacity)
SELECT CONCAT(CASE WHEN Number<=9 THEN 'A' WHEN Number<=17 THEN 'B' ELSE 'C' END,
 RIGHT(CONCAT('0',CASE WHEN Number<=9 THEN Number WHEN Number<=17 THEN Number-9 ELSE Number-17 END),2)),
 CASE WHEN Number<=9 THEN N'Tầng một' WHEN Number<=17 THEN N'Tầng hai' ELSE N'Sân vườn' END,
 CASE WHEN Number<=9 THEN Number WHEN Number<=17 THEN Number-9 ELSE Number-17 END,
 CASE WHEN (CASE WHEN Number<=9 THEN Number WHEN Number<=17 THEN Number-9 ELSE Number-17 END)%8=0 THEN 8
      WHEN (CASE WHEN Number<=9 THEN Number WHEN Number<=17 THEN Number-9 ELSE Number-17 END)%3=0 THEN 6 ELSE 4 END
FROM N OPTION(MAXRECURSION 25);

-- Added tables are made inactive; no table history or QR tokens are deleted.
UPDATE t SET IsActive=0,Status='Available',StatusChangedAt=CASE WHEN Status<>'Available' THEN @now ELSE StatusChangedAt END
FROM dbo.DiningTables t LEFT JOIN @Tables d ON d.Code=t.Code WHERE d.Code IS NULL;
UPDATE q SET RevokedAt=@now FROM dbo.TableQrCodes q JOIN dbo.DiningTables t ON t.Id=q.TableId
LEFT JOIN @Tables d ON d.Code=t.Code WHERE d.Code IS NULL AND q.RevokedAt IS NULL;
UPDATE t SET AreaId=a.Id,MinCapacity=1,MaxCapacity=d.Capacity,TableType='Standard',Status='Available',IsActive=1,
 SortOrder=d.TableNo,StatusChangedAt=CASE WHEN t.Status<>'Available' THEN @now ELSE t.StatusChangedAt END
FROM dbo.DiningTables t JOIN @Tables d ON d.Code=t.Code JOIN dbo.Areas a ON a.NormalizedName=UPPER(d.AreaName);
INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,TableType,Status,IsActive,SortOrder,StatusChangedAt)
SELECT a.Id,d.Code,1,d.Capacity,'Standard','Available',1,d.TableNo,@now
FROM @Tables d JOIN dbo.Areas a ON a.NormalizedName=UPPER(d.AreaName)
WHERE NOT EXISTS(SELECT 1 FROM dbo.DiningTables t WHERE t.Code=d.Code);

-- Rebuild dish names and catalogue values. Historical price/availability logs are retained.
DECLARE @Dishes TABLE(Number int PRIMARY KEY,CategoryName nvarchar(50) COLLATE DATABASE_DEFAULT,Name nvarchar(150) COLLATE DATABASE_DEFAULT,Price decimal(18,0),PrepMinutes int);
;WITH N AS (SELECT 1 AS Number UNION ALL SELECT Number+1 FROM N WHERE Number<60)
INSERT @Dishes(Number,CategoryName,Name,Price,PrepMinutes)
SELECT Number,
 CASE (Number-1)/12 WHEN 0 THEN N'Khai vị' WHEN 1 THEN N'Món chính' WHEN 2 THEN N'Lẩu' WHEN 3 THEN N'Tráng miệng' ELSE N'Đồ uống' END,
 CONCAT(CASE (Number-1)/12 WHEN 0 THEN N'Gỏi khai vị ' WHEN 1 THEN N'Cơm rang ' WHEN 2 THEN N'Lẩu nấm ' WHEN 3 THEN N'Chè trái cây ' ELSE N'Nước ép ' END,Number),
 20000+Number*5000,10+Number%20
FROM N OPTION(MAXRECURSION 60);
UPDATE m SET CategoryId=c.Id,Price=d.Price,Unit=N'Phần',Description=N'Món mẫu cho môi trường phát triển',
 EstimatedPrepMinutes=d.PrepMinutes,IsActive=1,IsSoldOut=0,SoldOutBusinessDate=NULL,
 ImagePath=NULL,ImageContentType=NULL,ImageSizeBytes=NULL,ImageWidth=NULL,ImageHeight=NULL,
 SortOrder=d.Number,UpdatedAt=@now
FROM dbo.MenuItems m JOIN @Dishes d ON d.Name=m.Name COLLATE DATABASE_DEFAULT
JOIN dbo.MenuCategories c ON c.NormalizedName=UPPER(d.CategoryName);
INSERT dbo.MenuItems(CategoryId,Name,Price,Unit,Description,EstimatedPrepMinutes,IsActive,IsSoldOut,SortOrder,CreatedAt,UpdatedAt)
SELECT c.Id,d.Name,d.Price,N'Phần',N'Món mẫu cho môi trường phát triển',d.PrepMinutes,1,0,d.Number,@now,@now
FROM @Dishes d JOIN dbo.MenuCategories c ON c.NormalizedName=UPPER(d.CategoryName)
WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuItems m WHERE m.Name COLLATE DATABASE_DEFAULT=d.Name);
UPDATE m SET IsActive=0,IsSoldOut=0,SoldOutBusinessDate=NULL,UpdatedAt=@now
FROM dbo.MenuItems m LEFT JOIN @Dishes d ON d.Name=m.Name COLLATE DATABASE_DEFAULT WHERE d.Number IS NULL;

-- Remove demo booking notifications/events and non-canonical test reservations,
-- while preserving IDs for the twenty canonical sample bookings.
DELETE dbo.ReservationTableChanges;
DELETE dbo.ReservationEvents;
DELETE dbo.EmailOutbox;
DELETE dbo.Reservations
WHERE Code NOT LIKE 'D[0-9][0-9][0-9][0-9][0-9]'
   OR TRY_CONVERT(int,SUBSTRING(Code,2,5)) NOT BETWEEN 1 AND 20;
DECLARE @base date=CONVERT(date,DATEADD(day,1,DATEADD(hour,7,@now))),@n int=1;
WHILE @n<=20
BEGIN
 DECLARE @start datetime2(3)=DATEADD(hour,12,CONVERT(datetime2(3),DATEADD(day,(@n-1)%7,@base)));
 DECLARE @areaId int=CASE WHEN @n%3=1 THEN (SELECT Id FROM dbo.Areas WHERE NormalizedName=N'TẦNG MỘT')
                          WHEN @n%3=2 THEN (SELECT Id FROM dbo.Areas WHERE NormalizedName=N'TẦNG HAI')
                          ELSE (SELECT Id FROM dbo.Areas WHERE NormalizedName=N'SÂN VƯỜN') END;
 IF EXISTS(SELECT 1 FROM dbo.Reservations WHERE Code=CONCAT('D',RIGHT(CONCAT('00000',@n),5)))
  UPDATE dbo.Reservations SET CustomerName=CONCAT(N'Khách mẫu ',@n),Phone=CONCAT('091',RIGHT(CONCAT('0000000',@n),7)),
   Email=CASE WHEN @n=3 THEN NULL ELSE CONCAT('khach',RIGHT(CONCAT('00',@n),2),'@example.com') END,
   GuestCount=2,PreferredAreaId=@areaId,TableId=NULL,StartsAt=@start,EndsAt=DATEADD(minute,90,@start),Status='Pending',
   Notes=NULL,RejectionReason=NULL,CancelReason=NULL,CancelledAt=NULL,ConfirmedAt=NULL,ConfirmedBy=NULL,
   ArrivedAt=NULL,NoShowAt=NULL,HoldExtendedUntil=NULL,ExtensionCount=0
  WHERE Code=CONCAT('D',RIGHT(CONCAT('00000',@n),5));
 ELSE
  INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,StartsAt,EndsAt,Status)
  VALUES(CONCAT('D',RIGHT(CONCAT('00000',@n),5)),CONCAT(N'Khách mẫu ',@n),CONCAT('091',RIGHT(CONCAT('0000000',@n),7)),
   CASE WHEN @n=3 THEN NULL ELSE CONCAT('khach',RIGHT(CONCAT('00',@n),2),'@example.com') END,
   2,@areaId,@start,DATEADD(minute,90,@start),'Pending');
 SET @n+=1;
END;

SELECT
 (SELECT COUNT(*) FROM dbo.Areas WHERE IsActive=1) AS Areas,
 (SELECT COUNT(*) FROM dbo.DiningTables WHERE IsActive=1) AS Tables,
 (SELECT COUNT(*) FROM dbo.MenuCategories WHERE IsActive=1) AS Categories,
 (SELECT COUNT(*) FROM dbo.MenuItems WHERE IsActive=1) AS Dishes,
 (SELECT COUNT(*) FROM dbo.Users WHERE IsActive=1 AND UserName IN (N'manager',N'waiter',N'kitchen',N'cashier')) AS Accounts,
 (SELECT COUNT(*) FROM dbo.Reservations) AS Reservations,
 (SELECT COUNT(*) FROM dbo.Reservations WHERE Status='Pending') AS PendingReservations;
