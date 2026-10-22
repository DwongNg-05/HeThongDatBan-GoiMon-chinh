CREATE OR ALTER PROCEDURE dbo.usp_CloseShiftAutomatically
    @ShiftId bigint,
    @ActorUserId int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        EXEC dbo.usp_LockOperations;

        EXEC dbo.usp_RequirePermission
            @ActorUserId,
            'Payments.Manage';

        IF NOT EXISTS (
            SELECT 1
            FROM dbo.Shifts
            WHERE Id = @ShiftId
              AND Status = 'Open'
        )
            THROW 51048,
                N'Ca không còn đang mở. Vui lòng tải lại màn hình.',
                1;

        IF EXISTS (
            SELECT 1
            FROM dbo.DiningSessions
            WHERE ShiftId = @ShiftId
              AND Status <> 'Closed'
        )
            THROW 51050,
                N'Còn bàn đang phục vụ hoặc chưa thanh toán. Vui lòng cập nhật số liệu để xem danh sách bàn.',
                1;

        DECLARE
            @openingCash decimal(18,0),
            @cashRevenue decimal(18,0),
            @cashRequired decimal(18,0);

        SELECT @openingCash = OpeningCash
        FROM dbo.Shifts
        WHERE Id = @ShiftId;

        SELECT @cashRevenue = COALESCE(SUM(p.Amount), 0)
        FROM dbo.Invoices AS i
        JOIN dbo.Payments AS p ON p.InvoiceId = i.Id
        WHERE i.ShiftId = @ShiftId
          AND i.Status = 'Paid'
          AND p.Method = 'Cash';

        SET @cashRequired = @openingCash + @cashRevenue;

        -- Không yêu cầu kiểm đếm thủ công: số tiền được lưu bằng số tiền hệ thống tính.
        EXEC dbo.usp_CloseShift
            @ShiftId = @ShiftId,
            @CountedCash = @cashRequired,
            @ActorUserId = @ActorUserId,
            @Explanation = NULL;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK;

        THROW;
    END CATCH
END;
GO
