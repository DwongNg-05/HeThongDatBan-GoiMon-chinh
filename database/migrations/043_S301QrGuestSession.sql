-- S3-01 Task 1: khách quét QR của bàn trống → mở phiên phục vụ mới của đúng bàn, không cần nhân viên/đăng nhập.
-- Quy tắc "bàn trống" và thời điểm chuyển "đang phục vụ" (đề xuất, chờ PO chốt): docs/S3-01-Task1.md.
--
-- 1) Phiên do khách tự mở bằng QR không có nhân viên mở: DiningSessions.OpenedBy cho phép NULL,
--    thay bằng OpenedByQrCodeId (mã QR đã dùng). Mỗi phiên có đúng một nguồn mở.
-- 2) dbo.usp_StartQrGuestSession: kiểm tra QR, kiểm tra bàn trống, tạo phiên + gắn bàn + chuyển bàn sang Serving
--    + cấp phiên khách (GuestSessions) trong MỘT giao dịch, dưới khoá nghiệp vụ chung (usp_LockOperations).
--    Quét lại / bấm nhiều lần không tạo thêm phiên.
-- Chạy lại an toàn.

IF COLUMNPROPERTY(OBJECT_ID(N'dbo.DiningSessions'), N'OpenedBy', 'AllowsNull') = 0
BEGIN
    -- Khoá ngoại tới dbo.Users được tạo không tên ở 001_Schema.sql: tìm tên thật, bỏ, đổi cột, tạo lại có tên.
    DECLARE @fk sysname =
    (
        SELECT TOP (1) fk.name
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns c ON c.constraint_object_id = fk.object_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.DiningSessions')
          AND COL_NAME(c.parent_object_id, c.parent_column_id) = N'OpenedBy'
    );
    IF @fk IS NOT NULL
    BEGIN
        DECLARE @dropFk nvarchar(400) = N'ALTER TABLE dbo.DiningSessions DROP CONSTRAINT ' + QUOTENAME(@fk) + N';';
        EXEC sys.sp_executesql @dropFk;
    END;

    ALTER TABLE dbo.DiningSessions ALTER COLUMN OpenedBy int NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DiningSessions_OpenedBy')
    ALTER TABLE dbo.DiningSessions WITH CHECK
        ADD CONSTRAINT FK_DiningSessions_OpenedBy FOREIGN KEY (OpenedBy) REFERENCES dbo.Users(Id);
GO

IF COL_LENGTH(N'dbo.DiningSessions', N'OpenedByQrCodeId') IS NULL
    ALTER TABLE dbo.DiningSessions ADD OpenedByQrCodeId bigint NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DiningSessions_OpenedByQrCode')
    ALTER TABLE dbo.DiningSessions WITH CHECK
        ADD CONSTRAINT FK_DiningSessions_OpenedByQrCode FOREIGN KEY (OpenedByQrCodeId) REFERENCES dbo.TableQrCodes(Id);
GO

-- Phiên do nhân viên mở (OpenedBy) hoặc do khách quét QR (OpenedByQrCodeId), không bao giờ cả hai hay không có gì.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_DiningSessions_Opener')
    ALTER TABLE dbo.DiningSessions WITH CHECK
        ADD CONSTRAINT CK_DiningSessions_Opener CHECK
        (
            (OpenedBy IS NOT NULL AND OpenedByQrCodeId IS NULL)
            OR (OpenedBy IS NULL AND OpenedByQrCodeId IS NOT NULL)
        );
GO

