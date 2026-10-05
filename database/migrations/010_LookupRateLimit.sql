/*
    S2-04 Task 3
    Rate limit tra cứu đặt bàn theo địa chỉ IP.

    Chính sách mặc định hiện tại:
    - Cửa sổ đếm: 10 phút.
    - Được phép sai tối đa 5 lần.
    - Lần sai thứ 6 bắt đầu bị chặn.
    - Thời gian chặn mặc định: 10 phút.
    - Tra cứu đúng không xoá lịch sử sai.
    - Request trong thời gian bị chặn không kéo dài thời gian chặn.

    Các giá trị này có thể thay đổi sau khi PO chốt.
*/

CREATE OR ALTER PROCEDURE dbo.usp_GetReservationLookupRateLimit
    @IpAddress varchar(45)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now datetime2(3) = SYSUTCDATETIME();
    DECLARE @blockedUntil datetime2(3);

    SELECT TOP (1)
        @blockedUntil =
            TRY_CONVERT(
                datetime2(3),
                JSON_VALUE(NewValues, '$.BlockedUntil')
            )
    FROM dbo.AuditLogs
    WHERE Action = 'LookupRateLimitBlocked'
      AND EntityType = 'ReservationLookup'
      AND IpAddress = @IpAddress
      AND TRY_CONVERT(
            datetime2(3),
            JSON_VALUE(NewValues, '$.BlockedUntil')
          ) > @now
    ORDER BY OccurredAt DESC;

    SELECT
        CAST(
            CASE
                WHEN @blockedUntil IS NOT NULL THEN 1
                ELSE 0
            END
            AS bit
        ) AS IsBlocked,

        @blockedUntil AS BlockedUntil,

        CASE
            WHEN @blockedUntil IS NULL THEN 0
            ELSE
                CASE
                    WHEN DATEDIFF(
                        SECOND,
                        @now,
                        @blockedUntil
                    ) < 1
                    THEN 1
                    ELSE DATEDIFF(
                        SECOND,
                        @now,
                        @blockedUntil
                    )
                END
        END AS RemainingSeconds;
END;
GO


CREATE OR ALTER PROCEDURE dbo.usp_RecordReservationLookupAttempt
    @IpAddress varchar(45),
    @Succeeded bit
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @now datetime2(3) = SYSUTCDATETIME();

    -- Các giá trị mặc định.
    -- Có thể điều chỉnh sau khi PO chốt.
    DECLARE @windowMinutes int = 10;
    DECLARE @maxFailures int = 5;
    DECLARE @blockMinutes int = 10;

    DECLARE @failureCount int;
    DECLARE @blockedUntil datetime2(3);
    DECLARE @existingBlockedUntil datetime2(3);

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT dbo.LookupAttempts
        (
            IpAddress,
            Succeeded,
            AttemptedAt
        )
        VALUES
        (
            @IpAddress,
            @Succeeded,
            @now
        );

        SELECT
            @failureCount = COUNT(*)
        FROM dbo.LookupAttempts
        WHERE IpAddress = @IpAddress
          AND Succeeded = 0
          AND AttemptedAt >=
              DATEADD(
                  MINUTE,
                  -@windowMinutes,
                  @now
              );

        /*
            Chỉ lần sai thứ 6 trở đi mới vượt ngưỡng.

            @maxFailures = 5
            FailureCount = 5 -> chưa block
            FailureCount = 6 -> block
        */
        IF @Succeeded = 0
           AND @failureCount > @maxFailures
        BEGIN
            SELECT TOP (1)
                @existingBlockedUntil =
                    TRY_CONVERT(
                        datetime2(3),
                        JSON_VALUE(
                            NewValues,
                            '$.BlockedUntil'
                        )
                    )
            FROM dbo.AuditLogs
            WHERE Action = 'LookupRateLimitBlocked'
              AND EntityType = 'ReservationLookup'
              AND IpAddress = @IpAddress
              AND TRY_CONVERT(
                    datetime2(3),
                    JSON_VALUE(
                        NewValues,
                        '$.BlockedUntil'
                    )
                  ) > @now
            ORDER BY OccurredAt DESC;

            -- Không tạo log block trùng trong cùng một lần khoá.
            IF @existingBlockedUntil IS NULL
            BEGIN
                SET @blockedUntil =
                    DATEADD(
                        MINUTE,
                        @blockMinutes,
                        @now
                    );

                DECLARE @newValues nvarchar(max);

                SET @newValues =
                (
                    SELECT
                        @failureCount AS FailureCount,
                        @windowMinutes AS WindowMinutes,
                        @maxFailures AS MaxFailures,
                        @blockMinutes AS BlockMinutes,
                        @blockedUntil AS BlockedUntil
                    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                );

                INSERT dbo.AuditLogs
                (
                    Action,
                    EntityType,
                    EntityId,
                    NewValues,
                    Reason,
                    IpAddress,
                    OccurredAt
                )
                VALUES
                (
                    'LookupRateLimitBlocked',
                    'ReservationLookup',
                    NULL,
                    @newValues,
                    N'Địa chỉ IP vượt quá số lần tra cứu đặt bàn sai cho phép.',
                    @IpAddress,
                    @now
                );
            END
            ELSE
            BEGIN
                SET @blockedUntil = @existingBlockedUntil;
            END
        END;

        COMMIT;

        SELECT
            @failureCount AS FailureCount,

            CAST(
                CASE
                    WHEN @blockedUntil IS NOT NULL THEN 1
                    ELSE 0
                END
                AS bit
            ) AS IsBlocked,

            @blockedUntil AS BlockedUntil,

            CASE
                WHEN @blockedUntil IS NULL THEN 0
                ELSE
                    CASE
                        WHEN DATEDIFF(
                            SECOND,
                            @now,
                            @blockedUntil
                        ) < 1
                        THEN 1
                        ELSE DATEDIFF(
                            SECOND,
                            @now,
                            @blockedUntil
                        )
                    END
            END AS RemainingSeconds;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK;

        THROW;
    END CATCH
END;
GO