-- S4-08 Task 2: kiểm tra việc bỏ qua email nhắc cho lượt đặt bàn không còn hợp lệ (chạy trong SSMS sau khi migrate).
-- Script chạy trong một giao dịch và luôn ROLLBACK ở cuối: không để lại dữ liệu. Gặp FAIL thì dừng và rollback.
SET NOCOUNT ON; SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @now datetime2(3)=SYSUTCDATETIME();
DECLARE @tables TABLE(N int IDENTITY,Id int);
INSERT @tables(Id)
 SELECT TOP(4) t.Id FROM dbo.DiningTables t
 WHERE NOT EXISTS(SELECT 1 FROM dbo.Reservations r WHERE r.TableId=t.Id AND r.Status IN ('Pending','Confirmed','Arrived')
   AND r.StartsAt<DATEADD(hour,6,@now) AND DATEADD(minute,15,r.EndsAt)>DATEADD(hour,-1,@now))
 ORDER BY t.Id;
IF (SELECT COUNT(*) FROM @tables)<4 THROW 50000,N'Cần ít nhất 4 bàn trống trong 6 giờ tới để chạy kiểm tra.',1;
DECLARE @actor int=(SELECT TOP(1) Id FROM dbo.Users ORDER BY Id);

-- A: sẽ bị huỷ; B: khách sẽ tới; C: vẫn đã xác nhận; D: bị huỷ sau khi web đã nhận email, ngay trước lúc gửi.
INSERT dbo.Reservations(Code,CustomerName,Phone,Email,GuestCount,TableId,StartsAt,EndsAt,Status,ConfirmedAt)
SELECT v.Code,v.Name,v.Phone,v.Email,2,t.Id,DATEADD(minute,v.M,@now),DATEADD(minute,v.M+90,@now),'Confirmed',@now
FROM (VALUES (1,'S2A001',N'Khách A','0900000011',N's408t2-a@example.com',100),
             (2,'S2B001',N'Khách B','0900000012',N's408t2-b@example.com',100),
             (3,'S2C001',N'Khách C','0900000013',N's408t2-c@example.com',90),
             (4,'S2D001',N'Khách D','0900000014',N's408t2-d@example.com',95)) v(N,Code,Name,Phone,Email,M)
JOIN @tables t ON t.N=v.N;
DECLARE @a bigint=(SELECT Id FROM dbo.Reservations WHERE Code='S2A001'),@b bigint=(SELECT Id FROM dbo.Reservations WHERE Code='S2B001'),
        @c bigint=(SELECT Id FROM dbo.Reservations WHERE Code='S2C001'),@d bigint=(SELECT Id FROM dbo.Reservations WHERE Code='S2D001');

EXEC dbo.usp_QueueDueBookingReminders;
IF (SELECT COUNT(*) FROM dbo.EmailOutbox WHERE ReservationId IN (@a,@b,@c,@d) AND MessageType='BookingReminder' AND Status='Pending')<>4
 THROW 50001,N'FAIL: 4 lượt đã xác nhận phải được xếp email nhắc.',1;
PRINT N'PASS: 4 lượt đã xác nhận được xếp email nhắc';

-- Đổi trạng thái sau khi đã xếp hàng: A huỷ, B khách đã tới.
UPDATE dbo.Reservations SET Status='Cancelled',CancelledAt=SYSUTCDATETIME(),CancelReason=N'Kiểm tra S4-08 Task 2' WHERE Id=@a;
IF @actor IS NOT NULL
 BEGIN EXEC dbo.usp_MarkReservationArrived @ReservationId=@b,@ActorUserId=@actor; END
ELSE
 BEGIN UPDATE dbo.Reservations SET Status='Arrived',ArrivedAt=SYSUTCDATETIME() WHERE Id=@b; END
-- Email nhắc có sẵn của dữ liệu thật được dời giờ gửi trong giao dịch này để chỉ nhận email của lượt kiểm tra.
UPDATE dbo.EmailOutbox SET NextAttemptAt=DATEADD(day,1,@now)
 WHERE MessageType='BookingReminder' AND Status='Pending' AND ReservationId NOT IN (@a,@b,@c,@d);

DECLARE @claimed TABLE(Id bigint,AttemptCount int,Recipient nvarchar(254),Subject nvarchar(250),PayloadJson nvarchar(max),MessageType varchar(30),ReservationId bigint);
INSERT @claimed EXEC dbo.usp_ClaimDueBookingReminder;
IF ISNULL((SELECT ReservationId FROM @claimed),-1)<>@c THROW 50002,N'FAIL: email đầu tiên được nhận phải là của lượt C (vẫn đã xác nhận).',1;
IF NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE ReservationId=@a AND MessageType='BookingReminder' AND Status='Cancelled' AND SkipReason='Cancelled')
 OR NOT EXISTS(SELECT 1 FROM dbo.Reservations WHERE Id=@a AND ReminderEmailStatus='Cancelled' AND ReminderSkipReason='Cancelled')
 THROW 50003,N'FAIL: lượt đã huỷ phải bị bỏ qua với lý do Cancelled.',1;
