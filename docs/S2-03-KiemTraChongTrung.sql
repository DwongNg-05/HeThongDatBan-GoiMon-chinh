-- S2-03 — Kiểm tra nhanh chống trùng bàn và 15 phút dọn bàn (chạy trong SSMS trên database dev).
-- Các lượt thử có mã bắt đầu bằng "ZZT" và được XOÁ ở đầu và cuối script: không để lại dữ liệu.
-- (Không bọc trong một giao dịch: lỗi từ trigger luôn huỷ giao dịch đang mở, các bước sau sẽ chạy ngoài giao dịch.)
-- Mỗi dòng kết quả in "PASS" / "FAIL". Giờ lưu trong database là UTC; bảng cuối hiển thị theo giờ Việt Nam.
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ReservationEvents') IS NOT NULL
    DELETE e FROM dbo.ReservationEvents e JOIN dbo.Reservations r ON r.Id=e.ReservationId WHERE r.Code LIKE 'ZZT%';
DELETE FROM dbo.Reservations WHERE Code LIKE 'ZZT%';

DECLARE @table int = (SELECT TOP (1) t.Id FROM dbo.DiningTables t JOIN dbo.Areas a ON a.Id=t.AreaId
                      WHERE t.IsActive=1 AND a.IsActive=1 AND t.MaxCapacity>=2 ORDER BY t.Id);
DECLARE @day date = DATEADD(day, 400, CONVERT(date, SYSUTCDATETIME()));   -- một ngày trống trong tương lai
DECLARE @t0 datetime2(3) = DATEADD(hour, 2, CONVERT(datetime2(3), @day)); -- 02:00 UTC = 09:00 giờ Việt Nam
DECLARE @duration int = COALESCE((SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1), 90);
DECLARE @r TABLE(Step nvarchar(200), Result varchar(4));
DECLARE @ok bit;

-- Lượt A (Chờ xác nhận): @t0 → @t0 + 90 phút, giữ bàn tới @t0 + 105 phút.
INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status)
VALUES('ZZT001',N'Lượt A','0999000001',2,@table,@t0,DATEADD(minute,@duration,@t0),'Pending');

-- 1. Hai khung giờ giao nhau (bắt đầu sau A 30 phút) → phải bị từ chối.
SET @ok = 0;
BEGIN TRY
    INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status)
    VALUES('ZZT002',N'Giao nhau','0999000002',2,@table,DATEADD(minute,30,@t0),DATEADD(minute,30+@duration,@t0),'Pending');
END TRY BEGIN CATCH IF ERROR_NUMBER()=51060 SET @ok = 1; END CATCH;
INSERT @r VALUES(N'1. Giao nhau → từ chối', IIF(@ok=1,'PASS','FAIL'));

-- 2. Nghỉ dưới 15 phút (bắt đầu 10 phút sau khi A kết thúc) → phải bị từ chối.
SET @ok = 0;
BEGIN TRY
    INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status)
    VALUES('ZZT003',N'Nghỉ 10 phút','0999000003',2,@table,DATEADD(minute,@duration+10,@t0),DATEADD(minute,2*@duration+10,@t0),'Confirmed');
END TRY BEGIN CATCH IF ERROR_NUMBER()=51060 SET @ok = 1; END CATCH;
INSERT @r VALUES(N'2. Nghỉ dưới 15 phút (lượt Đã xác nhận sau lượt Chờ xác nhận) → từ chối', IIF(@ok=1,'PASS','FAIL'));

-- 3. Cách đúng 15 phút dọn bàn → được nhận (lượt B, Đã xác nhận).
SET @ok = 1;
BEGIN TRY
    INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status)
    VALUES('ZZT004',N'Đúng 15 phút','0999000004',2,@table,DATEADD(minute,@duration+15,@t0),DATEADD(minute,2*@duration+15,@t0),'Confirmed');
END TRY BEGIN CATCH SET @ok = 0; END CATCH;
INSERT @r VALUES(N'3. Cách đúng 15 phút → được nhận', IIF(@ok=1,'PASS','FAIL'));

