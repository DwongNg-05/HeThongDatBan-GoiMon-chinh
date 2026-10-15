USE RestaurantManagement_Dev;
SELECT Code, CustomerName, TableCode, DATEADD(hour,7,StartsAt) AS StartsAtVN, DATEADD(hour,7,WarnAt) AS WarnAtVN, LateMinutes, Status
FROM dbo.fn_LateReservationAlerts(SYSUTCDATETIME());