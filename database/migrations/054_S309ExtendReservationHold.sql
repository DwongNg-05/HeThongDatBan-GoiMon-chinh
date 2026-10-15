-- S3-09 Task 2: phục vụ gia hạn giữ bàn thêm 15 phút, tối đa MỘT lần cho mỗi lượt đặt.
--
-- Quy tắc (đề xuất, cần PO chốt — docs/S3-09-Task2.md):
--  * Thời hạn mới = THỜI ĐIỂM NHÂN VIÊN BẤM GIA HẠN + 15 phút (không phải giờ hẹn + 30 phút), để lượt được xử lý muộn
--    (ví dụ trễ 25 phút mới bấm) vẫn được giữ đủ 15 phút. Muốn đổi sang "giờ hẹn + 30 phút": sửa đúng một dòng
--    "SET @holdUntil = ..." trong usp_ExtendLateReservationHold (bằng một migration mới).
--  * Chỉ gia hạn được khi lượt đang có cảnh báo trễ (dbo.fn_LateReservationAlerts) và chưa gia hạn lần nào (ExtensionCount = 0).
--    Lượt "Chờ xác nhận" / "Đã xác nhận" chưa tới mới có cảnh báo; khách đã tới, đã huỷ, bị từ chối, đã "không tới": từ chối.
--  * Trong thời gian gia hạn: không cảnh báo (fn_ReservationWarnAt lấy HoldExtendedUntil làm mốc, có sẵn từ migration 049),
--    hai màn hình hiển thị thời hạn mới (dbo.fn_ExtendedReservationHolds). Bàn vẫn "Đã đặt trước".
--  * Hết thời hạn mà khách chưa tới: cảnh báo tự hiện lại, chỉ còn thao tác "Đánh dấu khách không tới"
--    (usp_MarkLateReservationNoShow của 049 chạy như cũ: giải phóng bàn, ghi lịch sử số điện thoại đúng một lần).
--  * Chống gia hạn lần hai: khoá thao tác chung (usp_LockOperations) + kiểm tra ExtensionCount trong cùng giao dịch
--    + điều kiện ExtensionCount = 0 trong câu UPDATE + CHECK(ExtensionCount BETWEEN 0 AND 1) có sẵn ở bảng.
--    Gửi lặp / nhiều nhân viên cùng bấm: đúng một lần thành công, các lần sau nhận lỗi 51125.
-- Thêm cột ExtensionCount, HoldExtendedUntil (đã có từ 001) vào hai hàm cảnh báo, ở CUỐI danh sách cột để mã đọc theo thứ tự cũ vẫn đúng.
-- Chạy lại an toàn.

CREATE OR ALTER FUNCTION dbo.fn_ReservationWarnAt()
RETURNS TABLE
AS
RETURN
    SELECT r.Id, r.Code, r.CustomerName, r.Phone, r.GuestCount, r.TableId, r.StartsAt, r.EndsAt, r.Status,
           COALESCE(r.HoldExtendedUntil, DATEADD(minute, s.NoShowGraceMinutes, r.StartsAt)) AS WarnAt,
           r.ExtensionCount, r.HoldExtendedUntil
    FROM dbo.Reservations r
    CROSS JOIN dbo.RestaurantSettings s
    WHERE s.Id = 1 AND r.Status IN ('Pending', 'Confirmed') AND r.ArrivedAt IS NULL;
GO

CREATE OR ALTER FUNCTION dbo.fn_LateReservationAlerts(@NowUtc datetime2(3))
RETURNS TABLE
AS
RETURN
    SELECT w.Id, w.Code, w.CustomerName, w.Phone, w.GuestCount, w.TableId, t.Code AS TableCode, w.StartsAt, w.EndsAt,
           w.Status, w.WarnAt, DATEDIFF(second, w.StartsAt, @NowUtc) / 60 AS LateMinutes,
           w.ExtensionCount, w.HoldExtendedUntil
    FROM dbo.fn_ReservationWarnAt() w
    LEFT JOIN dbo.DiningTables t ON t.Id = w.TableId
    WHERE w.WarnAt <= @NowUtc AND w.StartsAt > DATEADD(hour, -12, @NowUtc);
GO