-- Kết quả (cột Outcome):
--   Started   : bàn trống → đã tạo phiên mới, bàn chuyển Available → Serving, cấp phiên khách @GuestTokenHash.
--   Rejoined  : cùng mã QR vừa mở phiên trong 120 giây và phiên chưa có món (quét/bấm lặp, 2 yêu cầu đồng thời)
--               → KHÔNG tạo phiên mới, cấp phiên khách @GuestTokenHash vào đúng phiên vừa mở.
--   Resumed   : điện thoại đã có phiên khách còn hạn của bàn này (@ExistingGuestTokenHash) → dùng lại, không ghi gì.
--   InvalidQr : mã không tồn tại, bàn hoặc khu vực ngừng dùng.     QrChanged : mã đã bị thu hồi (sinh lại QR).
--   TableReserved / TableBusy / TableCleaning : bàn không trống.    NoShift : chưa mở ca phục vụ.
-- Chỉ Started/Rejoined thay đổi dữ liệu. Các kết quả khác không ghi gì.
CREATE OR ALTER PROCEDURE dbo.usp_StartQrGuestSession
    @QrTokenHash binary(32),
    @GuestTokenHash binary(32),
    @ExistingGuestTokenHash binary(32) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @DuplicateScanSeconds int = 120;  -- cửa sổ coi là "quét lặp" của cùng một lần mở bàn
    DECLARE @GuestSessionHours int = 12;      -- giống usp_OpenGuestSession

    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;

        DECLARE @now datetime2(3) = SYSUTCDATETIME();
        DECLARE @outcome varchar(20), @session bigint, @expires datetime2(3);
        DECLARE @qr bigint, @table int, @status varchar(20), @usable bit, @revoked bit;

        SELECT @qr = q.Id, @table = t.Id, @status = t.Status,
               @usable = CASE WHEN t.IsActive = 1 AND a.IsActive = 1 THEN 1 ELSE 0 END,
               @revoked = CASE WHEN q.RevokedAt IS NULL THEN 0 ELSE 1 END
        FROM dbo.TableQrCodes q
        JOIN dbo.DiningTables t WITH (UPDLOCK) ON t.Id = q.TableId
        JOIN dbo.Areas a ON a.Id = t.AreaId
        WHERE q.TokenHash = @QrTokenHash;

        IF @qr IS NULL OR @usable = 0
            SET @outcome = 'InvalidQr';
        ELSE IF @revoked = 1
            SET @outcome = 'QrChanged';
        ELSE
        BEGIN
            -- 1. Điện thoại này đã ở trong phiên đang mở của chính bàn này.
            IF @ExistingGuestTokenHash IS NOT NULL
                SELECT @session = g.SessionId, @expires = g.ExpiresAt
                FROM dbo.GuestSessions g
                JOIN dbo.DiningSessions s ON s.Id = g.SessionId AND s.Status = 'Open'
                JOIN dbo.SessionTables st ON st.SessionId = s.Id AND st.TableId = @table AND st.ReleasedAt IS NULL
                WHERE g.TokenHash = @ExistingGuestTokenHash AND g.TableQrCodeId = @qr
                  AND g.RevokedAt IS NULL AND g.ExpiresAt > @now;

            IF @session IS NOT NULL
                SET @outcome = 'Resumed';
            ELSE
            BEGIN
                DECLARE @open bigint, @openStatus varchar(20), @openedByQr bigint, @openedAt datetime2(3);
                SELECT TOP (1) @open = s.Id, @openStatus = s.Status, @openedByQr = s.OpenedByQrCodeId, @openedAt = s.OpenedAt
                FROM dbo.SessionTables st
                JOIN dbo.DiningSessions s ON s.Id = st.SessionId
                WHERE st.TableId = @table AND st.ReleasedAt IS NULL
                ORDER BY s.Id DESC;

                IF @open IS NOT NULL
                BEGIN
                    -- 2. Quét lặp ngay sau khi chính mã này vừa mở bàn: vào lại đúng phiên đó, không tạo phiên thứ hai.
                    IF @openStatus = 'Open' AND @openedByQr = @qr
                       AND @openedAt > DATEADD(second, -@DuplicateScanSeconds, @now)
                       AND NOT EXISTS (SELECT 1 FROM dbo.OrderBatches WHERE SessionId = @open)
                    BEGIN
                        SET @session = @open;
                        SET @expires = DATEADD(hour, @GuestSessionHours, @now);
                        INSERT dbo.GuestSessions(SessionId, TableQrCodeId, TokenHash, CreatedAt, ExpiresAt)
                        VALUES (@session, @qr, @GuestTokenHash, @now, @expires);
                        SET @outcome = 'Rejoined';
                    END
                    ELSE
                        SET @outcome = 'TableBusy';  -- vào chung phiên đang mở: chưa làm ở task này
                END
                ELSE IF @status = 'Reserved'
                    SET @outcome = 'TableReserved';
                ELSE IF @status = 'Cleaning'
                    SET @outcome = 'TableCleaning';
                ELSE IF @status <> 'Available'
                    SET @outcome = 'TableBusy';
                -- Bàn đang giữ cho lượt đặt (Chờ xác nhận / Đã xác nhận) bắt đầu trong khoảng một lượt ngồi tới,
                -- hoặc đang trong giờ đặt + 15 phút dọn bàn (cùng quy tắc giữ bàn của 028_S203).
                ELSE IF EXISTS
                (
                    SELECT 1 FROM dbo.Reservations r
                    WHERE r.TableId = @table AND r.Status IN ('Pending', 'Confirmed')
                      AND r.StartsAt < DATEADD(minute, (SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id = 1), @now)
                      AND DATEADD(minute, 15, r.EndsAt) > @now
                )
                    SET @outcome = 'TableReserved';
                ELSE
                BEGIN
                    DECLARE @shift bigint = (SELECT Id FROM dbo.Shifts WHERE Status = 'Open');
                    IF @shift IS NULL
                        SET @outcome = 'NoShift';
                    ELSE
                    BEGIN
                        -- 3. Bàn trống: tạo phiên, gắn bàn, chuyển Serving, cấp phiên khách — cùng một giao dịch.
                        --    Số khách chưa hỏi ở bước này: tạm ghi 1, nhân viên cập nhật sau (xem docs/S3-01-Task1.md).
                        INSERT dbo.DiningSessions(ShiftId, GuestCount, OpenedBy, OpenedByQrCodeId, OpenedAt)
                        VALUES (@shift, 1, NULL, @qr, @now);
                        SET @session = SCOPE_IDENTITY();

                        INSERT dbo.SessionTables(SessionId, TableId, AssignedAt) VALUES (@session, @table, @now);

                        UPDATE dbo.DiningTables SET Status = 'Serving', StatusChangedAt = @now
                        WHERE Id = @table AND Status = 'Available';
                        IF @@ROWCOUNT <> 1
                            THROW 51310, N'Bàn vừa đổi trạng thái. Vui lòng quét lại mã QR.', 1;

                        SET @expires = DATEADD(hour, @GuestSessionHours, @now);
                        INSERT dbo.GuestSessions(SessionId, TableQrCodeId, TokenHash, CreatedAt, ExpiresAt)
                        VALUES (@session, @qr, @GuestTokenHash, @now, @expires);
                        SET @outcome = 'Started';
                    END
                END
            END
        END

        COMMIT;
        -- Trả kết quả sau khi đã commit: ứng dụng chỉ cấp cookie khi dữ liệu đã chắc chắn được lưu.
        SELECT @outcome AS Outcome, @session AS SessionId, @expires AS ExpiresAt;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON dbo.usp_StartQrGuestSession TO restaurant_app;
GO
