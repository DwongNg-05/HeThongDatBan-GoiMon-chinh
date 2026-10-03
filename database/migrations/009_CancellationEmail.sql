CREATE OR ALTER PROCEDURE dbo.usp_QueueBookingEmail
    @ReservationId bigint,
    @Kind varchar(30)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE
        @email nvarchar(254),
        @payload nvarchar(max),
        @key varchar(100) = CONCAT(@Kind, ':', @ReservationId),
        @subject nvarchar(250);

    SELECT @email = Email
    FROM dbo.Reservations
    WHERE Id = @ReservationId;

    -- Không có email thì bỏ qua.
    -- Việc huỷ/đặt bàn vẫn giữ nguyên thành công.
    IF @email IS NULL OR LTRIM(RTRIM(@email)) = ''
        RETURN;

    SET @subject =
        CASE @Kind
            WHEN 'BookingCancelled'
                THEN N'Xác nhận huỷ đặt bàn'
            WHEN 'BookingConfirmed'
                THEN N'Xác nhận đặt bàn'
            WHEN 'BookingRejected'
                THEN N'Thông báo về đặt bàn'
            WHEN 'BookingReceived'
                THEN N'Đã nhận yêu cầu đặt bàn'
            ELSE N'Thông tin đặt bàn'
        END;

    SET @payload =
    (
        SELECT
            r.Code,
            r.CustomerName,
            r.StartsAt,
            r.EndsAt,
            r.GuestCount,
            r.TableId,
            r.Status,
            a.Name AS AreaName
        FROM dbo.Reservations r
        LEFT JOIN dbo.DiningTables t
            ON t.Id = r.TableId
        LEFT JOIN dbo.Areas a
            ON a.Id = COALESCE(t.AreaId, r.PreferredAreaId)
        WHERE r.Id = @ReservationId
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    );

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.EmailOutbox
        WHERE DedupeKey = @key
    )
    BEGIN
        INSERT dbo.EmailOutbox
        (
            ReservationId,
            MessageType,
            Recipient,
            Subject,
            PayloadJson,
            DedupeKey
        )
        VALUES
        (
            @ReservationId,
            @Kind,
            @email,
            @subject,
            @payload,
            @key
        );
    END
END;
GO