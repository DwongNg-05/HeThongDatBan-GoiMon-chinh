-- S3-09 Task 1: cảnh báo đặt bàn trễ 15 phút và đánh dấu khách không tới để giải phóng bàn.
--
-- Quy tắc (đề xuất, cần PO chốt — docs/S3-09-Task1.md):
--  * Mốc cảnh báo = giờ hẹn + RestaurantSettings.NoShowGraceMinutes (mặc định 15 phút), hoặc HoldExtendedUntil nếu lượt đã được
--    gia hạn giữ bàn. Cảnh báo bắt đầu NGAY TẠI mốc (trễ đúng 15:00 là có cảnh báo; 14:59 thì chưa).
--  * Áp dụng cho lượt "Chờ xác nhận" (Pending) và "Đã xác nhận" (Confirmed) chưa có khách tới (ArrivedAt IS NULL).
--    Khách đã tới (Arrived), đã huỷ, bị từ chối, đã đánh dấu không tới: không cảnh báo.
--  * Chỉ xét lượt có giờ hẹn trong 12 giờ gần nhất để cảnh báo cũ không tồn đọng sang ngày sau.
--  * Số điện thoại đã được chuẩn hoá khi đặt bàn (10 chữ số, đầu 0 — ReservationPhoneNormalizer), nên lịch sử gom theo đúng chuỗi đó.
--
-- Thành phần:
--  * dbo.ReservationNoShowHistory: lịch sử không tới theo số điện thoại. Mỗi lượt đặt tối đa MỘT dòng (khoá duy nhất ReservationId),
--    nên bấm lặp / nhiều nhân viên cùng bấm không làm tăng số lần không tới. Không khoá ngoại tới Reservations để lịch sử còn
--    nguyên khi Quản lý xoá hẳn lượt đặt đã kết thúc (usp_DeleteReservation).
--  * tr_Reservations_NoShowHistory: mọi lần chuyển sang NoShow (thủ tục mới lẫn usp_MarkManagedReservationNoShow cũ) ghi lịch sử
--    trong CÙNG giao dịch với câu lệnh đổi trạng thái.
--  * fn_ReservationWarnAt / fn_LateReservationAlerts: một nguồn quy tắc duy nhất cho sơ đồ bàn, danh sách đặt bàn và thủ tục.
--  * usp_MarkLateReservationNoShow: đánh dấu không tới + giải phóng bàn + lịch sử, khoá thao tác (usp_LockOperations) nên
--    các lần gửi trùng/đồng thời được xử lý lần lượt; lần sau nhận kết quả "đã đánh dấu trước đó", không ghi thêm.
-- Chạy lại an toàn.

IF OBJECT_ID(N'dbo.ReservationNoShowHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReservationNoShowHistory (
        Id bigint IDENTITY CONSTRAINT PK_ReservationNoShowHistory PRIMARY KEY,
        Phone varchar(10) NOT NULL CONSTRAINT CK_ReservationNoShowHistory_Phone CHECK(LEN(Phone)=10 AND Phone NOT LIKE '%[^0-9]%'),
        ReservationId bigint NOT NULL CONSTRAINT UQ_ReservationNoShowHistory_Reservation UNIQUE,
        ReservationCode char(6) NOT NULL,
        AppointmentAt datetime2(3) NOT NULL,
        NoShowAt datetime2(3) NOT NULL,
        TableId int NULL
    );
    CREATE INDEX IX_ReservationNoShowHistory_Phone ON dbo.ReservationNoShowHistory(Phone, NoShowAt);
END;
GO

-- Lượt đã được đánh dấu không tới trước migration này cũng vào lịch sử (một lần, không trùng).
INSERT dbo.ReservationNoShowHistory(Phone, ReservationId, ReservationCode, AppointmentAt, NoShowAt, TableId)
SELECT r.Phone, r.Id, r.Code, r.StartsAt, COALESCE(r.NoShowAt, r.StartsAt), r.TableId
FROM dbo.Reservations r
WHERE r.Status = 'NoShow'
  AND NOT EXISTS(SELECT 1 FROM dbo.ReservationNoShowHistory h WHERE h.ReservationId = r.Id);
GO

CREATE OR ALTER TRIGGER dbo.tr_Reservations_NoShowHistory ON dbo.Reservations AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(Status) RETURN;
    INSERT dbo.ReservationNoShowHistory(Phone, ReservationId, ReservationCode, AppointmentAt, NoShowAt, TableId)
    SELECT i.Phone, i.Id, i.Code, i.StartsAt, COALESCE(i.NoShowAt, SYSUTCDATETIME()), i.TableId
    FROM inserted i
    JOIN deleted d ON d.Id = i.Id
    WHERE i.Status = 'NoShow' AND d.Status <> 'NoShow'
      AND NOT EXISTS(SELECT 1 FROM dbo.ReservationNoShowHistory h WHERE h.ReservationId = i.Id);
END;
GO

-- Mốc cảnh báo của các lượt còn chờ khách (chưa tới): giờ hẹn + thời gian giữ bàn, hoặc mốc đã gia hạn.
CREATE OR ALTER FUNCTION dbo.fn_ReservationWarnAt()
RETURNS TABLE
AS
RETURN
    SELECT r.Id, r.Code, r.CustomerName, r.Phone, r.GuestCount, r.TableId, r.StartsAt, r.EndsAt, r.Status,
           COALESCE(r.HoldExtendedUntil, DATEADD(minute, s.NoShowGraceMinutes, r.StartsAt)) AS WarnAt
    FROM dbo.Reservations r
    CROSS JOIN dbo.RestaurantSettings s
    WHERE s.Id = 1 AND r.Status IN ('Pending', 'Confirmed') AND r.ArrivedAt IS NULL;
