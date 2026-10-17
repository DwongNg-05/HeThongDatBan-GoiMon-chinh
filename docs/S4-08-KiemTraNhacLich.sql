-- S4-08 Task 1: kiểm tra tác vụ email nhắc trước giờ hẹn 2 tiếng ngay trên database dev (SSMS), sau khi chạy migrate.
-- Script chạy trong một giao dịch và luôn ROLLBACK ở cuối: không để lại lượt đặt bàn hay email nào.
-- Kết quả: mỗi dòng PASS/FAIL; gặp FAIL thì dừng và rollback.
SET NOCOUNT ON; SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @now datetime2(3)=SYSUTCDATETIME();
-- Hai bàn trống trong 6 giờ tới (tránh trùng giờ với lượt đặt có sẵn).
DECLARE @tables TABLE(N int IDENTITY,Id int,Code varchar(20));
INSERT @tables(Id,Code)
 SELECT TOP(2) t.Id,t.Code FROM dbo.DiningTables t
 WHERE NOT EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status IN ('Pending','Confirmed','Arrived')
   AND r.StartsAt<DATEADD(hour,6,@now) AND DATEADD(minute,15,r.EndsAt)>DATEADD(hour,-1,@now))
 ORDER BY t.Id;
IF (SELECT COUNT(*) FROM @tables)<2 THROW 50000,N'Cần ít nhất 2 bàn trống trong 6 giờ tới để chạy kiểm tra.',1;
DECLARE @t1 int=(SELECT Id FROM @tables WHERE N=1),@t2 int=(SELECT Id FROM @tables WHERE N=2);
DECLARE @t1Code varchar(20)=(SELECT Code FROM @tables WHERE N=1);

-- A: đã xác nhận, còn 1 tiếng 59 phút  -> phải được nhắc.
-- B: đã xác nhận, còn 2 tiếng 5 phút   -> CHƯA tới mốc nhắc, không gửi.
-- C: đã xác nhận, còn 30 phút nhưng không có email -> không gửi.
-- D: chờ xác nhận, còn 1 tiếng         -> không gửi.
INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,TableId,StartsAt,EndsAt,Status,ConfirmedAt)
VALUES ('S48A01',N'Khách A','0900000001',N's408-a@example.com',2,@t1,DATEADD(minute,119,@now),DATEADD(minute,209,@now),'Confirmed',@now),
       ('S48B01',N'Khách B','0900000002',N's408-b@example.com',2,@t2,DATEADD(minute,125,@now),DATEADD(minute,215,@now),'Confirmed',@now),
       ('S48C01',N'Khách C','0900000003',NULL,2,@t2,DATEADD(minute,30,@now),DATEADD(minute,60,@now),'Confirmed',@now),
       ('S48D01',N'Khách D','0900000004',N's408-d@example.com',2,NULL,DATEADD(minute,60,@now),DATEADD(minute,150,@now),'Pending',NULL);

DECLARE @a bigint=(SELECT Id FROM dbo.Reservations WHERE Code='S48A01'),
        @b bigint=(SELECT Id FROM dbo.Reservations WHERE Code='S48B01'),
        @c bigint=(SELECT Id FROM dbo.Reservations WHERE Code='S48C01'),
        @d bigint=(SELECT Id FROM dbo.Reservations WHERE Code='S48D01');

DECLARE @queued int;
EXEC dbo.usp_QueueDueBookingReminders @QueuedCount=@queued OUTPUT;
IF NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE ReservationId=@a AND MessageType='BookingReminder' AND Status='Pending'
              AND Recipient=N's408-a@example.com')
 THROW 50001,N'FAIL: lượt đã xác nhận còn 2 tiếng phải được xếp email nhắc tới email của lượt đặt.',1;
PRINT N'PASS: lượt đã xác nhận còn 2 tiếng được xếp email nhắc, gửi tới email của lượt đặt bàn';
IF EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE ReservationId IN (@b,@c,@d) AND MessageType='BookingReminder')
 THROW 50002,N'FAIL: lượt chưa tới mốc nhắc / không có email / chưa xác nhận không được xếp email nhắc.',1;
PRINT N'PASS: lượt chưa tới mốc nhắc, không có email hoặc chưa xác nhận không được xếp email nhắc';

