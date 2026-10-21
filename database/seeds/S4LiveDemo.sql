-- Additive operational data for demonstrating the complete S4 workflow.
-- It intentionally never deletes or rewrites existing rows.  The marker is a
-- reservation code so a second invocation is a no-op.
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.Reservations WHERE Code = 'L4A009')
BEGIN
    PRINT N'S4 live demo data already exists; existing data preserved.';
    RETURN;
END;

DECLARE @manager int = (SELECT TOP (1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE r.Name IN (N'Manager',N'Quản lý') AND u.IsActive=1 ORDER BY u.Id);
DECLARE @waiter int = (SELECT TOP (1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE r.Name IN (N'Waiter',N'Phục vụ') AND u.IsActive=1 ORDER BY u.Id);
DECLARE @kitchen int = (SELECT TOP (1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE r.Name IN (N'Kitchen',N'Bếp') AND u.IsActive=1 ORDER BY u.Id);
DECLARE @cashier int = (SELECT TOP (1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId WHERE r.Name IN (N'Cashier',N'Thu ngân') AND u.IsActive=1 ORDER BY u.Id);
DECLARE @shift bigint = (SELECT TOP (1) Id FROM dbo.Shifts WHERE Status='Open' ORDER BY Id DESC);

IF @manager IS NULL OR @waiter IS NULL OR @kitchen IS NULL OR @cashier IS NULL
    THROW 51370, N'Cần đủ bốn tài khoản nội bộ trước khi nạp dữ liệu S4 live.', 1;

-- A fresh staging database only has the four accounts and closed historical
-- shifts.  Give the rehearsal its own open shift instead of requiring a
-- manual cashier action before the seed can be used.
IF @shift IS NULL
BEGIN
    INSERT dbo.Shifts(Name,BusinessDate,OpenedBy,OpenedAt,OpeningCash,Status)
    VALUES(N'Ca demo S4 đang mở',CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())),@manager,SYSUTCDATETIME(),0,'Open');
    SET @shift=SCOPE_IDENTITY();
    INSERT dbo.ShiftEvents(ShiftId,Action,ActorUserId,Reason)
    VALUES(@shift,'Opened',@manager,N'Tự tạo khi nạp dữ liệu diễn tập S4.');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE NormalizedName=N'DỮ LIỆU DEMO S4')
    INSERT dbo.Areas(Name,SortOrder,Notes) VALUES(N'Dữ liệu demo S4',999,N'Bàn riêng để diễn tập nghiệp vụ S4.');
DECLARE @area int=(SELECT Id FROM dbo.Areas WHERE NormalizedName=N'DỮ LIỆU DEMO S4');

DECLARE @tables table(Code varchar(20) NOT NULL, Capacity int NOT NULL, SortOrder int NOT NULL, X int NOT NULL, Y int NOT NULL);
INSERT @tables VALUES
 ('S4D04',4,4,80,80),('S4D05',4,5,210,80),('S4D06',6,6,340,80),('S4D07',4,7,470,80),
 ('S4D08',4,8,80,210),('S4D09',4,9,210,210),('S4D10',4,10,340,210),('S4D11',6,11,470,210),
 ('S4D12',4,12,80,340),('S4D13',4,13,210,340),('S4D14',6,14,340,340);
INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,SortOrder,PositionX,PositionY)
SELECT @area,Code,1,Capacity,SortOrder,X,Y FROM @tables src
WHERE NOT EXISTS(SELECT 1 FROM dbo.DiningTables t WHERE t.Code=src.Code);

IF (SELECT COUNT(*) FROM dbo.DiningTables WHERE Code IN ('S4D04','S4D05','S4D06','S4D07','S4D08','S4D09','S4D10')) <> 7
    THROW 51371, N'Không thể chuẩn bị đủ bàn S4 live.', 1;

-- Refuse rather than overwrite a table that a real user is already using.
IF EXISTS (
    SELECT 1 FROM dbo.DiningTables t
    WHERE t.Code IN ('S4D04','S4D05','S4D06','S4D07','S4D08','S4D09','S4D10')
      AND (t.Status <> 'Available' OR EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId=t.Id AND st.ReleasedAt IS NULL)))
    THROW 51372, N'Một bàn S4 live không còn trống. Dữ liệu hiện tại được giữ nguyên; hãy chọn bàn demo trống rồi chạy lại.', 1;

