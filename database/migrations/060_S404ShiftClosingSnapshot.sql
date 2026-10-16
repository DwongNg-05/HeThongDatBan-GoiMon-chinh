CREATE OR ALTER TRIGGER dbo.trg_ShiftEvents_ClosingSnapshot
ON dbo.ShiftEvents
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1
        FROM inserted
        WHERE Action = 'Closed'
    )
        RETURN;

    -- Không cho tạo bản chốt thiếu thông tin nhà hàng.
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.RestaurantSettings
        WHERE Id = 1
    )
    BEGIN
        THROW 51460,
            N'Chưa cấu hình thông tin nhà hàng để lưu bản chốt ca.',
            1;
    END;

    -- Ghi thông tin ngay trong giao dịch chốt ca.
    -- Sau khi mở lại ca, bản JSON này vẫn giữ số liệu lần chốt đó.
    UPDATE eventRow
    SET SnapshotJson = (
        SELECT
            1 AS SnapshotVersion,

            shiftRow.Id AS ShiftId,
            shiftRow.Name AS ShiftName,
            shiftRow.BusinessDate,
            shiftRow.OpenedAt,

            (
                SELECT COUNT_BIG(*)
                FROM dbo.ShiftEvents previousEvent
                WHERE previousEvent.ShiftId = eventRow.ShiftId
                  AND previousEvent.Action = 'Closed'
                  AND previousEvent.Id <= eventRow.Id
            ) AS ClosingNumber,

            eventRow.Id AS ClosingEventId,
            eventRow.OccurredAt AS ClosedAt,
            eventRow.ActorUserId AS ClosedBy,
            closingUser.FullName AS ClosedByName,

            N'Closed' AS StatusAtClosing,

            restaurant.Name AS RestaurantName,
            restaurant.Address AS RestaurantAddress,
            restaurant.Phone AS RestaurantPhone,
            restaurant.TimeZoneId,

            shiftRow.OpeningCash,
            shiftRow.InvoiceCount,
            shiftRow.Revenue,
            shiftRow.CashRevenue,
            shiftRow.TransferRevenue,
            shiftRow.DiscountTotal,
            shiftRow.ServedTableCount,

            shiftRow.CountedCash,
            shiftRow.ExpectedCash,
            shiftRow.CashDifference,
            shiftRow.Explanation

        FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER
    )
    FROM dbo.ShiftEvents eventRow
    INNER JOIN inserted newEvent
        ON newEvent.Id = eventRow.Id
    INNER JOIN dbo.Shifts shiftRow
        ON shiftRow.Id = eventRow.ShiftId
    INNER JOIN dbo.Users closingUser
        ON closingUser.Id = eventRow.ActorUserId
    CROSS JOIN dbo.RestaurantSettings restaurant
    WHERE newEvent.Action = 'Closed'
      AND restaurant.Id = 1;
END;
GO