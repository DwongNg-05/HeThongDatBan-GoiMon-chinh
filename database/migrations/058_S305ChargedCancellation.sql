-- S3-05 Task 3: Quản lý huỷ món đã bắt đầu chế biến — món vẫn tính tiền, bếp nhận thông báo dừng món.
--
-- Quy tắc (đề xuất, cần PO chốt — docs/S3-05-Task3.md):
--  * Sau khi bếp bắt đầu, Quản lý chỉ huỷ được món ĐANG NẤU (Preparing) hoặc ĐÃ XONG, CHỜ MANG RA (Ready).
--    Món ĐÃ MANG RA (Served) không huỷ được nữa (51143): khách đã nhận món, xử lý bằng giảm giá khi thanh toán.
--  * Huỷ có tính tiền bắt buộc một trong BA lý do: khách đổi ý (ChangedMind), gọi nhầm (Mistake), hết nguyên liệu (SoldOut).
--    "Quản lý yêu cầu" (ManagerOverride) không dùng cho huỷ có tính tiền (51142); vẫn dùng được khi huỷ món còn chờ bếp (Task 1).
--  * Quản lý phải xác nhận rõ "huỷ có tính tiền" (@ConfirmCharged = 1), nếu không: 51144. Cùng với @ExpectedStatus (51034),
--    trạng thái mới nhất được kiểm tra trong cùng giao dịch có khoá với thao tác của bếp (usp_LockOperations).
--  * Dòng món giữ nguyên trên hoá đơn với ChargeWhenCancelled = 1: tạm tính, hoá đơn (InvoiceLines.IsChargedCancellation) không giảm.
--  * Bếp: món rời hàng đợi chế biến và hiện thẻ "DỪNG MÓN" ở đầu màn hình bếp (lần tải lại kế tiếp, ≤ 3 giây) cho tới khi
--    tài khoản Bếp bấm "Đã dừng món" (dbo.KitchenStopAcknowledgements, ghi một lần, bấm lặp không ghi thêm).
--  * Nhật ký: như Task 1 (dbo.OrderItemEvents, FromStatus = Preparing/Ready) và báo cáo cuối ca của Task 2 (057).
-- Chạy lại an toàn.

