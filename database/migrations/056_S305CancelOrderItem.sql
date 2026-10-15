-- S3-05 Task 1: Phục vụ huỷ dòng món đang chờ bếp với lý do bắt buộc; hàng đợi bếp và tạm tính cập nhật theo.
--
-- Quy tắc (đề xuất, cần PO chốt — docs/S3-05-Task1.md):
--  * Huỷ NGUYÊN DÒNG món (toàn bộ số lượng trên dòng), không huỷ một phần. Muốn bớt số lượng: huỷ dòng rồi gọi lại số mới.
--  * Huỷ khi món còn "Chờ bếp" (Pending): KHÔNG tính tiền (ChargeWhenCancelled = 0). Quản lý huỷ sau khi bếp đã bắt đầu
--    (Preparing / Ready / Served): VẪN tính tiền (ChargeWhenCancelled = 1) — giữ nguyên quy tắc của migration 002.
--  * Phục vụ chỉ huỷ được món còn chờ bếp, với một trong ba lý do: khách đổi ý (ChangedMind), gọi nhầm (Mistake),
--    hết nguyên liệu (SoldOut). Lý do "Quản lý yêu cầu" (ManagerOverride) chỉ dành cho Quản lý.
--
-- Thay đổi so với bản cũ (002_BusinessOperations.sql); lời gọi cũ (không có tham số mới) vẫn chạy:
--  * @SessionId (tuỳ chọn): hoá đơn đang mở trên màn hình. Dòng món phải thuộc hoá đơn đó (phiên gốc hoặc phiên gộp vào), nếu không: 51033.
--  * @ExpectedStatus (tuỳ chọn): trạng thái dòng món lúc nhân viên mở hộp xác nhận huỷ. Bếp đổi trạng thái trong lúc nhân viên
--    chọn lý do → từ chối (51034) để nhân viên xem lại; không huỷ, không tính tiền ngoài ý muốn.
--  * Phục vụ huỷ món bếp đã bắt đầu: 51035 kèm thông báo rõ ràng (bản cũ báo lỗi quyền chung 51001).
--  * Phục vụ dùng lý do "Quản lý yêu cầu": 51036. Thiếu / sai lý do: 51032. Các kiểm tra này chạy trước mọi thay đổi.
--  * Gửi lặp hoặc hai người cùng huỷ: lần sau KHÔNG ghi thêm, không trừ tiền thêm, trả Outcome = 'AlreadyCancelled'
--    (bản cũ báo lỗi 51031). Khoá thao tác chung (usp_LockOperations) xếp các yêu cầu đồng thời lần lượt; bếp chuyển trạng thái
--    (usp_TransitionOrderItem) dùng cùng khoá, nên "bếp bắt đầu nấu" và "phục vụ huỷ" của cùng một món không bao giờ cùng thành công.
--  * Nhật ký: mỗi lần huỷ thành công ghi đúng một dòng dbo.OrderItemEvents (dòng món, người thực hiện, thời điểm, lý do + ghi chú)
--    và lưu CancelledBy / CancelledAt / CancelReason / CancellationNote trên dòng món.
--  * Trả về một dòng kết quả: Outcome, dòng món, số tiền của dòng, có còn tính tiền không, tạm tính mới của cả hoá đơn.
-- Chạy lại an toàn.

CREATE OR ALTER PROCEDURE dbo.usp_CancelOrderItem
    @OrderItemId bigint,
    @Reason varchar(30),
    @ActorUserId int,
    @Note nvarchar(500) = NULL,
    @SessionId bigint = NULL,
    @ExpectedStatus varchar(20) = NULL
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
            SET @outcome = 'AlreadyCancelled';   -- gửi lặp / người khác vừa huỷ: không ghi thêm, không trừ tiền thêm
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
                    CASE WHEN @canCancelPrepared = 1
                        THEN N' Nếu huỷ bây giờ món vẫn tính tiền — hãy mở lại hộp huỷ để xác nhận.'
                        ELSE N' Phục vụ không huỷ được nữa; chỉ Quản lý được huỷ và món vẫn tính tiền.' END);
                THROW 51034, @message, 1;
            END;
            IF @status <> 'Pending' AND @canCancelPrepared = 0
                THROW 51035, N'Bếp đã bắt đầu chế biến món này nên Phục vụ không huỷ được. Chỉ Quản lý được huỷ và món vẫn tính tiền.', 1;

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

GRANT EXECUTE ON dbo.usp_CancelOrderItem TO restaurant_app;
GO
