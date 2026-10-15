-- S3-05 Task 2: Quản lý xem lại các lần huỷ món trong báo cáo cuối ca.
--
-- Quy tắc (đề xuất, cần PO chốt — docs/S3-05-Task2.md):
--  * Ca của một lần huỷ = ca của phiên phục vụ chứa dòng món (dbo.DiningSessions.ShiftId). Không chốt được ca khi còn bàn
--    chưa thanh toán (usp_CloseShift 51050) nên mọi lần huỷ đều xảy ra trong lúc chính ca đó đang mở.
--  * "Mọi lần huỷ" gồm: huỷ THÀNH CÔNG (dbo.OrderItemEvents ToStatus='Cancelled', mỗi lần huỷ đúng một dòng) và yêu cầu huỷ
--    BỊ TỪ CHỐI / THẤT BẠI (bảng mới dbo.OrderCancelRejections: thiếu/sai lý do, bếp đã bắt đầu, dòng của hoá đơn khác,
--    hệ thống bận…). Bấm xác nhận lặp lại cho một dòng đã huỷ KHÔNG ghi thêm (không trùng).
--  * Chỉ Quản lý xem báo cáo: web [Authorize(Manager)] và database kiểm tra quyền Audit.Read (chỉ Quản lý có).
-- Chạy lại an toàn.