-- 4. Lượt Đã xác nhận (B) cũng giữ bàn: lượt Chờ xác nhận chồng lên B → từ chối.
SET @ok = 0;
BEGIN TRY
    INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status)
    VALUES('ZZT005',N'Chồng lên B','0999000005',2,@table,DATEADD(minute,@duration+60,@t0),DATEADD(minute,2*@duration+60,@t0),'Pending');
END TRY BEGIN CATCH IF ERROR_NUMBER()=51060 SET @ok = 1; END CATCH;
INSERT @r VALUES(N'4. Đã xác nhận chặn Chờ xác nhận → từ chối', IIF(@ok=1,'PASS','FAIL'));

-- 5. Đổi giờ một lượt (UPDATE trực tiếp) để lấn vào khoảng dọn bàn → từ chối.
SET @ok = 0;
BEGIN TRY
    UPDATE dbo.Reservations SET StartsAt=DATEADD(minute,-5,StartsAt), EndsAt=DATEADD(minute,-5,EndsAt) WHERE Code='ZZT004';
END TRY BEGIN CATCH IF ERROR_NUMBER()=51060 SET @ok = 1; END CATCH;
INSERT @r VALUES(N'5. UPDATE trực tiếp lấn khoảng dọn bàn → từ chối', IIF(@ok=1,'PASS','FAIL'));

-- 6. Danh sách bàn trống cho khách ẩn bàn trong khoảng dọn bàn, hiện bàn khi hết 15 phút.
DECLARE @free TABLE(Id int,Code varchar(20),AreaId int,AreaName nvarchar(200),MinCapacity int,MaxCapacity int);
DECLARE @inside datetime2(3) = DATEADD(minute,2*@duration+15+10,@t0);  -- 10 phút sau khi B kết thúc
DECLARE @after  datetime2(3) = DATEADD(minute,2*@duration+15+15,@t0);  -- đúng 15 phút sau khi B kết thúc
INSERT @free EXEC dbo.usp_AvailableTables @StartsAt=@inside, @GuestCount=2;
SET @ok = IIF(EXISTS(SELECT 1 FROM @free WHERE Id=@table), 0, 1);
DELETE @free;
INSERT @free EXEC dbo.usp_AvailableTables @StartsAt=@after, @GuestCount=2;
SET @ok = IIF(@ok=1 AND EXISTS(SELECT 1 FROM @free WHERE Id=@table), 1, 0);
INSERT @r VALUES(N'6. Danh sách bàn trống tính 15 phút dọn bàn', IIF(@ok=1,'PASS','FAIL'));

-- 7. Lượt đã huỷ không giữ bàn: huỷ A rồi đặt lại đúng giờ của A → được nhận.
UPDATE dbo.Reservations SET Status='Cancelled', CancelledAt=SYSUTCDATETIME() WHERE Code='ZZT001';
SET @ok = 1;
BEGIN TRY
    INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status)
    VALUES('ZZT006',N'Thay lượt huỷ','0999000006',2,@table,@t0,DATEADD(minute,@duration,@t0),'Pending');
END TRY BEGIN CATCH SET @ok = 0; END CATCH;
INSERT @r VALUES(N'7. Lượt đã huỷ không giữ bàn', IIF(@ok=1,'PASS','FAIL'));

SELECT Step AS [Bước kiểm tra], Result AS [Kết quả] FROM @r;
SELECT Code, CustomerName, Status,
       DATEADD(hour,7,StartsAt) AS [Bắt đầu (giờ VN)], DATEADD(hour,7,EndsAt) AS [Kết thúc (giờ VN)],
       DATEADD(hour,7,DATEADD(minute,15,EndsAt)) AS [Hết dọn bàn (giờ VN)]
FROM dbo.Reservations WHERE Code LIKE 'ZZT%' ORDER BY StartsAt;

-- Dọn dữ liệu kiểm tra.
IF OBJECT_ID(N'dbo.ReservationEvents') IS NOT NULL
    DELETE e FROM dbo.ReservationEvents e JOIN dbo.Reservations r ON r.Id=e.ReservationId WHERE r.Code LIKE 'ZZT%';
DELETE FROM dbo.Reservations WHERE Code LIKE 'ZZT%';
