-- Additive S4 rehearsal data.  It never deletes or changes existing business rows.
-- The three invoices make the S4 dashboard and menu-performance report useful
-- immediately after a clean development demo database has menu data.
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.Invoices WHERE InvoiceNumber LIKE 'S4D-%')
BEGIN
    PRINT N'S4 demo data already exists; existing data preserved.';
    RETURN;
END;

DECLARE @manager int = (
    SELECT TOP (1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id = u.RoleId
    WHERE r.Name IN (N'Manager', N'Quản lý') AND u.IsActive = 1 ORDER BY u.Id);
DECLARE @waiter int = (
    SELECT TOP (1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id = u.RoleId
    WHERE r.Name IN (N'Waiter', N'Phục vụ') AND u.IsActive = 1 ORDER BY u.Id);
DECLARE @cashier int = (
    SELECT TOP (1) u.Id FROM dbo.Users u JOIN dbo.Roles r ON r.Id = u.RoleId
    WHERE r.Name IN (N'Cashier', N'Thu ngân') AND u.IsActive = 1 ORDER BY u.Id);

IF @manager IS NULL OR @waiter IS NULL OR @cashier IS NULL
    THROW 51270, N'Cần có tài khoản Quản lý, Phục vụ và Thu ngân đang hoạt động trước khi nạp dữ liệu S4.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.Areas WHERE NormalizedName = N'DỮ LIỆU DEMO S4')
    INSERT dbo.Areas(Name, SortOrder, Notes) VALUES (N'Dữ liệu demo S4', 999, N'Bàn riêng cho dữ liệu diễn tập, có thể giữ lại an toàn.');

DECLARE @areaId int = (SELECT Id FROM dbo.Areas WHERE NormalizedName = N'DỮ LIỆU DEMO S4');
IF NOT EXISTS (SELECT 1 FROM dbo.DiningTables WHERE Code = 'S4D01')
    INSERT dbo.DiningTables(AreaId, Code, MinCapacity, MaxCapacity, SortOrder) VALUES (@areaId, 'S4D01', 1, 4, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.DiningTables WHERE Code = 'S4D02')
    INSERT dbo.DiningTables(AreaId, Code, MinCapacity, MaxCapacity, SortOrder) VALUES (@areaId, 'S4D02', 1, 4, 2);
IF NOT EXISTS (SELECT 1 FROM dbo.DiningTables WHERE Code = 'S4D03')
    INSERT dbo.DiningTables(AreaId, Code, MinCapacity, MaxCapacity, SortOrder) VALUES (@areaId, 'S4D03', 1, 6, 3);

DECLARE @menuOne int = (SELECT TOP (1) Id FROM dbo.MenuItems WHERE IsActive = 1 AND IsSoldOut = 0 ORDER BY Id);
DECLARE @menuTwo int = (SELECT TOP (1) Id FROM dbo.MenuItems WHERE IsActive = 1 AND IsSoldOut = 0 AND Id <> @menuOne ORDER BY Price DESC, Id);
DECLARE @menuThree int = (SELECT TOP (1) Id FROM dbo.MenuItems WHERE IsActive = 1 AND IsSoldOut = 0 AND Id NOT IN (@menuOne, @menuTwo) ORDER BY Price, Id);
IF @menuOne IS NULL OR @menuTwo IS NULL OR @menuThree IS NULL
    THROW 51271, N'Cần ít nhất ba món đang bán trước khi nạp dữ liệu S4.', 1;

DECLARE @nameOne nvarchar(150), @unitOne nvarchar(30), @priceOne decimal(18,0),
        @nameTwo nvarchar(150), @unitTwo nvarchar(30), @priceTwo decimal(18,0),
        @nameThree nvarchar(150), @unitThree nvarchar(30), @priceThree decimal(18,0);
SELECT @nameOne = Name, @unitOne = Unit, @priceOne = Price FROM dbo.MenuItems WHERE Id = @menuOne;
SELECT @nameTwo = Name, @unitTwo = Unit, @priceTwo = Price FROM dbo.MenuItems WHERE Id = @menuTwo;
SELECT @nameThree = Name, @unitThree = Unit, @priceThree = Price FROM dbo.MenuItems WHERE Id = @menuThree;

DECLARE @today date = CONVERT(date, DATEADD(hour, 7, SYSUTCDATETIME()));
DECLARE @number tinyint = 1;
WHILE @number <= 3
BEGIN
    DECLARE @businessDate date = DATEADD(day, @number - 3, @today);
    DECLARE @tableId int = CASE @number WHEN 1 THEN (SELECT Id FROM dbo.DiningTables WHERE Code = 'S4D01')
                                        WHEN 2 THEN (SELECT Id FROM dbo.DiningTables WHERE Code = 'S4D02')
                                        ELSE (SELECT Id FROM dbo.DiningTables WHERE Code = 'S4D03') END;
    DECLARE @tableCode varchar(20) = CASE @number WHEN 1 THEN 'S4D01' WHEN 2 THEN 'S4D02' ELSE 'S4D03' END;
    DECLARE @openedAt datetime2(3) = DATEADD(hour, 4, CONVERT(datetime2(3), @businessDate));
    DECLARE @closedAt datetime2(3) = DATEADD(minute, 85, @openedAt);
    DECLARE @shiftId bigint, @sessionId bigint, @batchId bigint, @servedOne bigint, @servedTwo bigint, @cancelled bigint;
    DECLARE @chargeCancelled bit = CASE WHEN @number = 1 THEN 1 ELSE 0 END;
    DECLARE @discount decimal(18,0) = CASE WHEN @number = 2 THEN 10000 ELSE 0 END;
    DECLARE @subtotal decimal(18,0) = 2 * @priceOne + @priceTwo + CASE WHEN @chargeCancelled = 1 THEN @priceThree ELSE 0 END;
    DECLARE @total decimal(18,0) = 2 * @priceOne + @priceTwo + CASE WHEN @chargeCancelled = 1 THEN @priceThree ELSE 0 END - @discount;
    DECLARE @method varchar(10) = CASE WHEN @number = 2 THEN 'Transfer' ELSE 'Cash' END;
    DECLARE @invoiceId bigint;
    DECLARE @invoiceNumber varchar(30) = CONCAT('S4D-', CONVERT(char(8), @businessDate, 112), '-', @number);

    INSERT dbo.Shifts(Name, BusinessDate, OpenedBy, OpenedAt, OpeningCash, Status, ClosedBy, ClosedAt,
        CountedCash, ExpectedCash, Explanation, InvoiceCount, Revenue, CashRevenue, TransferRevenue, DiscountTotal, ServedTableCount)
    VALUES (CONCAT(N'Ca diễn tập S4 ', @number), @businessDate, @manager, @openedAt, 500000, 'Closed', @manager, @closedAt,
        CASE WHEN @method = 'Cash' THEN 500000 + @total ELSE 500000 END,
        CASE WHEN @method = 'Cash' THEN 500000 + @total ELSE 500000 END, N'Dữ liệu diễn tập S4', 1, @total,
        CASE WHEN @method = 'Cash' THEN @total ELSE 0 END, CASE WHEN @method = 'Transfer' THEN @total ELSE 0 END,
        @discount, 1);
    SET @shiftId = SCOPE_IDENTITY();

    INSERT dbo.DiningSessions(ShiftId, GuestCount, Status, OpenedBy, OpenedAt, ClosedAt, ClosedBy)
    VALUES (@shiftId, @number + 1, 'Closed', @waiter, @openedAt, @closedAt, @cashier);
    SET @sessionId = SCOPE_IDENTITY();
    INSERT dbo.SessionTables(SessionId, TableId, AssignedAt, ReleasedAt) VALUES (@sessionId, @tableId, @openedAt, @closedAt);
    INSERT dbo.OrderBatches(SessionId, OriginSessionId, BatchNumber, RequestId, CreatedBy, CreatedAt)
    VALUES (@sessionId, @sessionId, 1, NEWID(), @waiter, DATEADD(minute, 10, @openedAt));
    SET @batchId = SCOPE_IDENTITY();

    INSERT dbo.OrderItems(BatchId, MenuItemId, OriginalTableId, ItemName, Unit, UnitPrice, Quantity, EstimatedPrepMinutes,
        Status, SubmittedAt, PreparingAt, ReadyAt, ServedAt, ServedBy)
    VALUES (@batchId, @menuOne, @tableId, @nameOne, @unitOne, @priceOne, 2, 12, 'Served', DATEADD(minute, 10, @openedAt),
        DATEADD(minute, 12, @openedAt), DATEADD(minute, 25, @openedAt), DATEADD(minute, 35, @openedAt), @waiter);
    SET @servedOne = SCOPE_IDENTITY();
    INSERT dbo.OrderItems(BatchId, MenuItemId, OriginalTableId, ItemName, Unit, UnitPrice, Quantity, EstimatedPrepMinutes,
        Status, SubmittedAt, PreparingAt, ReadyAt, ServedAt, ServedBy)
    VALUES (@batchId, @menuTwo, @tableId, @nameTwo, @unitTwo, @priceTwo, 1, 15, 'Served', DATEADD(minute, 11, @openedAt),
        DATEADD(minute, 14, @openedAt), DATEADD(minute, 29, @openedAt), DATEADD(minute, 40, @openedAt), @waiter);
    SET @servedTwo = SCOPE_IDENTITY();
    INSERT dbo.OrderItems(BatchId, MenuItemId, OriginalTableId, ItemName, Unit, UnitPrice, Quantity, EstimatedPrepMinutes,
        Status, SubmittedAt, PreparingAt, CancelledAt, CancelledBy, CancelReason, CancellationNote, ChargeWhenCancelled)
    VALUES (@batchId, @menuThree, @tableId, @nameThree, @unitThree, @priceThree, 1, 10, 'Cancelled', DATEADD(minute, 12, @openedAt),
        DATEADD(minute, 14, @openedAt), DATEADD(minute, 18, @openedAt), @manager,
        CASE WHEN @number = 3 THEN 'SoldOut' ELSE 'ChangedMind' END, N'Dòng món diễn tập huỷ', @chargeCancelled);
    SET @cancelled = SCOPE_IDENTITY();

    INSERT dbo.OrderItemEvents(OrderItemId, FromStatus, ToStatus, ActorUserId, OccurredAt) VALUES
        (@servedOne, 'Ready', 'Served', @waiter, DATEADD(minute, 35, @openedAt)),
        (@servedTwo, 'Ready', 'Served', @waiter, DATEADD(minute, 40, @openedAt)),
        (@cancelled, 'Preparing', 'Cancelled', @manager, DATEADD(minute, 18, @openedAt));

    INSERT dbo.Invoices(SessionId, ShiftId, InvoiceNumber, BusinessDate, RequestId, RestaurantName, RestaurantAddress,
        RestaurantPhone, TableLabels, GuestCount, SessionOpenedAt, IssuedAt, IssuedBy, Subtotal,
        DiscountType, DiscountValue, DiscountAmount, DiscountReason)
    VALUES (@sessionId, @shiftId, @invoiceNumber, @businessDate, NEWID(), N'Bếp Nhà', N'Dữ liệu diễn tập S4', '0900000000',
        @tableCode, @number + 1, @openedAt, @closedAt, @cashier, @subtotal,
        CASE WHEN @discount > 0 THEN 'Amount' ELSE 'None' END, @discount, @discount,
        CASE WHEN @discount > 0 THEN N'Ưu đãi khách quen (demo)' ELSE NULL END);
    SET @invoiceId = SCOPE_IDENTITY();
    INSERT dbo.InvoiceLines(InvoiceId, OrderItemId, MenuItemId, ItemName, Unit, Quantity, UnitPrice, OriginalTableCode, IsChargedCancellation)
    VALUES (@invoiceId, @servedOne, @menuOne, @nameOne, @unitOne, 2, @priceOne, @tableCode, 0),
           (@invoiceId, @servedTwo, @menuTwo, @nameTwo, @unitTwo, 1, @priceTwo, @tableCode, 0);
    IF @chargeCancelled = 1
        INSERT dbo.InvoiceLines(InvoiceId, OrderItemId, MenuItemId, ItemName, Unit, Quantity, UnitPrice, OriginalTableCode, IsChargedCancellation)
        VALUES (@invoiceId, @cancelled, @menuThree, @nameThree, @unitThree, 1, @priceThree, @tableCode, 1);
    INSERT dbo.Payments(InvoiceId, Method, Amount, CashReceived, TransferReference, ReceivedBy, ReceivedAt)
    VALUES (@invoiceId, @method, @total,
        CASE WHEN @method = 'Cash' THEN @total + 10000 ELSE NULL END,
        CASE WHEN @method = 'Transfer' THEN CONCAT('S4D-', @number, '-CK') ELSE NULL END,
        @cashier, @closedAt);
    INSERT dbo.AuditLogs(ActorUserId, Action, EntityType, EntityId, Reason, OccurredAt)
    VALUES (@cashier, 'Checkout', 'Invoice', CONVERT(varchar(60), @invoiceId), N'Dữ liệu diễn tập S4', @closedAt);

    UPDATE dbo.DiningTables SET Status = 'Available', StatusChangedAt = @closedAt WHERE Id = @tableId;
    SET @number += 1;
END;

PRINT N'S4 demo data created: three closed sessions, invoices, payment methods and cancellation history.';