-- Lượt đang trong thời gian gia hạn giữ bàn (đã gia hạn, chưa hết hạn, khách chưa tới): không cảnh báo, hiển thị thời hạn mới.
CREATE OR ALTER FUNCTION dbo.fn_ExtendedReservationHolds(@NowUtc datetime2(3))
RETURNS TABLE
AS
RETURN
    SELECT w.Id, w.Code, w.CustomerName, w.Phone, w.GuestCount, w.TableId, t.Code AS TableCode, w.StartsAt,
           w.HoldExtendedUntil, w.Status, w.ExtensionCount
    FROM dbo.fn_ReservationWarnAt() w
    LEFT JOIN dbo.DiningTables t ON t.Id = w.TableId
    WHERE w.ExtensionCount >= 1 AND w.HoldExtendedUntil > @NowUtc AND w.StartsAt > DATEADD(hour, -12, @NowUtc);
GO

CREATE OR ALTER PROCEDURE dbo.usp_ExtendLateReservationHold
    @ReservationId bigint,
    @ActorUserId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- Khoá thao tác chung: hai nhân viên (hoặc hai lần bấm) cùng gửi sẽ chạy lần lượt, lần sau thấy ExtensionCount = 1.
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Reservations.Manage';

        DECLARE @now datetime2(3) = SYSUTCDATETIME();
        DECLARE @status varchar(20), @tableId int, @code char(6), @extensions int, @heldUntil datetime2(3), @holdUntil datetime2(3);
        SELECT @status = Status, @tableId = TableId, @code = Code, @extensions = ExtensionCount, @heldUntil = HoldExtendedUntil
        FROM dbo.Reservations WITH (UPDLOCK) WHERE Id = @ReservationId;

        IF @status IS NULL THROW 51120, N'Không tìm thấy lượt đặt bàn.', 1;
        IF @status = 'Arrived'
            THROW 51121, N'Khách đã tới nên không cần gia hạn giữ bàn.', 1;
        IF @status = 'NoShow'
            THROW 51124, N'Lượt đặt đã được đánh dấu khách không tới nên không gia hạn được.', 1;
        IF @status NOT IN ('Pending', 'Confirmed')
            THROW 51122, N'Lượt đặt này đã huỷ hoặc bị từ chối, không gia hạn được.', 1;
        IF @extensions >= 1
        BEGIN
            DECLARE @extendedMessage nvarchar(400) = CONCAT(N'Lượt đặt này đã được gia hạn một lần',
                CASE WHEN @heldUntil IS NULL THEN N'' ELSE CONCAT(N' (tới ', FORMAT(DATEADD(hour, 7, @heldUntil), 'HH:mm'), N')') END,
                N', không gia hạn thêm được. Nếu khách vẫn chưa tới, hãy đánh dấu khách không tới.');
            THROW 51125, @extendedMessage, 1;
        END;
        IF NOT EXISTS(SELECT 1 FROM dbo.fn_LateReservationAlerts(@now) WHERE Id = @ReservationId)
            THROW 51123, N'Lượt đặt chưa trễ tới mốc cảnh báo nên chưa cần gia hạn giữ bàn.', 1;

        -- Quy tắc thời hạn mới (cần PO chốt): thời điểm bấm + 15 phút.
        SET @holdUntil = DATEADD(minute, 15, @now);

        UPDATE dbo.Reservations SET ExtensionCount = ExtensionCount + 1, HoldExtendedUntil = @holdUntil
        WHERE Id = @ReservationId AND Status = @status AND ArrivedAt IS NULL AND ExtensionCount = 0;
        IF @@ROWCOUNT <> 1
            THROW 51125, N'Lượt đặt này đã được gia hạn một lần, không gia hạn thêm được.', 1;

        INSERT dbo.ReservationEvents(ReservationId, FromStatus, ToStatus, OldTableId, NewTableId, ActorUserId, Reason)
        VALUES(@ReservationId, @status, @status, @tableId, @tableId, @ActorUserId,
               CONCAT(N'Gia hạn giữ bàn 15 phút, tới ', FORMAT(DATEADD(hour, 7, @holdUntil), 'HH:mm dd/MM/yyyy'), N' (giờ Việt Nam)'));

        SELECT 'Extended' AS Outcome, @code AS ReservationCode, t.Code AS TableCode, @holdUntil AS HoldExtendedUntil,
               1 AS ExtensionCount
        FROM (SELECT 1 AS One) x
        LEFT JOIN dbo.DiningTables t ON t.Id = @tableId;
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON dbo.usp_ExtendLateReservationHold TO restaurant_app;
GRANT SELECT ON dbo.fn_ExtendedReservationHolds TO restaurant_app;
GRANT SELECT ON dbo.fn_LateReservationAlerts TO restaurant_app;
GRANT SELECT ON dbo.fn_ReservationWarnAt TO restaurant_app;
GO