DECLARE @payload nvarchar(max)=(SELECT PayloadJson FROM dbo.EmailOutbox WHERE ReservationId=@a AND MessageType='BookingReminder');
IF JSON_VALUE(@payload,'$.Code')<>'S48A01' OR JSON_VALUE(@payload,'$.TableCode')<>@t1Code OR JSON_VALUE(@payload,'$.StartsAt') IS NULL
 THROW 50003,N'FAIL: nội dung email nhắc phải có mã đặt bàn, giờ hẹn và bàn đã xếp.',1;
PRINT N'PASS: nội dung email nhắc có mã đặt bàn, giờ hẹn và bàn đã xếp';
IF (SELECT ReminderEmailStatus FROM dbo.Reservations WHERE Id=@a)<>'Pending'
 THROW 50004,N'FAIL: lượt đặt bàn phải có trạng thái gửi nhắc "Pending".',1;
PRINT N'PASS: trạng thái gửi nhắc của lượt đặt bàn = Pending';

-- Web nhận email nhắc để gửi, rồi ghi kết quả thành công.
-- Email nhắc có sẵn của dữ liệu thật được dời giờ gửi trong giao dịch này (rollback ở cuối) để lượt A được nhận trước.
UPDATE dbo.EmailOutbox SET NextAttemptAt=DATEADD(day,1,@now)
 WHERE MessageType='BookingReminder' AND Status='Pending' AND ReservationId<>@a;
DECLARE @claimed TABLE(Id bigint,AttemptCount int,Recipient nvarchar(254),Subject nvarchar(250),PayloadJson nvarchar(max),MessageType varchar(30),ReservationId bigint);
INSERT @claimed EXEC dbo.usp_ClaimDueBookingReminder;
DECLARE @emailId bigint=(SELECT Id FROM @claimed WHERE ReservationId=@a);
IF @emailId IS NULL THROW 50005,N'FAIL: usp_ClaimDueBookingReminder phải trả về email nhắc của lượt A.',1;
IF (SELECT ReminderEmailStatus FROM dbo.Reservations WHERE Id=@a)<>'Sending'
 THROW 50006,N'FAIL: đang gửi thì trạng thái gửi nhắc = Sending.',1;
EXEC dbo.usp_CompleteEmail @EmailId=@emailId,@AttemptNumber=1,@Succeeded=1;
IF NOT EXISTS(SELECT 1 FROM dbo.Reservations r JOIN dbo.EmailOutbox e ON e.Id=@emailId
              WHERE r.Id=@a AND r.ReminderEmailStatus='Sent' AND r.ReminderEmailSentAt IS NOT NULL AND r.ReminderEmailSentAt=e.SentAt)
 THROW 50007,N'FAIL: gửi thành công phải ghi thời điểm gửi lên lượt đặt bàn.',1;
PRINT N'PASS: gửi thành công ghi trạng thái Sent và thời điểm gửi lên lượt đặt bàn';

-- Chạy lại tác vụ: không xếp thêm email nhắc cho lượt A.
EXEC dbo.usp_QueueDueBookingReminders @QueuedCount=@queued OUTPUT;
IF (SELECT COUNT(*) FROM dbo.EmailOutbox WHERE ReservationId=@a AND MessageType='BookingReminder')<>1
 THROW 50008,N'FAIL: chạy lại tác vụ không được xếp thêm email nhắc cho cùng lượt.',1;
PRINT N'PASS: chạy lại tác vụ không xếp thêm email nhắc cho lượt đã nhắc';

-- 6 phút sau, lượt B (còn 2 tiếng 5 phút lúc đầu) tới mốc nhắc.
DECLARE @later datetime2(3)=DATEADD(minute,6,@now);
EXEC dbo.usp_QueueDueBookingReminders @Now=@later,@QueuedCount=@queued OUTPUT;
IF NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE ReservationId=@b AND MessageType='BookingReminder')
 THROW 50009,N'FAIL: lượt B phải được xếp email nhắc khi tới mốc 2 tiếng.',1;
PRINT N'PASS: lượt chưa tới mốc được nhắc khi tới đúng mốc 2 tiếng';

PRINT N'Tất cả kiểm tra S4-08 Task 1 đã PASS. Rollback dữ liệu kiểm tra.';
ROLLBACK;
