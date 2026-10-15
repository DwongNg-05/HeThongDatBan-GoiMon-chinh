CREATE OR ALTER PROCEDURE dbo.usp_CloseShiftReviewed
    @ShiftId bigint,
    @CountedCash decimal(18,0),
    @ActorUserId int,
    @ExpectedInvoiceCount int,
    @ExpectedCashRevenue decimal(18,0),
    @ExpectedTransferRevenue decimal(18,0),
    @ExpectedDiscountTotal decimal(18,0),
    @ExpectedServedTableCount int,
    @ExpectedOpeningCash decimal(18,0),
    @Explanation nvarchar(500) = NULL
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

        IF @CountedCash IS NULL OR @CountedCash < 0
            THROW 51049,
                N'Tiền kiểm đếm không hợp lệ.',
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

        IF @ExpectedInvoiceCount IS NULL
            OR @ExpectedCashRevenue IS NULL
            OR @ExpectedTransferRevenue IS NULL
            OR @ExpectedDiscountTotal IS NULL
            OR @ExpectedServedTableCount IS NULL
            OR @ExpectedOpeningCash IS NULL
            THROW 51052,
                N'Thiếu số liệu đối chiếu. Vui lòng tải lại màn hình.',
                1;

        DECLARE
            @invoiceCount int,
            @cashRevenue decimal(18,0),
            @transferRevenue decimal(18,0),
            @discountTotal decimal(18,0),
            @servedTableCount int,
            @openingCash decimal(18,0);

        SELECT
            @invoiceCount = COUNT(*),
            @discountTotal = COALESCE(SUM(DiscountAmount), 0)
        FROM dbo.Invoices
        WHERE ShiftId = @ShiftId
          AND Status = 'Paid';

        SELECT
            @cashRevenue = COALESCE(
                SUM(CASE
                    WHEN p.Method = 'Cash' THEN p.Amount
                    ELSE 0
                END), 0),
            @transferRevenue = COALESCE(
                SUM(CASE
                    WHEN p.Method = 'Transfer' THEN p.Amount
                    ELSE 0
                END), 0)
        FROM dbo.Invoices i
        JOIN dbo.Payments p ON p.InvoiceId = i.Id
        WHERE i.ShiftId = @ShiftId
          AND i.Status = 'Paid';

        SELECT @servedTableCount = COUNT(*)
        FROM dbo.SessionTables st
        JOIN dbo.DiningSessions s ON s.Id = st.SessionId
        WHERE s.ShiftId = @ShiftId;

        SELECT @openingCash = OpeningCash
        FROM dbo.Shifts
        WHERE Id = @ShiftId;

        IF @invoiceCount <> @ExpectedInvoiceCount
            OR @cashRevenue <> @ExpectedCashRevenue
            OR @transferRevenue <> @ExpectedTransferRevenue
            OR @discountTotal <> @ExpectedDiscountTotal
            OR @servedTableCount <> @ExpectedServedTableCount
            OR @openingCash <> @ExpectedOpeningCash
            THROW 51052,
                N'Số liệu ca đã thay đổi. Vui lòng đối chiếu lại số liệu mới và nhập lại tiền thực đếm trước khi chốt.',
                1;

        SET @Explanation = NULLIF(
            LTRIM(RTRIM(@Explanation)), N'');

        IF ABS(
            @CountedCash - (@openingCash + @cashRevenue)
        ) > 50000 AND @Explanation IS NULL
            THROW 51051,
                N'Chênh lệch quá 50.000 VND phải có giải trình.',
                1;

        EXEC dbo.usp_CloseShift
            @ShiftId = @ShiftId,
            @CountedCash = @CountedCash,
            @ActorUserId = @ActorUserId,
            @Explanation = @Explanation;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK;

        THROW;
    END CATCH
END;
GO