DECLARE @m1 int=(SELECT TOP (1) Id FROM dbo.MenuItems WHERE IsActive=1 AND IsSoldOut=0 ORDER BY Id);
DECLARE @m2 int=(SELECT TOP (1) Id FROM dbo.MenuItems WHERE IsActive=1 AND IsSoldOut=0 AND Id<>@m1 ORDER BY Id);
DECLARE @m3 int=(SELECT TOP (1) Id FROM dbo.MenuItems WHERE IsActive=1 AND IsSoldOut=0 AND Id NOT IN(@m1,@m2) ORDER BY Id);
DECLARE @m4 int=(SELECT TOP (1) Id FROM dbo.MenuItems WHERE IsActive=1 AND IsSoldOut=0 AND Id NOT IN(@m1,@m2,@m3) ORDER BY Id);
DECLARE @m5 int=(SELECT TOP (1) Id FROM dbo.MenuItems WHERE IsActive=1 AND IsSoldOut=0 AND Id NOT IN(@m1,@m2,@m3,@m4) ORDER BY Id);
DECLARE @m6 int=(SELECT TOP (1) Id FROM dbo.MenuItems WHERE IsActive=1 AND IsSoldOut=0 AND Id NOT IN(@m1,@m2,@m3,@m4,@m5) ORDER BY Id);
IF @m6 IS NULL THROW 51373,N'Cần tối thiểu sáu món đang bán trước khi nạp S4 live.',1;

DECLARE @t4 int=(SELECT Id FROM dbo.DiningTables WHERE Code='S4D04'), @t5 int=(SELECT Id FROM dbo.DiningTables WHERE Code='S4D05'),
        @t6 int=(SELECT Id FROM dbo.DiningTables WHERE Code='S4D06'), @t7 int=(SELECT Id FROM dbo.DiningTables WHERE Code='S4D07'),
        @t8 int=(SELECT Id FROM dbo.DiningTables WHERE Code='S4D08'), @t9 int=(SELECT Id FROM dbo.DiningTables WHERE Code='S4D09');
DECLARE @now datetime2(3)=SYSUTCDATETIME(), @s4 bigint, @s5 bigint, @s6 bigint, @s7 bigint, @s8 bigint;

-- Two adjacent, independent tables are ready for the waiter to merge.
INSERT dbo.TableAdjacencies(TableId,AdjacentTableId)
SELECT CASE WHEN @t4<@t5 THEN @t4 ELSE @t5 END, CASE WHEN @t4<@t5 THEN @t5 ELSE @t4 END
WHERE NOT EXISTS(SELECT 1 FROM dbo.TableAdjacencies WHERE TableId=CASE WHEN @t4<@t5 THEN @t4 ELSE @t5 END AND AdjacentTableId=CASE WHEN @t4<@t5 THEN @t5 ELSE @t4 END);

INSERT dbo.DiningSessions(ShiftId,GuestCount,Status,OpenedBy,OpenedAt) VALUES
 (@shift,2,'Open',@waiter,DATEADD(minute,-45,@now)),(@shift,3,'Open',@waiter,DATEADD(minute,-38,@now)),
 (@shift,4,'Open',@waiter,DATEADD(minute,-32,@now)),(@shift,2,'AwaitingPayment',@waiter,DATEADD(minute,-52,@now));
