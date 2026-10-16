-- PO: exactly ten minutes is not overdue. Use the latest status transition in UTC.
CREATE OR ALTER FUNCTION dbo.fn_IsCleaningOverdue
(@Status varchar(20), @StartedAt datetime2(3), @Now datetime2(3))
RETURNS bit
AS
BEGIN
 RETURN CASE WHEN @Status='Cleaning' AND DATEADD(minute,10,@StartedAt)<@Now THEN 1 ELSE 0 END;
END;
GO
GRANT EXECUTE ON dbo.fn_IsCleaningOverdue TO restaurant_app;
GO
