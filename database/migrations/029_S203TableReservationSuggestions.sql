-- S2-03 Task 2: suggestions for the same table after a concurrent/overlap conflict.
CREATE OR ALTER PROCEDURE dbo.usp_GetManagedTableReservationSuggestions
    @TableId int,
    @ReservationDate date,
    @DesiredStart time(0),
    @MaxSuggestions int = 3
AS
BEGIN
    SET NOCOUNT ON;

    IF @MaxSuggestions IS NULL OR @MaxSuggestions < 1 SET @MaxSuggestions = 3;
    IF @MaxSuggestions > 3 SET @MaxSuggestions = 3;
    IF NOT EXISTS (SELECT 1 FROM dbo.DiningTables WHERE Id=@TableId AND IsActive=1) RETURN;
    IF EXISTS (SELECT 1 FROM dbo.SpecialHolidays WHERE HolidayDate=@ReservationDate AND IsActive=1) RETURN;

    DECLARE @weekday int = (DATEDIFF(day, CONVERT(date, '19000101'), @ReservationDate) % 7) + 1;
    DECLARE @opens time(0), @closes time(0), @isClosed bit;
    SELECT @opens=OpensAt, @closes=ClosesAt, @isClosed=IsClosed
    FROM dbo.OpeningHours WHERE DayOfWeek=@weekday;
    IF @isClosed=1 OR @opens IS NULL OR @closes IS NULL RETURN;

    DECLARE @duration int=(SELECT DefaultBookingMinutes FROM dbo.RestaurantSettings WHERE Id=1);
    IF @duration IS NULL OR @duration<=0 SET @duration=90;
    DECLARE @candidate time(0)=@opens;
    DECLARE @candidates TABLE (StartsAt datetime2(3) NOT NULL, StartsAtLocal datetime2(3) NOT NULL);

    WHILE @candidate < @closes
    BEGIN
        DECLARE @candidateLocal datetime2(3)=DATEADD(minute, DATEDIFF(minute, 0, @candidate), CONVERT(datetime2(3), @ReservationDate));
        DECLARE @candidateEndLocal datetime2(3)=DATEADD(minute, @duration, @candidateLocal);
        DECLARE @candidateUtc datetime2(3)=DATEADD(hour, -7, @candidateLocal);
        DECLARE @candidateEndUtc datetime2(3)=DATEADD(minute, @duration, @candidateUtc);

        IF CONVERT(time(0), @candidateEndLocal) <= @closes
           AND NOT EXISTS
           (
               SELECT 1 FROM dbo.Reservations r
               WHERE r.TableId=@TableId AND r.Status IN ('Pending','Confirmed')
                 AND r.StartsAt < DATEADD(minute, 15, @candidateEndUtc)
                 AND @candidateUtc < DATEADD(minute, 15, r.EndsAt)
           )
            INSERT @candidates(StartsAt, StartsAtLocal) VALUES(@candidateUtc, @candidateLocal);

        SET @candidate=DATEADD(minute, 30, @candidate);
    END;

    DECLARE @desiredLocal datetime2(3)=DATEADD(minute, DATEDIFF(minute, 0, @DesiredStart), CONVERT(datetime2(3), @ReservationDate));
    SELECT TOP (@MaxSuggestions) CONVERT(char(5), StartsAtLocal, 108) AS StartTime
    FROM @candidates
    ORDER BY ABS(DATEDIFF(minute, @desiredLocal, StartsAtLocal)), StartsAtLocal;
END;
GO
GRANT EXECUTE ON dbo.usp_GetManagedTableReservationSuggestions TO restaurant_app;
GO