SELECT @s4=MIN(Id),@s5=MIN(Id)+1,@s6=MIN(Id)+2,@s7=MIN(Id)+3 FROM dbo.DiningSessions WHERE ShiftId=@shift AND OpenedAt>=DATEADD(minute,-53,@now) AND OpenedAt<=@now AND OpenedBy=@waiter AND Status IN('Open','AwaitingPayment');
-- The four rows above are unique because target tables were required to be free; use their exact opening times to avoid touching any pre-existing session.
SELECT @s4=Id FROM dbo.DiningSessions WHERE ShiftId=@shift AND OpenedBy=@waiter AND OpenedAt=DATEADD(minute,-45,@now);
SELECT @s5=Id FROM dbo.DiningSessions WHERE ShiftId=@shift AND OpenedBy=@waiter AND OpenedAt=DATEADD(minute,-38,@now);
SELECT @s6=Id FROM dbo.DiningSessions WHERE ShiftId=@shift AND OpenedBy=@waiter AND OpenedAt=DATEADD(minute,-32,@now);
SELECT @s7=Id FROM dbo.DiningSessions WHERE ShiftId=@shift AND OpenedBy=@waiter AND OpenedAt=DATEADD(minute,-52,@now);
INSERT dbo.SessionTables(SessionId,TableId,AssignedAt) VALUES
 (@s4,@t4,DATEADD(minute,-45,@now)),(@s5,@t5,DATEADD(minute,-38,@now)),(@s6,@t6,DATEADD(minute,-32,@now)),(@s7,@t7,DATEADD(minute,-52,@now));
UPDATE dbo.DiningTables SET Status='Serving',StatusChangedAt=@now WHERE Id IN(@t4,@t5,@t6,@t7);

-- Each active table has an independent batch.  S4D06 deliberately spans all kitchen states and both cancellation outcomes.
DECLARE @b4 bigint,@b5 bigint,@b6 bigint,@b7 bigint;
INSERT dbo.OrderBatches(SessionId,OriginSessionId,BatchNumber,RequestId,CreatedBy,CreatedAt) VALUES(@s4,@s4,1,NEWID(),@waiter,DATEADD(minute,-40,@now)); SET @b4=SCOPE_IDENTITY();
INSERT dbo.OrderBatches(SessionId,OriginSessionId,BatchNumber,RequestId,CreatedBy,CreatedAt) VALUES(@s5,@s5,1,NEWID(),@waiter,DATEADD(minute,-34,@now)); SET @b5=SCOPE_IDENTITY();
INSERT dbo.OrderBatches(SessionId,OriginSessionId,BatchNumber,RequestId,CreatedBy,CreatedAt) VALUES(@s6,@s6,1,NEWID(),@waiter,DATEADD(minute,-28,@now)); SET @b6=SCOPE_IDENTITY();
INSERT dbo.OrderBatches(SessionId,OriginSessionId,BatchNumber,RequestId,CreatedBy,CreatedAt) VALUES(@s7,@s7,1,NEWID(),@waiter,DATEADD(minute,-48,@now)); SET @b7=SCOPE_IDENTITY();

INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,Notes,EstimatedPrepMinutes,Status,SubmittedAt,PreparingAt,ReadyAt,ServedAt,ServedBy)
SELECT @b4,Id,@t4,Name,Unit,Price,2,N'Bàn ghép - món đã phục vụ',EstimatedPrepMinutes,'Served',DATEADD(minute,-40,@now),DATEADD(minute,-37,@now),DATEADD(minute,-30,@now),DATEADD(minute,-27,@now),@waiter FROM dbo.MenuItems WHERE Id=@m1;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,SubmittedAt)
SELECT @b5,Id,@t5,Name,Unit,Price,1,EstimatedPrepMinutes,'Pending',DATEADD(minute,-34,@now) FROM dbo.MenuItems WHERE Id=@m2;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,SubmittedAt)
SELECT @b6,Id,@t6,Name,Unit,Price,1,EstimatedPrepMinutes,'Pending',DATEADD(minute,-28,@now) FROM dbo.MenuItems WHERE Id=@m1;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,SubmittedAt,PreparingAt)
SELECT @b6,Id,@t6,Name,Unit,Price,1,EstimatedPrepMinutes,'Preparing',DATEADD(minute,-27,@now),DATEADD(minute,-24,@now) FROM dbo.MenuItems WHERE Id=@m2;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,SubmittedAt,PreparingAt,ReadyAt)
SELECT @b6,Id,@t6,Name,Unit,Price,1,EstimatedPrepMinutes,'Ready',DATEADD(minute,-26,@now),DATEADD(minute,-23,@now),DATEADD(minute,-18,@now) FROM dbo.MenuItems WHERE Id=@m3;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,SubmittedAt,PreparingAt,ReadyAt,ServedAt,ServedBy)
SELECT @b6,Id,@t6,Name,Unit,Price,1,EstimatedPrepMinutes,'Served',DATEADD(minute,-25,@now),DATEADD(minute,-22,@now),DATEADD(minute,-17,@now),DATEADD(minute,-15,@now),@waiter FROM dbo.MenuItems WHERE Id=@m4;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,SubmittedAt,CancelledAt,CancelledBy,CancelReason,CancellationNote,ChargeWhenCancelled)
SELECT @b6,Id,@t6,Name,Unit,Price,1,EstimatedPrepMinutes,'Cancelled',DATEADD(minute,-20,@now),DATEADD(minute,-19,@now),@waiter,'ChangedMind',N'Khách đổi ý trước khi bếp làm',0 FROM dbo.MenuItems WHERE Id=@m5;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,SubmittedAt,PreparingAt,CancelledAt,CancelledBy,CancelReason,CancellationNote,ChargeWhenCancelled)
SELECT @b6,Id,@t6,Name,Unit,Price,1,EstimatedPrepMinutes,'Cancelled',DATEADD(minute,-19,@now),DATEADD(minute,-17,@now),DATEADD(minute,-13,@now),@manager,'Mistake',N'Đã bắt đầu chế biến - tính tiền',1 FROM dbo.MenuItems WHERE Id=@m6;
INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,SubmittedAt,PreparingAt,ReadyAt,ServedAt,ServedBy)
SELECT @b7,Id,@t7,Name,Unit,Price,2,EstimatedPrepMinutes,'Served',DATEADD(minute,-48,@now),DATEADD(minute,-45,@now),DATEADD(minute,-40,@now),DATEADD(minute,-35,@now),@waiter FROM dbo.MenuItems WHERE Id=@m1;

