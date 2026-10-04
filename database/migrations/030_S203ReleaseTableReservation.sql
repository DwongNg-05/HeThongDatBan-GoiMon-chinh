-- S2-03 Task 3: manager release of a table reservation.
CREATE OR ALTER PROCEDURE dbo.usp_CancelManagedTableReservation
    @ReservationId bigint,
    @ActorUserId int,
    @Reason nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Reservations.Manage';

        DECLARE @fromStatus varchar(20), @tableId int;
        SELECT @fromStatus=Status, @tableId=TableId FROM dbo.Reservations WHERE Id=@ReservationId;
        IF @fromStatus IS NULL THROW 51064, N'Lượt đặt không tồn tại.', 1;
        IF @fromStatus='Cancelled' THROW 51064, N'Lượt đặt này đã được huỷ trước đó.', 1;
        IF @fromStatus='NoShow' THROW 51064, N'Lượt đặt này đã được đánh dấu khách không tới.', 1;
        IF @fromStatus NOT IN ('Pending', 'Confirmed') THROW 51064, N'Chỉ có thể huỷ lượt đang chờ xác nhận hoặc đã xác nhận.', 1;

        UPDATE dbo.Reservations
        SET Status='Cancelled', CancelledAt=SYSUTCDATETIME(), CancelReason=NULLIF(LTRIM(RTRIM(@Reason)), '')
        WHERE Id=@ReservationId;
        INSERT dbo.ReservationEvents(ReservationId, FromStatus, ToStatus, OldTableId, ActorUserId, Reason)
        VALUES(@ReservationId, @fromStatus, 'Cancelled', @tableId, @ActorUserId, NULLIF(LTRIM(RTRIM(@Reason)), ''));
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_CancelManagedTableReservation TO restaurant_app;
GO

CREATE OR ALTER PROCEDURE dbo.usp_MarkManagedReservationNoShow
    @ReservationId bigint,
    @ActorUserId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC dbo.usp_LockOperations;
        EXEC dbo.usp_RequirePermission @ActorUserId, 'Reservations.Manage';

        DECLARE @fromStatus varchar(20), @tableId int, @startsAt datetime2(3);
        SELECT @fromStatus=Status, @tableId=TableId, @startsAt=StartsAt FROM dbo.Reservations WHERE Id=@ReservationId;
        IF @fromStatus IS NULL THROW 51064, N'Lượt đặt không tồn tại.', 1;
        IF @fromStatus='Cancelled' THROW 51064, N'Lượt đặt này đã được huỷ nên không thể đánh dấu khách không tới.', 1;
        IF @fromStatus='NoShow' THROW 51064, N'Lượt đặt này đã được đánh dấu khách không tới trước đó.', 1;
        IF @fromStatus NOT IN ('Pending', 'Confirmed') THROW 51064, N'Chỉ có thể đánh dấu khách không tới với lượt đang chờ xác nhận hoặc đã xác nhận.', 1;
        IF @startsAt >= SYSUTCDATETIME() THROW 51065, N'Chỉ được đánh dấu khách không tới sau giờ hẹn.', 1;

        UPDATE dbo.Reservations SET Status='NoShow', NoShowAt=SYSUTCDATETIME() WHERE Id=@ReservationId;
        INSERT dbo.ReservationEvents(ReservationId, FromStatus, ToStatus, OldTableId, ActorUserId)
        VALUES(@ReservationId, @fromStatus, 'NoShow', @tableId, @ActorUserId);
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH
END;
GO
GRANT EXECUTE ON dbo.usp_MarkManagedReservationNoShow TO restaurant_app;
GO
