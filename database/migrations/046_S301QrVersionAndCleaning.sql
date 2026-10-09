-- S3-01 Task 3: chặn mở phiên khi mã QR đã bị thay thế hoặc bàn đang dọn.
--
-- 1) Phiên bản mã QR: TableQrCodes.Version (1, 2, 3... theo từng bàn). Mã hiện tại = phiên bản lớn nhất, chưa thu hồi
--    (RevokedAt IS NULL; chỉ mục UX_TableQrCodes_Active bảo đảm mỗi bàn đúng một mã hiện tại).
--    Mã khách quét được đối chiếu bằng TokenHash → nếu không phải phiên bản hiện tại (đã thu hồi) thì từ chối 'QrChanged'.
-- 2) usp_RotateTableQr ghi phiên bản mới mỗi lần sinh lại.
-- 3) usp_StartQrGuestSession kiểm tra bàn "đang dọn" NGAY sau khi kiểm mã, trước cả "quét lại"/"vào chung phiên".
-- Mọi trường hợp bị từ chối không ghi gì vào database.
-- Chạy lại an toàn.

IF COL_LENGTH(N'dbo.TableQrCodes', N'Version') IS NULL
    ALTER TABLE dbo.TableQrCodes ADD Version int NULL;
GO

-- Đánh số phiên bản cho các mã đã có, theo thứ tự tạo của từng bàn.
WITH numbered AS
(
    SELECT Version, ROW_NUMBER() OVER (PARTITION BY TableId ORDER BY Id) AS n
    FROM dbo.TableQrCodes
)
UPDATE numbered SET Version = n WHERE Version IS NULL OR Version <> n;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TableQrCodes') AND name = N'UX_TableQrCodes_TableVersion')
    CREATE UNIQUE INDEX UX_TableQrCodes_TableVersion ON dbo.TableQrCodes(TableId, Version) WHERE Version IS NOT NULL;
GO

CREATE OR ALTER PROCEDURE dbo.usp_RotateTableQr
    @TableId int,
    @TokenHash binary(32),
    @ActorUserId int,
    @PublicToken varchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Catalog.Manage';

        IF @PublicToken IS NOT NULL
        BEGIN
            IF LEN(@PublicToken) NOT BETWEEN 16 AND 64
               OR DATALENGTH(@PublicToken) <> LEN(@PublicToken)
               OR @PublicToken COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Za-z0-9_-]%'
                THROW 51031, N'Mã QR công khai không hợp lệ.', 1;

            IF @TokenHash IS NULL
               OR HASHBYTES('SHA2_256', CONVERT(varbinary(64), @PublicToken)) <> @TokenHash
                THROW 51032, N'Mã hash QR không khớp với mã QR công khai.', 1;
        END;

        IF NOT EXISTS (SELECT 1 FROM dbo.DiningTables WHERE Id = @TableId AND IsActive = 1)
            THROW 51021, N'Bàn không hoạt động.', 1;

        -- These statements and the insert are one transaction. A failed insert (for example,
        -- an accidental token collision) rolls back the revocations as well.
        UPDATE gs
        SET RevokedAt = SYSUTCDATETIME()
        FROM dbo.GuestSessions gs
        JOIN dbo.TableQrCodes q ON q.Id = gs.TableQrCodeId
        WHERE q.TableId = @TableId AND gs.RevokedAt IS NULL;

        UPDATE dbo.TableQrCodes
        SET RevokedAt = SYSUTCDATETIME()
        WHERE TableId = @TableId AND RevokedAt IS NULL;

        -- S3-01 Task 3: mỗi lần sinh mã là một phiên bản mới của bàn (1, 2, 3, ...).
        DECLARE @version int = (SELECT COALESCE(MAX(Version), 0) + 1 FROM dbo.TableQrCodes WHERE TableId = @TableId);
        INSERT dbo.TableQrCodes(TableId, TokenHash, PublicToken, CreatedBy, Version)
        VALUES(@TableId, @TokenHash, @PublicToken, @ActorUserId, @version);

        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

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
            SET @outcome = 'QrChanged';   -- S3-01 Task 3: mã đã bị sinh lại (không phải phiên bản hiện tại của bàn)
        -- S3-01 Task 3: bàn đang dọn không nhận khách, kể cả điện thoại đã từng ở trong phiên trước của bàn.
        ELSE IF @status = 'Cleaning'
            SET @outcome = 'TableCleaning';
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
                    -- 2. S3-01 Task 2: bàn đã có phiên đang mở (do nhân viên hoặc khách khác mở) → vào chung phiên đó,
                    --    KHÔNG tạo phiên mới. Chỉ cấp thêm một phiên khách (GuestSessions) cho điện thoại này.
                    IF @openStatus = 'Open'
                    BEGIN
                        SET @session = @open;
                        SET @expires = DATEADD(hour, @GuestSessionHours, @now);
                        INSERT dbo.GuestSessions(SessionId, TableQrCodeId, TokenHash, CreatedAt, ExpiresAt)
                        VALUES (@session, @qr, @GuestTokenHash, @now, @expires);
                        -- Quét lặp ngay sau khi chính mã này vừa mở bàn (Task 1) vẫn báo Rejoined; còn lại là vào chung phiên.
                        SET @outcome = CASE WHEN @openedByQr = @qr AND @openedAt > DATEADD(second, -@DuplicateScanSeconds, @now)
                                                 AND NOT EXISTS (SELECT 1 FROM dbo.OrderBatches WHERE SessionId = @open)
                                            THEN 'Rejoined' ELSE 'Joined' END;
                    END
                    ELSE IF @openStatus = 'AwaitingPayment'
                        SET @outcome = 'AwaitingPayment';  -- đang thanh toán: không nhận khách/món mới
                    ELSE
                        SET @outcome = 'TableBusy';
                END
                ELSE IF @status = 'Reserved'
                    SET @outcome = 'TableReserved';
                ELSE IF @status = 'Cleaning'
                    SET @outcome = 'TableCleaning';
                ELSE IF @status <> 'Available'
                    SET @outcome = 'TableBusy';
                ELSE IF EXISTS
                (
                    SELECT 1 FROM dbo.Reservations r
                    WHERE r.TableId = @table AND r.Status IN ('Pending', 'Confirmed')
                      AND r.StartsAt < DATEADD(minute, (SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id = 1), @now)
                      AND DATEADD(minute, 15, r.EndsAt) > @now
                )
                    SET @outcome = 'TableReserved';
                ELSE IF dbo.fn_IsWithinOpeningHours(@now) = 0
                    SET @outcome = 'OutsideOpeningHours';
                ELSE
                BEGIN
                    -- 3. Bàn trống, trong giờ hoạt động: tạo phiên, gắn bàn, chuyển Serving, cấp phiên khách — cùng một giao dịch.
                    --    Có ca đang mở thì gắn vào ca; chưa có thì để trống, usp_OpenShift gắn khi thu ngân mở ca.
                    DECLARE @shift bigint = (SELECT Id FROM dbo.Shifts WHERE Status = 'Open');
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

        COMMIT;
        SELECT @outcome AS Outcome, @session AS SessionId, @expires AS ExpiresAt;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

GRANT EXECUTE ON dbo.usp_StartQrGuestSession TO restaurant_app;
GRANT EXECUTE ON dbo.usp_RotateTableQr TO restaurant_app;
GO