CREATE OR ALTER PROCEDURE dbo.usp_CancelOrderItem
    @OrderItemId bigint,
    @Reason varchar(30),
    @ActorUserId int,
    @Note nvarchar(500) = NULL,
    @SessionId bigint = NULL,
    @ExpectedStatus varchar(20) = NULL,
    @ConfirmCharged bit = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Orders.Manage';

        DECLARE @status varchar(20), @sessionStatus varchar(20), @root bigint, @outcome varchar(20), @message nvarchar(400);
        SELECT @status = i.Status, @sessionStatus = s.Status, @root = COALESCE(s.BillingSessionId, s.Id)
        FROM dbo.OrderItems i WITH (UPDLOCK)
        JOIN dbo.OrderBatches b ON b.Id = i.BatchId
        JOIN dbo.DiningSessions s ON s.Id = b.SessionId
        WHERE i.Id = @OrderItemId;

        IF @status IS NULL
            THROW 51031, N'Không tìm thấy dòng món này.', 1;
        IF @SessionId IS NOT NULL AND NOT EXISTS(
            SELECT 1 FROM dbo.DiningSessions WHERE Id = @SessionId AND COALESCE(BillingSessionId, Id) = @root)
            THROW 51033, N'Dòng món không thuộc hoá đơn đang mở. Vui lòng tải lại hoá đơn.', 1;

        SET @Note = NULLIF(LTRIM(RTRIM(@Note)), N'');
        IF @Reason IS NULL OR @Reason NOT IN ('ChangedMind', 'Mistake', 'SoldOut', 'ManagerOverride')
            THROW 51032, N'Hãy chọn lý do huỷ món: khách đổi ý, gọi nhầm hoặc hết nguyên liệu.', 1;

        DECLARE @canCancelPrepared bit = CASE WHEN EXISTS(
            SELECT 1 FROM dbo.Users u
            JOIN dbo.RolePermissions rp ON rp.RoleId = u.RoleId
            JOIN dbo.Permissions p ON p.Id = rp.PermissionId
            WHERE u.Id = @ActorUserId AND u.IsActive = 1 AND p.Code = 'Orders.CancelPrepared') THEN 1 ELSE 0 END;
        IF @Reason = 'ManagerOverride' AND @canCancelPrepared = 0
            THROW 51036, N'Lý do "Quản lý yêu cầu" chỉ dành cho Quản lý.', 1;
        IF @sessionStatus = 'Closed'
            THROW 51031, N'Bàn đã thanh toán nên không huỷ món được nữa.', 1;

        IF @status = 'Cancelled'
            SET @outcome = 'AlreadyCancelled';   -- gửi lặp / người khác vừa huỷ: không ghi thêm, không đổi tiền
        ELSE
        BEGIN
            IF @ExpectedStatus IS NOT NULL AND @ExpectedStatus <> @status
            BEGIN
                SET @message = CONCAT(
                    CASE @status
                        WHEN 'Preparing' THEN N'Bếp đã bắt đầu chế biến món này trong lúc bạn chọn lý do.'
                        WHEN 'Ready' THEN N'Bếp đã làm xong món này trong lúc bạn chọn lý do.'
                        WHEN 'Served' THEN N'Món này vừa được mang ra trong lúc bạn chọn lý do.'
                        ELSE N'Trạng thái món vừa thay đổi trong lúc bạn chọn lý do.' END,
                    CASE WHEN @status = 'Served' THEN N' Món đã mang ra cho khách nên không huỷ được nữa.'
                         WHEN @canCancelPrepared = 1
                        THEN N' Nếu huỷ bây giờ món vẫn tính tiền — hãy mở lại hộp huỷ để xác nhận.'
                        ELSE N' Phục vụ không huỷ được nữa; chỉ Quản lý được huỷ và món vẫn tính tiền.' END);
                THROW 51034, @message, 1;
            END;
            IF @status <> 'Pending' AND @canCancelPrepared = 0
                THROW 51035, N'Bếp đã bắt đầu chế biến món này nên Phục vụ không huỷ được. Chỉ Quản lý được huỷ và món vẫn tính tiền.', 1;
            IF @status = 'Served'
                THROW 51143, N'Món đã mang ra cho khách nên không huỷ được nữa. Nếu cần, hãy giảm giá khi thanh toán.', 1;
            IF @status IN ('Preparing', 'Ready') AND @Reason = 'ManagerOverride'
                THROW 51142, N'Huỷ có tính tiền cần một trong ba lý do: khách đổi ý, gọi nhầm hoặc hết nguyên liệu.', 1;
            IF @status IN ('Preparing', 'Ready') AND ISNULL(@ConfirmCharged, 0) = 0
                THROW 51144, N'Bếp đã bắt đầu món này: hãy đánh dấu xác nhận huỷ có tính tiền trước khi huỷ.', 1;

            UPDATE dbo.OrderItems
            SET Status = 'Cancelled', CancelledAt = SYSUTCDATETIME(), CancelledBy = @ActorUserId,
                CancelReason = @Reason, CancellationNote = @Note,
                ChargeWhenCancelled = CASE WHEN @status = 'Pending' THEN 0 ELSE 1 END
            WHERE Id = @OrderItemId AND Status = @status;
            INSERT dbo.OrderItemEvents(OrderItemId, FromStatus, ToStatus, ActorUserId, Reason)
            VALUES(@OrderItemId, @status, 'Cancelled', @ActorUserId,
                   CONCAT(@Reason, CASE WHEN @Note IS NULL THEN N'' ELSE CONCAT(N': ', @Note) END));
            SET @outcome = 'Cancelled';
        END;

        SELECT @outcome AS Outcome, i.Id AS OrderItemId, i.ItemName, i.Quantity, i.Unit, i.LineTotal, i.ChargeWhenCancelled,
               i.CancelReason, i.CancelledAt, u.FullName AS CancelledByName,
               (SELECT COALESCE(SUM(CASE WHEN x.Status <> 'Cancelled' OR x.ChargeWhenCancelled = 1 THEN x.LineTotal ELSE 0 END), 0)
                FROM dbo.OrderItems x
                JOIN dbo.OrderBatches xb ON xb.Id = x.BatchId
                JOIN dbo.DiningSessions xs ON xs.Id = xb.SessionId
                WHERE COALESCE(xs.BillingSessionId, xs.Id) = @root) AS BillSubtotal
        FROM dbo.OrderItems i
        LEFT JOIN dbo.Users u ON u.Id = i.CancelledBy
        WHERE i.Id = @OrderItemId;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Bếp xác nhận đã dừng món bị huỷ khi đang làm (thẻ "DỪNG MÓN" biến khỏi màn hình bếp).