GO

-- Lượt đang có cảnh báo trễ tại thời điểm @NowUtc (bắt đầu từ đúng mốc cảnh báo).
CREATE OR ALTER FUNCTION dbo.fn_LateReservationAlerts(@NowUtc datetime2(3))
RETURNS TABLE
AS
RETURN
    SELECT w.Id, w.Code, w.CustomerName, w.Phone, w.GuestCount, w.TableId, t.Code AS TableCode, w.StartsAt, w.EndsAt,
           w.Status, w.WarnAt, DATEDIFF(second, w.StartsAt, @NowUtc) / 60 AS LateMinutes
    FROM dbo.fn_ReservationWarnAt() w
    LEFT JOIN dbo.DiningTables t ON t.Id = w.TableId
    WHERE w.WarnAt <= @NowUtc AND w.StartsAt > DATEADD(hour, -12, @NowUtc);
GO

CREATE OR ALTER PROCEDURE dbo.usp_MarkLateReservationNoShow
    @ReservationId bigint,
    @ActorUserId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- Khoá thao tác chung: hai nhân viên (hoặc hai lần bấm) cùng gửi sẽ chạy lần lượt.
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Reservations.Manage';

        DECLARE @now datetime2(3) = SYSUTCDATETIME();
        DECLARE @status varchar(20), @tableId int, @phone varchar(10), @code char(6), @outcome varchar(20), @released bit = 0;
        SELECT @status = Status, @tableId = TableId, @phone = Phone, @code = Code
        FROM dbo.Reservations WITH (UPDLOCK) WHERE Id = @ReservationId;

        IF @status IS NULL THROW 51120, N'Không tìm thấy lượt đặt bàn.', 1;
        IF @status = 'NoShow'
            SET @outcome = 'AlreadyNoShow';   -- gửi lặp: không ghi thêm gì, không tăng số lần không tới
        ELSE
        BEGIN
            IF @status = 'Arrived'
                THROW 51121, N'Khách đã tới nên không đánh dấu khách không tới; bàn vẫn giữ cho khách.', 1;
            IF @status NOT IN ('Pending', 'Confirmed')
                THROW 51122, N'Lượt đặt này đã huỷ hoặc bị từ chối, không cần đánh dấu khách không tới.', 1;
            IF NOT EXISTS(SELECT 1 FROM dbo.fn_LateReservationAlerts(@now) WHERE Id = @ReservationId)
                THROW 51123, N'Lượt đặt chưa trễ tới mốc cảnh báo nên chưa đánh dấu khách không tới được.', 1;

            UPDATE dbo.Reservations SET Status = 'NoShow', NoShowAt = @now
            WHERE Id = @ReservationId AND Status = @status AND ArrivedAt IS NULL;   -- tr_Reservations_NoShowHistory ghi lịch sử
            INSERT dbo.ReservationEvents(ReservationId, FromStatus, ToStatus, OldTableId, ActorUserId, Reason)
            VALUES(@ReservationId, @status, 'NoShow', @tableId, @ActorUserId, N'Trễ quá mốc giữ bàn, khách không tới');

            -- Giải phóng bàn đang giữ cho lượt này: chỉ khi bàn đang "Đã đặt trước", không có phiên phục vụ đang mở
            -- và không có lượt đã xác nhận khác sắp tới (30 phút) trên bàn đó. tr_DiningTables_StatusChanged đẩy cập nhật lên sơ đồ.
            IF @tableId IS NOT NULL
            BEGIN
                DECLARE @tableBefore varchar(20) = (SELECT Status FROM dbo.DiningTables WHERE Id = @tableId);
                UPDATE dbo.DiningTables SET Status = 'Available'
                WHERE Id = @tableId AND Status = 'Reserved'
                  AND NOT EXISTS(SELECT 1 FROM dbo.SessionTables st WHERE st.TableId = @tableId AND st.ReleasedAt IS NULL)
                  AND NOT EXISTS(SELECT 1 FROM dbo.Reservations o WHERE o.TableId = @tableId AND o.Id <> @ReservationId
                                 AND o.Status = 'Confirmed' AND o.StartsAt <= DATEADD(minute, 30, @now) AND o.EndsAt > @now);
                IF @tableBefore = 'Reserved' AND (SELECT Status FROM dbo.DiningTables WHERE Id = @tableId) = 'Available'
                    SET @released = 1;
            END;
            SET @outcome = 'Marked';
        END;

        SELECT @outcome AS Outcome, @code AS ReservationCode, t.Code AS TableCode, t.Status AS TableStatus,
               @released AS TableReleased,
               (SELECT COUNT(*) FROM dbo.ReservationNoShowHistory h WHERE h.Phone = @phone) AS PhoneNoShowCount
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

GRANT EXECUTE ON dbo.usp_MarkLateReservationNoShow TO restaurant_app;
GRANT SELECT ON dbo.fn_LateReservationAlerts TO restaurant_app;
GRANT SELECT ON dbo.fn_ReservationWarnAt TO restaurant_app;
GRANT SELECT ON dbo.ReservationNoShowHistory TO restaurant_app;
GO