IF OBJECT_ID(N'dbo.OrderCancelRejections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrderCancelRejections (
        Id bigint IDENTITY CONSTRAINT PK_OrderCancelRejections PRIMARY KEY,
        ShiftId bigint NULL CONSTRAINT FK_OrderCancelRejections_Shift REFERENCES dbo.Shifts(Id),
        SessionId bigint NULL,          -- hoá đơn đang mở trên màn hình khi gửi yêu cầu
        OrderItemId bigint NULL,        -- dòng món được yêu cầu huỷ (có thể không tồn tại nếu yêu cầu bị giả mạo)
        ItemStatus varchar(20) NULL,    -- trạng thái dòng món lúc bị từ chối
        ActorUserId int NOT NULL CONSTRAINT FK_OrderCancelRejections_Actor REFERENCES dbo.Users(Id),
        Reason nvarchar(60) NULL,       -- lý do nhân viên gửi (có thể trống / sai)
        ErrorNumber int NOT NULL,
        Message nvarchar(400) NOT NULL,
        OccurredAt datetime2(3) NOT NULL CONSTRAINT DF_OrderCancelRejections_OccurredAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_OrderCancelRejections_Shift ON dbo.OrderCancelRejections(ShiftId, OccurredAt);
END;
GO

-- Nhật ký chỉ được ghi thêm, không sửa / xoá.
CREATE OR ALTER TRIGGER dbo.tr_OrderCancelRejections_AppendOnly ON dbo.OrderCancelRejections INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51140, N'Nhật ký huỷ món chỉ được ghi thêm, không sửa hoặc xoá.', 1;
END;
GO

-- Ghi một yêu cầu huỷ bị từ chối / thất bại (gọi từ web sau khi usp_CancelOrderItem từ chối, hoặc web tự chặn trước).
CREATE OR ALTER PROCEDURE dbo.usp_LogOrderCancelRejection
    @OrderItemId bigint = NULL,
    @SessionId bigint = NULL,
    @ActorUserId int,
    @Reason nvarchar(60) = NULL,
    @ErrorNumber int,
    @Message nvarchar(400)
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.usp_RequirePermission @ActorUserId, 'Orders.Manage';
    DECLARE @shift bigint, @status varchar(20);
    SELECT @shift = s.ShiftId, @status = i.Status
    FROM dbo.OrderItems i JOIN dbo.OrderBatches b ON b.Id = i.BatchId JOIN dbo.DiningSessions s ON s.Id = b.SessionId
    WHERE i.Id = @OrderItemId;
    IF @shift IS NULL SET @shift = (SELECT ShiftId FROM dbo.DiningSessions WHERE Id = @SessionId);
    IF @shift IS NULL SET @shift = (SELECT TOP(1) Id FROM dbo.Shifts WHERE Status = 'Open' ORDER BY OpenedAt DESC);
    INSERT dbo.OrderCancelRejections(ShiftId, SessionId, OrderItemId, ItemStatus, ActorUserId, Reason, ErrorNumber, Message)
    VALUES(@shift, @SessionId, @OrderItemId, @status, @ActorUserId, NULLIF(LEFT(LTRIM(RTRIM(@Reason)), 60), N''), @ErrorNumber, LEFT(@Message, 400));
END;
GO

-- Báo cáo huỷ món của một ca: (1) thông tin ca; (2) mọi lần huỷ thành công và bị từ chối, theo thời gian.
CREATE OR ALTER PROCEDURE dbo.usp_ShiftCancellationReport
    @ShiftId bigint,
    @ActorUserId int
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.usp_RequirePermission @ActorUserId, 'Audit.Read';
    IF NOT EXISTS(SELECT 1 FROM dbo.Shifts WHERE Id = @ShiftId)
        THROW 51141, N'Không tìm thấy ca này.', 1;

    SELECT s.Id, s.Name, s.BusinessDate, s.OpenedAt, s.ClosedAt, s.Status
    FROM dbo.Shifts s WHERE s.Id = @ShiftId;

    SELECT 'Cancelled' AS Kind, e.OccurredAt, COALESCE(se.BillingSessionId, se.Id) AS BillSessionId, t.Code AS TableCode,
           b.BatchNumber, i.Id AS OrderItemId, i.ItemName, i.Quantity, i.Unit, i.LineTotal,
           e.FromStatus AS ItemStatus, i.ChargeWhenCancelled, i.CancelReason AS Reason, i.CancellationNote AS Note,
           u.FullName AS ActorName, r.Code AS ActorRole, CAST(NULL AS nvarchar(400)) AS Message, e.Id AS EntryId
    FROM dbo.OrderItemEvents e
    JOIN dbo.OrderItems i ON i.Id = e.OrderItemId
    JOIN dbo.OrderBatches b ON b.Id = i.BatchId
    JOIN dbo.DiningSessions se ON se.Id = b.SessionId
    JOIN dbo.DiningTables t ON t.Id = i.OriginalTableId
    LEFT JOIN dbo.Users u ON u.Id = e.ActorUserId
    LEFT JOIN dbo.Roles r ON r.Id = u.RoleId
    WHERE e.ToStatus = 'Cancelled' AND se.ShiftId = @ShiftId
    UNION ALL
    SELECT 'Rejected', x.OccurredAt, COALESCE(se.BillingSessionId, se.Id, x.SessionId), t.Code,
           b.BatchNumber, x.OrderItemId, i.ItemName, i.Quantity, i.Unit, i.LineTotal,
           x.ItemStatus, CAST(NULL AS bit), x.Reason, CAST(NULL AS nvarchar(500)),
           u.FullName, r.Code, x.Message, x.Id
    FROM dbo.OrderCancelRejections x
    LEFT JOIN dbo.OrderItems i ON i.Id = x.OrderItemId
    LEFT JOIN dbo.OrderBatches b ON b.Id = i.BatchId
    LEFT JOIN dbo.DiningSessions se ON se.Id = b.SessionId
    LEFT JOIN dbo.DiningTables t ON t.Id = i.OriginalTableId
    LEFT JOIN dbo.Users u ON u.Id = x.ActorUserId
    LEFT JOIN dbo.Roles r ON r.Id = u.RoleId
    WHERE x.ShiftId = @ShiftId
    ORDER BY OccurredAt, Kind, EntryId;
END;
GO

GRANT EXECUTE ON dbo.usp_LogOrderCancelRejection TO restaurant_app;
GRANT EXECUTE ON dbo.usp_ShiftCancellationReport TO restaurant_app;
GO
