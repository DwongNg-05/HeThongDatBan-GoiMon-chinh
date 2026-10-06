-- Keep the live table-map state aligned with confirmed reservations.
-- A reservation holds capacity from creation, but a table becomes "Reserved"
-- on the staff map only when the confirmed booking is due within 30 minutes.
CREATE OR ALTER PROCEDURE dbo.usp_SyncReservationTableStatuses
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;

        DECLARE @now datetime2(3) = SYSUTCDATETIME();

        UPDATE t
        SET Status = CASE
            WHEN EXISTS (
                SELECT 1
                FROM dbo.Reservations r
                WHERE r.TableId = t.Id
                  AND r.Status = 'Confirmed'
                  AND r.StartsAt <= DATEADD(minute, 30, @now)
                  AND r.EndsAt > @now)
            THEN 'Reserved'
            ELSE 'Available'
        END
        FROM dbo.DiningTables t
        WHERE t.IsActive = 1
          AND t.Status IN ('Available', 'Reserved')
          AND t.Status <> CASE
            WHEN EXISTS (
                SELECT 1
                FROM dbo.Reservations r
                WHERE r.TableId = t.Id
                  AND r.Status = 'Confirmed'
                  AND r.StartsAt <= DATEADD(minute, 30, @now)
                  AND r.EndsAt > @now)
            THEN 'Reserved'
            ELSE 'Available'
          END;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

-- Run the same synchronization immediately whenever a reservation is created,
-- confirmed, moved to another table, cancelled, rejected, or marked no-show.
CREATE OR ALTER TRIGGER dbo.tr_Reservations_SyncTableMapStatus
ON dbo.Reservations
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted WHERE TableId IS NOT NULL)
       AND NOT EXISTS (SELECT 1 FROM deleted WHERE TableId IS NOT NULL)
        RETURN;

    DECLARE @now datetime2(3) = SYSUTCDATETIME();

    ;WITH changedTables AS (
        SELECT TableId FROM inserted WHERE TableId IS NOT NULL
        UNION
        SELECT TableId FROM deleted WHERE TableId IS NOT NULL
    ), desiredStatuses AS (
        SELECT t.Id,
            CASE WHEN EXISTS (
                SELECT 1
                FROM dbo.Reservations r
                WHERE r.TableId = t.Id
                  AND r.Status = 'Confirmed'
                  AND r.StartsAt <= DATEADD(minute, 30, @now)
                  AND r.EndsAt > @now)
            THEN 'Reserved' ELSE 'Available' END AS Status
        FROM dbo.DiningTables t
        JOIN changedTables c ON c.TableId = t.Id
        WHERE t.IsActive = 1 AND t.Status IN ('Available', 'Reserved')
    )
    UPDATE t
    SET Status = desired.Status
    FROM dbo.DiningTables t
    JOIN desiredStatuses desired ON desired.Id = t.Id
    WHERE t.Status <> desired.Status;
END;
GO

GRANT EXECUTE ON dbo.usp_SyncReservationTableStatuses TO restaurant_app;
GO

-- Apply the correction to reservations that were confirmed before this migration.
EXEC dbo.usp_SyncReservationTableStatuses;
GO
