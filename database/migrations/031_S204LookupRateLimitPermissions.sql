-- S2-04: public reservation lookup is executed through stored procedures.
-- Grant only the two required procedures to the least-privilege application role.
GRANT EXECUTE ON dbo.usp_GetReservationLookupRateLimit TO restaurant_app;
GRANT EXECUTE ON dbo.usp_RecordReservationLookupAttempt TO restaurant_app;
GO
