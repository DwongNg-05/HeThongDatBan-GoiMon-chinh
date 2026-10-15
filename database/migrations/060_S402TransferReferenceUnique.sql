-- S4-02 Task 2: một mã ngân hàng chỉ được dùng cho một hoá đơn chuyển khoản.
-- Giá trị được chuẩn hoá chữ hoa ở CashierController trước khi gọi usp_Checkout.
IF EXISTS (
    SELECT TransferReference FROM dbo.Payments
    WHERE Method='Transfer' AND TransferReference IS NOT NULL
    GROUP BY TransferReference HAVING COUNT(*) > 1)
    THROW 51058, N'Có mã tham chiếu chuyển khoản bị trùng trong dữ liệu cũ; cần xử lý trước khi áp dụng ràng buộc.', 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Payments') AND name=N'UX_Payments_TransferReference')
    CREATE UNIQUE INDEX UX_Payments_TransferReference ON dbo.Payments(TransferReference) WHERE Method='Transfer' AND TransferReference IS NOT NULL;
GO