INSERT dbo.OrderItemEvents(OrderItemId,ToStatus,ActorUserId,OccurredAt)
SELECT Id,Status,CASE WHEN Status='Served' THEN @waiter WHEN Status IN('Preparing','Ready') THEN @kitchen WHEN Status='Cancelled' THEN COALESCE(CancelledBy,@waiter) ELSE @waiter END,COALESCE(CancelledAt,ServedAt,ReadyAt,PreparingAt,SubmittedAt)
FROM dbo.OrderItems WHERE BatchId IN(@b4,@b5,@b6,@b7);

-- A freshly paid table is deliberately left in Cleaning; it can be cleaned by the waiter.
INSERT dbo.DiningSessions(ShiftId,GuestCount,Status,OpenedBy,OpenedAt,ClosedAt,ClosedBy) VALUES(@shift,2,'Closed',@waiter,DATEADD(minute,-85,@now),DATEADD(minute,-8,@now),@cashier); SET @s8=SCOPE_IDENTITY();
INSERT dbo.SessionTables(SessionId,TableId,AssignedAt,ReleasedAt) VALUES(@s8,@t8,DATEADD(minute,-85,@now),DATEADD(minute,-8,@now));
DECLARE @b8 bigint; INSERT dbo.OrderBatches(SessionId,OriginSessionId,BatchNumber,RequestId,CreatedBy,CreatedAt) VALUES(@s8,@s8,1,NEWID(),@waiter,DATEADD(minute,-80,@now)); SET @b8=SCOPE_IDENTITY();
DECLARE @i8 bigint; INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,SubmittedAt,PreparingAt,ReadyAt,ServedAt,ServedBy)
SELECT @b8,Id,@t8,Name,Unit,Price,2,EstimatedPrepMinutes,'Served',DATEADD(minute,-80,@now),DATEADD(minute,-75,@now),DATEADD(minute,-68,@now),DATEADD(minute,-62,@now),@waiter FROM dbo.MenuItems WHERE Id=@m2; SET @i8=SCOPE_IDENTITY();
DECLARE @invoice bigint,@price8 decimal(18,0)=(SELECT Price FROM dbo.MenuItems WHERE Id=@m2);
INSERT dbo.Invoices(SessionId,ShiftId,InvoiceNumber,BusinessDate,RequestId,RestaurantName,RestaurantAddress,RestaurantPhone,TableLabels,GuestCount,SessionOpenedAt,IssuedAt,IssuedBy,Subtotal)
SELECT @s8,@shift,CONCAT('LIVE-',CONVERT(char(8),DATEADD(hour,7,@now),112),'-S4D08'),CONVERT(date,DATEADD(hour,7,@now)),NEWID(),Name,Address,Phone,N'S4D08',2,DATEADD(minute,-85,@now),DATEADD(minute,-8,@now),@cashier,@price8*2 FROM dbo.RestaurantSettings WHERE Id=1; SET @invoice=SCOPE_IDENTITY();
INSERT dbo.InvoiceLines(InvoiceId,OrderItemId,MenuItemId,ItemName,Unit,Quantity,UnitPrice,OriginalTableCode,IsChargedCancellation)
SELECT @invoice,@i8,Id,Name,Unit,2,Price,N'S4D08',0 FROM dbo.MenuItems WHERE Id=@m2;
INSERT dbo.Payments(InvoiceId,Method,Amount,CashReceived,ReceivedBy,ReceivedAt) VALUES(@invoice,'Cash',@price8*2,@price8*2+50000,@cashier,DATEADD(minute,-8,@now));
UPDATE dbo.DiningTables SET Status='Cleaning',StatusChangedAt=DATEADD(minute,-8,@now) WHERE Id=@t8;

