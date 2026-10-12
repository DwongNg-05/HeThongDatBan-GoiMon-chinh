-- S3-02 Task 2: every order-entry path, including direct SQL calls, has the
-- same per-dish maximum as the customer cart.
CREATE OR ALTER TRIGGER dbo.tr_OrderItems_QuantityMaximum
ON dbo.OrderItems
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted WHERE Quantity NOT BETWEEN 1 AND 20)
        THROW 51820, N'Số lượng mỗi món phải là số nguyên từ 1 đến 20 phần.', 1;
END;
GO
