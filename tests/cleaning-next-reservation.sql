-- Run only in Verify-Cleaning's disposable database.
DECLARE @a int=(SELECT TOP(1) Id FROM dbo.Areas),@now datetime2(3)=SYSUTCDATETIME();
INSERT dbo.DiningTables(AreaId,Code,MaxCapacity,Status)
VALUES(@a,'NEXT1',4,'Cleaning'),(@a,'CANCEL1',4,'Cleaning'),(@a,'FAR1',4,'Cleaning'),
(@a,'MULTI1',4,'Cleaning'),(@a,'PENDING1',4,'Cleaning'),(@a,'ARRIVE1',4,'Cleaning'),
(@a,'PAST1',4,'Cleaning'),(@a,'BOUND1',4,'Cleaning');
INSERT dbo.Reservations(Code,CustomerName,Phone,GuestCount,TableId,StartsAt,EndsAt,Status,ArrivedAt)
SELECT r.Code,r.Guest,'0900000099',2,t.Id,DATEADD(minute,r.OffsetMinutes,@now),DATEADD(minute,r.OffsetMinutes+5,@now),r.Status,
CASE WHEN r.Status='Arrived' THEN @now ELSE NULL END
FROM (VALUES
('NX0001','NEXT1',N'Khách kế tiếp',15,'Confirmed'),
('NX0002','CANCEL1',N'Khách đã hủy',15,'Cancelled'),
('NX0003','FAR1',N'Khách ngoài 30 phút',40,'Confirmed'),
('NX0004','MULTI1',N'Khách đến sau',28,'Confirmed'),
('NX0005','MULTI1',N'Khách đến trước',5,'Confirmed'),
('NX0006','PENDING1',N'Khách chờ xác nhận',15,'Pending'),
('NX0007','ARRIVE1',N'Khách đã tới',15,'Arrived'),
('NX0008','PAST1',N'Khách quá giờ',-10,'Confirmed'),
('NX0009','BOUND1',N'Khách đúng biên',30,'Confirmed')
) r(Code,TableCode,Guest,OffsetMinutes,Status) JOIN dbo.DiningTables t ON t.Code=r.TableCode;
DECLARE @boundary int=(SELECT Id FROM dbo.DiningTables WHERE Code='BOUND1');
IF NOT EXISTS(SELECT 1 FROM dbo.fn_NextTableReservation(@boundary,@now)) THROW 51999,'Exactly 30 minutes must qualify',1;
IF EXISTS(SELECT 1 FROM dbo.fn_NextTableReservation(@boundary,DATEADD(millisecond,-1,@now))) THROW 51999,'Beyond 30 minutes must not qualify',1;
IF NOT EXISTS(SELECT 1 FROM dbo.fn_NextTableReservation(@boundary,DATEADD(minute,30,@now))) THROW 51999,'Exactly now must qualify',1;
IF EXISTS(SELECT 1 FROM dbo.fn_NextTableReservation(@boundary,DATEADD(millisecond,1,DATEADD(minute,30,@now)))) THROW 51999,'Past appointment must not qualify',1;
PRINT 'PASS: inclusive now/30-minute boundaries and exclusion beyond boundaries';
