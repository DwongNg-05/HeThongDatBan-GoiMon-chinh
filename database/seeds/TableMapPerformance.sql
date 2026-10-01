-- Only used by the guarded performance command on its own disposable database.
IF DB_NAME() NOT LIKE 'RestaurantManagement[_]Perf[_]%'
 THROW 51500,'Never load the performance fixture into a real restaurant database.',1;
IF EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE Name='TABLE_MAP_PERFORMANCE') RETURN;
DECLARE @manager int=(SELECT Id FROM dbo.Users WHERE UserName='manager');
DECLARE @waiter int=(SELECT Id FROM dbo.Users WHERE UserName='waiter');
-- Disposable fixture passwords are supplied by the runner; no first-login password change in timed runs.
UPDATE dbo.Users SET MustChangePassword=0 WHERE Id IN (@manager,@waiter);
DECLARE @garden int=(SELECT Id FROM dbo.Areas WHERE Name=N'Sân vườn');
DECLARE @number int=61;
WHILE @number<=@TargetCount
BEGIN
 INSERT dbo.DiningTables(AreaId,Code,MinCapacity,MaxCapacity,SortOrder)
 VALUES(@garden,CONCAT('C',@number-40),1,CASE WHEN @number%3=0 THEN 6 ELSE 4 END,@number);
 SET @number+=1;
END;
DECLARE @now datetime2(3)=SYSUTCDATETIME();
INSERT dbo.Shifts(Name,BusinessDate,OpenedBy,OpeningCash)
 VALUES(N'Ca đo hiệu năng',CONVERT(date,DATEADD(hour,7,@now)),@manager,500000);
DECLARE @shift bigint=SCOPE_IDENTITY();
DECLARE @table int,@area int,@capacity int,@status varchar(20),@reservation bigint,@session bigint,@batch bigint,@start datetime2(3);
DECLARE tables CURSOR LOCAL FAST_FORWARD FOR SELECT Id,AreaId,MaxCapacity FROM dbo.DiningTables ORDER BY Id;
OPEN tables;
FETCH NEXT FROM tables INTO @table,@area,@capacity;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @status=CASE @table%4 WHEN 1 THEN 'Available' WHEN 2 THEN 'Reserved' WHEN 3 THEN 'Serving' ELSE 'Cleaning' END;
 SET @reservation=NULL;
 IF @status IN ('Reserved','Serving')
 BEGIN
  SET @start=DATEADD(minute,CASE WHEN @status='Serving' THEN -20 ELSE 60 END,@now);
  INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,PreferredAreaId,TableId,StartsAt,EndsAt,Status,ConfirmedBy,ConfirmedAt)
  VALUES(CONCAT('P',RIGHT(CONCAT('00000',@table),5)),CONCAT(N'Khách thử bàn ',@table),'0900000099',3,@area,@table,@start,DATEADD(minute,90,@start),'Confirmed',@manager,@now);
  SET @reservation=SCOPE_IDENTITY();
 END;
 IF @status='Serving'
 BEGIN
  INSERT dbo.DiningSessions(ReservationId,ShiftId,GuestCount,OpenedBy,OpenedAt) VALUES(@reservation,@shift,3,@waiter,DATEADD(minute,-20,@now));
  SET @session=SCOPE_IDENTITY();
  INSERT dbo.SessionTables(SessionId,TableId) VALUES(@session,@table);
  UPDATE dbo.Reservations SET Status='Arrived',ArrivedAt=@now WHERE Id=@reservation;
  INSERT dbo.OrderBatches(SessionId,BatchNumber,RequestId,CreatedBy) VALUES(@session,1,NEWID(),@waiter);
  SET @batch=SCOPE_IDENTITY();
  INSERT dbo.OrderItems(BatchId,MenuItemId,OriginalTableId,ItemName,Unit,UnitPrice,Quantity,EstimatedPrepMinutes,Status,ServedAt,ServedBy)
  SELECT TOP(3) @batch,Id,@table,Name,Unit,Price,CASE WHEN Id%2=0 THEN 2 ELSE 1 END,EstimatedPrepMinutes,
   CASE Id%3 WHEN 0 THEN 'Served' WHEN 1 THEN 'Ready' ELSE 'Pending' END,
   CASE WHEN Id%3=0 THEN @now END,CASE WHEN Id%3=0 THEN @waiter END
  FROM dbo.MenuItems ORDER BY Id;
 END;
 UPDATE dbo.DiningTables SET Status=@status WHERE Id=@table;
 FETCH NEXT FROM tables INTO @table,@area,@capacity;
END;
CLOSE tables;
DEALLOCATE tables;
INSERT dbo.SchemaVersions(Name,Sha256) VALUES('TABLE_MAP_PERFORMANCE',REPLICATE('0',64));