IF OBJECT_ID(N'dbo.KitchenStopAcknowledgements', N'U') IS NULL
    CREATE TABLE dbo.KitchenStopAcknowledgements (
        OrderItemId bigint NOT NULL CONSTRAINT PK_KitchenStopAcknowledgements PRIMARY KEY
            CONSTRAINT FK_KitchenStopAcknowledgements_Item REFERENCES dbo.OrderItems(Id),
        AcknowledgedBy int NOT NULL CONSTRAINT FK_KitchenStopAcknowledgements_User REFERENCES dbo.Users(Id),
        AcknowledgedAt datetime2(3) NOT NULL CONSTRAINT DF_KitchenStopAcknowledgements_At DEFAULT SYSUTCDATETIME()
    );
GO

-- Món cần bếp dừng: huỷ có tính tiền từ Đang nấu / Xong trong 12 giờ gần nhất, bếp chưa bấm "Đã dừng món".
CREATE OR ALTER VIEW dbo.vw_KitchenStopNotices
AS
SELECT i.Id AS OrderItemId, t.Code AS TableCode, i.ItemName, i.Quantity, i.Unit, e.FromStatus, i.CancelReason, i.CancellationNote,
       i.CancelledAt, u.FullName AS CancelledByName, b.SessionId, b.BatchNumber
FROM dbo.OrderItems i
JOIN dbo.OrderBatches b ON b.Id = i.BatchId
JOIN dbo.DiningTables t ON t.Id = i.OriginalTableId
JOIN dbo.OrderItemEvents e ON e.OrderItemId = i.Id AND e.ToStatus = 'Cancelled'
LEFT JOIN dbo.Users u ON u.Id = i.CancelledBy
WHERE i.Status = 'Cancelled' AND i.ChargeWhenCancelled = 1 AND e.FromStatus IN ('Preparing', 'Ready')
  AND i.CancelledAt > DATEADD(hour, -12, SYSUTCDATETIME())
  AND NOT EXISTS(SELECT 1 FROM dbo.KitchenStopAcknowledgements k WHERE k.OrderItemId = i.Id);
GO

CREATE OR ALTER PROCEDURE dbo.usp_AcknowledgeKitchenStop
    @OrderItemId bigint,
    @ActorUserId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Kitchen.Manage';
        IF NOT EXISTS(
            SELECT 1 FROM dbo.OrderItems i JOIN dbo.OrderItemEvents e ON e.OrderItemId = i.Id AND e.ToStatus = 'Cancelled'
            WHERE i.Id = @OrderItemId AND i.Status = 'Cancelled' AND i.ChargeWhenCancelled = 1 AND e.FromStatus IN ('Preparing', 'Ready'))
            THROW 51145, N'Không có thông báo dừng món này.', 1;
        DECLARE @outcome varchar(20) = 'AlreadyAcknowledged';
        IF NOT EXISTS(SELECT 1 FROM dbo.KitchenStopAcknowledgements WHERE OrderItemId = @OrderItemId)
        BEGIN
            INSERT dbo.KitchenStopAcknowledgements(OrderItemId, AcknowledgedBy) VALUES(@OrderItemId, @ActorUserId);
            SET @outcome = 'Acknowledged';
        END;
        SELECT @outcome AS Outcome;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON dbo.usp_CancelOrderItem TO restaurant_app;
GRANT EXECUTE ON dbo.usp_AcknowledgeKitchenStop TO restaurant_app;
GRANT SELECT ON dbo.vw_KitchenStopNotices TO restaurant_app;
GO