-- Booking examples: confirmed, pending, cancelled history and no-show history.
-- Confirmed bookings reserve their table on the map only in the final 30 minutes,
-- so the future dates below remain available for repeated, multi-day testing.
-- No email address is used, so seeding cannot send mail to a real customer.
INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,TableId,StartsAt,EndsAt,Status,Notes,ConfirmedAt,ConfirmedBy)
VALUES('L4A009',N'Khách demo đã xác nhận','0909000009',NULL,3,@area,@t9,DATEADD(day,7,@now),DATEADD(day,7,DATEADD(minute,90,@now)),'Confirmed',N'Lịch xác nhận còn hiệu lực để test nhiều ngày',@now,@waiter);
INSERT dbo.ReservationEvents(ReservationId,ToStatus,NewTableId,ActorUserId,Reason)
SELECT Id,'Confirmed',@t9,@waiter,N'Dữ liệu demo S4 live' FROM dbo.Reservations WHERE Code='L4A009';
UPDATE dbo.DiningTables SET Status='Available',StatusChangedAt=@now WHERE Id=@t9;
INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,StartsAt,EndsAt,Status,Notes)
VALUES('L4A010',N'Khách demo chờ xác nhận','0909000010',NULL,4,@area,DATEADD(day,8,@now),DATEADD(day,8,DATEADD(minute,90,@now)),'Pending',N'Dùng để thực hành xác nhận hoặc từ chối đặt bàn trong nhiều ngày');
INSERT dbo.ReservationEvents(ReservationId,ToStatus,ActorUserId,Reason)
SELECT Id,'Pending',NULL,N'Dữ liệu demo S4 live' FROM dbo.Reservations WHERE Code='L4A010';
INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,StartsAt,EndsAt,Status,Notes,CancelledAt,CancelReason)
VALUES('L4A011',N'Khách demo đã huỷ','0909000011',NULL,2,@area,DATEADD(day,-1,@now),DATEADD(day,-1,DATEADD(minute,90,@now)),'Cancelled',N'Lịch sử huỷ để tra cứu',DATEADD(day,-1,@now),N'Khách thay đổi lịch');
INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,PreferredAreaId,StartsAt,EndsAt,Status,Notes,NoShowAt)
VALUES('L4A012',N'Khách demo không tới','0909000012',NULL,2,@area,DATEADD(day,-2,@now),DATEADD(day,-2,DATEADD(minute,90,@now)),'NoShow',N'Lịch sử không tới để báo cáo',DATEADD(day,-2,DATEADD(minute,20,@now)));

INSERT dbo.AuditLogs(ActorUserId,Action,EntityType,EntityId,Reason,NewValues)
VALUES(@manager,'SeedS4LiveDemo','Demo',N'S4','Nạp bổ sung dữ liệu diễn tập S4',N'{"additive":true}');

PRINT N'S4 live data created: S4D04/S4D05 merge, S4D06 kitchen/cancellation, S4D07 payment, S4D08 cleaning and S4D09/L4A010 reservations.';
