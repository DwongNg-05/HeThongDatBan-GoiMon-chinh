-- S3-01 Task 1 (sửa theo phản hồi): quét QR mở phiên gọi món theo GIỜ HOẠT ĐỘNG của nhà hàng, không bắt buộc thu ngân mở ca trước.
--
-- 1) DiningSessions.ShiftId cho phép NULL, CHỈ với phiên do khách mở bằng QR khi chưa có ca đang mở.
--    Phiên do nhân viên mở (usp_OpenSession) vẫn luôn thuộc một ca.
-- 2) usp_StartQrGuestSession: thay điều kiện "đang có ca mở" bằng "đang trong giờ hoạt động hôm nay" (giờ Việt Nam,
--    dbo.OpeningHours, trừ ngày nghỉ đặc biệt dbo.SpecialHolidays). Có ca đang mở thì phiên gắn ngay vào ca đó.
-- 3) usp_OpenShift: khi thu ngân mở ca, các phiên QR chưa thuộc ca nào được gắn vào ca mới (để thanh toán, chốt ca như thường).
-- Chạy lại an toàn.

IF COLUMNPROPERTY(OBJECT_ID(N'dbo.DiningSessions'), N'ShiftId', 'AllowsNull') = 0
BEGIN
    DECLARE @fk sysname =
    (
        SELECT TOP (1) fk.name
        FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns c ON c.constraint_object_id = fk.object_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.DiningSessions')
          AND COL_NAME(c.parent_object_id, c.parent_column_id) = N'ShiftId'
    );
    IF @fk IS NOT NULL
    BEGIN
        DECLARE @dropFk nvarchar(400) = N'ALTER TABLE dbo.DiningSessions DROP CONSTRAINT ' + QUOTENAME(@fk) + N';';
        EXEC sys.sp_executesql @dropFk;
    END;

    ALTER TABLE dbo.DiningSessions ALTER COLUMN ShiftId bigint NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DiningSessions_Shift')
    ALTER TABLE dbo.DiningSessions WITH CHECK
        ADD CONSTRAINT FK_DiningSessions_Shift FOREIGN KEY (ShiftId) REFERENCES dbo.Shifts(Id);
GO

-- Chỉ phiên do khách mở bằng QR mới được tạm chưa có ca.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_DiningSessions_ShiftOrQr')
    ALTER TABLE dbo.DiningSessions WITH CHECK
        ADD CONSTRAINT CK_DiningSessions_ShiftOrQr CHECK (ShiftId IS NOT NULL OR OpenedByQrCodeId IS NOT NULL);
GO

-- Đang trong giờ hoạt động (giờ Việt Nam) tại thời điểm @AtUtc: ngày không nghỉ, không phải ngày nghỉ đặc biệt,
-- giờ mở cửa <= giờ hiện tại < giờ đóng cửa. Cùng cách tính thứ và múi giờ với usp_ValidateBookingSchedule (018).
CREATE OR ALTER FUNCTION dbo.fn_IsWithinOpeningHours(@AtUtc datetime2(3))
RETURNS bit
AS
BEGIN
    DECLARE @local datetime2(3) = CONVERT(datetime2(3), (@AtUtc AT TIME ZONE 'UTC') AT TIME ZONE 'SE Asia Standard Time');
    DECLARE @day date = CONVERT(date, @local), @time time(3) = CONVERT(time(3), @local);
    DECLARE @weekday int = (DATEDIFF(day, CONVERT(date, '19000101'), @day) % 7) + 1;

    IF EXISTS (SELECT 1 FROM dbo.SpecialHolidays WHERE HolidayDate = @day AND IsActive = 1)
        RETURN 0;
    IF EXISTS
    (
        SELECT 1 FROM dbo.OpeningHours
        WHERE DayOfWeek = @weekday AND IsClosed = 0
          AND OpensAt IS NOT NULL AND ClosesAt IS NOT NULL
          AND @time >= OpensAt AND @time < ClosesAt
    )
        RETURN 1;
    RETURN 0;
END;
GO

-- Kết quả giống migration 043, riêng NoShift được thay bằng OutsideOpeningHours (ngoài giờ hoạt động).
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

-- Như 002_BusinessOperations.sql, thêm: gắn các phiên QR đang mở chưa thuộc ca nào vào ca vừa mở.
CREATE OR ALTER PROCEDURE dbo.usp_OpenShift @Name nvarchar(100),@OpeningCash decimal(18,0),@ActorUserId int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.usp_LockOperations;
  EXEC dbo.usp_RequirePermission @ActorUserId,'Payments.Manage';

  INSERT dbo.Shifts(Name,BusinessDate,OpenedBy,OpeningCash) VALUES(@Name,CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())),@ActorUserId,@OpeningCash);
  DECLARE @id bigint=SCOPE_IDENTITY();
  INSERT dbo.ShiftEvents(ShiftId,Action,ActorUserId) VALUES(@id,'Opened',@ActorUserId);
  UPDATE dbo.DiningSessions SET ShiftId=@id WHERE ShiftId IS NULL AND Status<>'Closed';
  SELECT @id AS ShiftId;
  COMMIT;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK;
  THROW;
 END CATCH
END;
GO

GRANT EXECUTE ON dbo.usp_StartQrGuestSession TO restaurant_app;
GRANT EXECUTE ON dbo.usp_OpenShift TO restaurant_app;
GO
