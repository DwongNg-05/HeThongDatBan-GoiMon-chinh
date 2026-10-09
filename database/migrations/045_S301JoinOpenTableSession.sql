-- S3-01 Task 2: khách quét QR của bàn ĐANG PHỤC VỤ được đưa vào phiên hiện tại của bàn (không tạo phiên mới)
-- và thấy các món bàn đã gọi trước đó (trang /TableOrder đọc theo phiên, không theo điện thoại).
--
-- Thay đổi so với 044:
--   * Bàn có phiên Open (nhân viên mở bằng usp_OpenSession hoặc khách khác mở bằng QR) → cấp phiên khách mới cho
--     điện thoại vào ĐÚNG phiên đó, kết quả 'Joined' (trước đây: 'TableBusy' sau 120 giây đầu).
--   * Phiên đang chờ thanh toán (AwaitingPayment) → 'AwaitingPayment', không cho vào để gọi thêm.
--   * Các nhánh còn lại giữ nguyên 044 (bàn trống trong giờ hoạt động → tạo phiên mới; đặt trước/đang dọn/ngoài giờ → từ chối).
-- Chạy lại an toàn.

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
GO