PRINT N'PASS: lượt đã huỷ không nhận email, lý do bỏ qua = Cancelled';
IF NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE ReservationId=@b AND MessageType='BookingReminder' AND Status='Cancelled' AND SkipReason='Arrived')
 OR NOT EXISTS(SELECT 1 FROM dbo.Reservations WHERE Id=@b AND Status='Arrived' AND ReminderSkipReason='Arrived')
 THROW 50004,N'FAIL: lượt khách đã tới phải bị bỏ qua với lý do Arrived.',1;
PRINT N'PASS: lượt khách đã tới không nhận email, lý do bỏ qua = Arrived';

-- C: kiểm tra lại ngay trước khi gửi -> vẫn hợp lệ, gửi và ghi thành công.
DECLARE @emailC bigint=(SELECT Id FROM @claimed);
DECLARE @check TABLE(SkipReason varchar(30));
INSERT @check EXEC dbo.usp_CheckBookingReminderBeforeSend @EmailId=@emailC,@AttemptNumber=1;
IF (SELECT SkipReason FROM @check) IS NOT NULL THROW 50005,N'FAIL: lượt vẫn đã xác nhận không được bị bỏ qua.',1;
EXEC dbo.usp_CompleteEmail @EmailId=@emailC,@AttemptNumber=1,@Succeeded=1;
IF NOT EXISTS(SELECT 1 FROM dbo.Reservations WHERE Id=@c AND ReminderEmailStatus='Sent' AND ReminderEmailSentAt IS NOT NULL AND ReminderSkipReason IS NULL)
 THROW 50006,N'FAIL: lượt vẫn đã xác nhận phải nhận email nhắc.',1;
PRINT N'PASS: lượt vẫn đã xác nhận nhận email nhắc';

-- D: web đã nhận email, lượt bị huỷ ngay trước lúc gửi -> kiểm tra lại trả về Cancelled, email không gửi.
DELETE @claimed; DELETE @check;
INSERT @claimed EXEC dbo.usp_ClaimDueBookingReminder;
IF ISNULL((SELECT ReservationId FROM @claimed),-1)<>@d THROW 50007,N'FAIL: email thứ hai được nhận phải là của lượt D.',1;
DECLARE @emailD bigint=(SELECT Id FROM @claimed);
UPDATE dbo.Reservations SET Status='Cancelled',CancelledAt=SYSUTCDATETIME() WHERE Id=@d;
INSERT @check EXEC dbo.usp_CheckBookingReminderBeforeSend @EmailId=@emailD,@AttemptNumber=1;
IF ISNULL((SELECT SkipReason FROM @check),'')<>'Cancelled'
 OR NOT EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE Id=@emailD AND Status='Cancelled' AND SkipReason='Cancelled' AND AttemptCount=0 AND SentAt IS NULL)
 OR EXISTS(SELECT 1 FROM dbo.EmailAttempts WHERE EmailId=@emailD)
 THROW 50008,N'FAIL: lượt đổi trạng thái ngay trước lúc gửi phải bị bỏ qua, không tính lần gửi.',1;
PRINT N'PASS: lượt đổi trạng thái ngay trước lúc gửi không nhận email';

-- Chạy lại: không còn email nào của lượt kiểm tra được nhận để gửi.
DELETE @claimed;
INSERT @claimed EXEC dbo.usp_ClaimDueBookingReminder;
IF EXISTS(SELECT 1 FROM @claimed WHERE ReservationId IN (@a,@b,@c,@d)) THROW 50009,N'FAIL: chạy lại không được gửi thêm email.',1;
PRINT N'PASS: chạy lại tác vụ không có email mới được gửi';

-- Danh sách theo dõi: lý do bỏ qua đúng từng lượt.
IF (SELECT COUNT(*) FROM dbo.EmailOutbox WHERE ReservationId IN (@a,@b,@d) AND MessageType='BookingReminder' AND Status='Cancelled' AND SkipReason IS NOT NULL)<>3
 OR EXISTS(SELECT 1 FROM dbo.EmailOutbox WHERE ReservationId=@c AND MessageType='BookingReminder' AND SkipReason IS NOT NULL)
 THROW 50010,N'FAIL: danh sách theo dõi phải có lý do bỏ qua cho A, B, D và không có cho C.',1;
PRINT N'PASS: danh sách theo dõi thể hiện đúng lý do bỏ qua';

PRINT N'Tất cả kiểm tra S4-08 Task 2 đã PASS. Rollback dữ liệu kiểm tra.';
ROLLBACK;
