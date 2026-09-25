-- SQL Server 2022+, UTC timestamps; VND amounts are integral decimal(18,0).
CREATE TABLE dbo.RestaurantSettings (
 Id int NOT NULL CONSTRAINT PK_RestaurantSettings PRIMARY KEY CONSTRAINT CK_SettingsSingleton CHECK(Id=1),
 Name nvarchar(150) NOT NULL, Address nvarchar(300) NOT NULL, Phone varchar(10) NOT NULL,
 Email nvarchar(254) NULL, TimeZoneId varchar(50) NOT NULL DEFAULT 'Asia/Ho_Chi_Minh',
 CurrencyCode char(3) NOT NULL DEFAULT 'VND' CHECK(CurrencyCode='VND'),
 BookingSlotMinutes int NOT NULL DEFAULT 30 CHECK(BookingSlotMinutes=30),
 DefaultBookingMinutes int NOT NULL DEFAULT 90 CHECK(DefaultBookingMinutes BETWEEN 30 AND 360),
 NoShowGraceMinutes int NOT NULL DEFAULT 15 CHECK(NoShowGraceMinutes>0),
 CancelCutoffMinutes int NOT NULL DEFAULT 60 CHECK(CancelCutoffMinutes>=0),
 ReminderLeadMinutes int NOT NULL DEFAULT 120, RowVersion rowversion NOT NULL
);
CREATE TABLE dbo.OpeningHours (
 DayOfWeek tinyint NOT NULL PRIMARY KEY CHECK(DayOfWeek BETWEEN 1 AND 7),
 IsClosed bit NOT NULL DEFAULT 0, OpensAt time(0) NULL, ClosesAt time(0) NULL,
 CONSTRAINT CK_OpeningHours CHECK(IsClosed=1 OR (OpensAt IS NOT NULL AND ClosesAt IS NOT NULL AND ClosesAt>OpensAt))
);
CREATE TABLE dbo.SpecialDates (
 BusinessDate date NOT NULL PRIMARY KEY, IsClosed bit NOT NULL DEFAULT 1,
 OpensAt time(0) NULL, ClosesAt time(0) NULL, Reason nvarchar(300) NOT NULL,
 CHECK(IsClosed=1 OR (OpensAt IS NOT NULL AND ClosesAt IS NOT NULL AND ClosesAt>OpensAt))
);
CREATE TABLE dbo.Roles (Id int NOT NULL PRIMARY KEY, Code varchar(20) NOT NULL UNIQUE, Name nvarchar(80) NOT NULL);
CREATE TABLE dbo.Permissions (Id int IDENTITY PRIMARY KEY, Code varchar(100) NOT NULL UNIQUE, Description nvarchar(250) NOT NULL);
CREATE TABLE dbo.RolePermissions (
 RoleId int NOT NULL REFERENCES dbo.Roles(Id), PermissionId int NOT NULL REFERENCES dbo.Permissions(Id),
 CONSTRAINT PK_RolePermissions PRIMARY KEY(RoleId,PermissionId)
);
CREATE TABLE dbo.Users (
 Id int IDENTITY PRIMARY KEY, RoleId int NOT NULL REFERENCES dbo.Roles(Id),
 FullName nvarchar(100) NOT NULL, UserName nvarchar(50) NOT NULL,
 NormalizedUserName AS UPPER(LTRIM(RTRIM(UserName))) PERSISTED,
 Phone varchar(10) NOT NULL UNIQUE CHECK(LEN(Phone)=10 AND Phone NOT LIKE '%[^0-9]%'),
 PasswordHash varchar(100) NULL, MustChangePassword bit NOT NULL DEFAULT 1,
 IsActive bit NOT NULL DEFAULT 1, FailedLoginCount int NOT NULL DEFAULT 0 CHECK(FailedLoginCount>=0),
 FailureWindowStartedAt datetime2(3) NULL, LockedUntil datetime2(3) NULL,
 SecurityStamp uniqueidentifier NOT NULL DEFAULT NEWID(), PasswordChangedAt datetime2(3) NULL,
 LastLoginAt datetime2(3) NULL, CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), RowVersion rowversion NOT NULL,
 CONSTRAINT CK_UserName CHECK(LEN(LTRIM(RTRIM(UserName)))>0)
);
CREATE UNIQUE INDEX UX_Users_NormalizedUserName ON dbo.Users(NormalizedUserName);
CREATE TABLE dbo.RefreshTokens (
 Id bigint IDENTITY PRIMARY KEY, UserId int NOT NULL REFERENCES dbo.Users(Id),
 TokenHash binary(32) NOT NULL UNIQUE, CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 ExpiresAt datetime2(3) NOT NULL, LastActivityAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 RevokedAt datetime2(3) NULL, ReplacedByTokenHash binary(32) NULL, IpAddress varchar(45) NULL,
 CHECK(ExpiresAt>CreatedAt)
);
CREATE INDEX IX_RefreshTokens_User ON dbo.RefreshTokens(UserId,RevokedAt);
CREATE TABLE dbo.LoginAttempts (
 Id bigint IDENTITY PRIMARY KEY, UserId int NULL REFERENCES dbo.Users(Id),
 UserNameAttempt nvarchar(50) NOT NULL, IpAddress varchar(45) NOT NULL,
 Succeeded bit NOT NULL, AttemptedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE TABLE dbo.LookupAttempts (
 Id bigint IDENTITY PRIMARY KEY, IpAddress varchar(45) NOT NULL,
 Succeeded bit NOT NULL, AttemptedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_LookupAttempts_RateLimit ON dbo.LookupAttempts(IpAddress,AttemptedAt) INCLUDE(Succeeded);
CREATE TABLE dbo.AuditLogs (
 Id bigint IDENTITY PRIMARY KEY, ActorUserId int NULL REFERENCES dbo.Users(Id),
 ActorRole varchar(20) NULL, Action varchar(80) NOT NULL, EntityType varchar(60) NOT NULL,
 EntityId varchar(60) NULL, OldValues nvarchar(max) NULL, NewValues nvarchar(max) NULL,
 Reason nvarchar(500) NULL, IpAddress varchar(45) NULL,
 OccurredAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 CHECK(OldValues IS NULL OR ISJSON(OldValues)=1), CHECK(NewValues IS NULL OR ISJSON(NewValues)=1)
);
CREATE INDEX IX_AuditLogs_DateActor ON dbo.AuditLogs(OccurredAt,ActorUserId);
CREATE TABLE dbo.Areas (
 Id int IDENTITY PRIMARY KEY, Name nvarchar(80) NOT NULL,
 NormalizedName AS UPPER(LTRIM(RTRIM(Name))) PERSISTED,
 SortOrder int NOT NULL DEFAULT 0, Notes nvarchar(500) NULL, IsActive bit NOT NULL DEFAULT 1,
 RowVersion rowversion NOT NULL, CHECK(LEN(LTRIM(RTRIM(Name)))>0)
);
CREATE UNIQUE INDEX UX_Areas_Name ON dbo.Areas(NormalizedName);
CREATE TABLE dbo.DiningTables (
 Id int IDENTITY PRIMARY KEY, AreaId int NOT NULL REFERENCES dbo.Areas(Id),
 Code varchar(20) NOT NULL UNIQUE, MinCapacity int NOT NULL DEFAULT 1,
 MaxCapacity int NOT NULL, TableType varchar(20) NOT NULL DEFAULT 'Standard' CHECK(TableType IN ('Standard','PrivateRoom')),
 Status varchar(20) NOT NULL DEFAULT 'Available' CHECK(Status IN ('Available','Reserved','Serving','Cleaning')),
 IsActive bit NOT NULL DEFAULT 1, SortOrder int NOT NULL DEFAULT 0,
 PositionX int NULL, PositionY int NULL, StatusChangedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 RowVersion rowversion NOT NULL, CHECK(MinCapacity>=1 AND MaxCapacity>=MinCapacity AND MaxCapacity<=60)
);
CREATE INDEX IX_DiningTables_Area ON dbo.DiningTables(AreaId,IsActive,Status);
CREATE TABLE dbo.TableQrCodes (
 Id bigint IDENTITY PRIMARY KEY, TableId int NOT NULL REFERENCES dbo.DiningTables(Id),
 TokenHash binary(32) NOT NULL UNIQUE, CreatedBy int NULL REFERENCES dbo.Users(Id),
 CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), RevokedAt datetime2(3) NULL
);
CREATE UNIQUE INDEX UX_TableQrCodes_Active ON dbo.TableQrCodes(TableId) WHERE RevokedAt IS NULL;
CREATE TABLE dbo.MenuCategories (
 Id int IDENTITY PRIMARY KEY, Name nvarchar(50) NOT NULL,
 NormalizedName AS UPPER(LTRIM(RTRIM(Name))) PERSISTED,
 SortOrder int NOT NULL DEFAULT 0, IsActive bit NOT NULL DEFAULT 1,
 DefaultImagePath nvarchar(500) NULL, RowVersion rowversion NOT NULL,
 CHECK(LEN(LTRIM(RTRIM(Name)))>0)
);
CREATE UNIQUE INDEX UX_MenuCategories_Name ON dbo.MenuCategories(NormalizedName);
CREATE TABLE dbo.MenuItems (
 Id int IDENTITY PRIMARY KEY, CategoryId int NOT NULL REFERENCES dbo.MenuCategories(Id),
 Name nvarchar(150) COLLATE Vietnamese_100_CI_AI NOT NULL,
 Price decimal(18,0) NOT NULL CHECK(Price>0 AND Price<=50000000), Unit nvarchar(30) NOT NULL,
 Description nvarchar(1000) NULL, EstimatedPrepMinutes int NOT NULL CHECK(EstimatedPrepMinutes BETWEEN 1 AND 240),
 IsActive bit NOT NULL DEFAULT 1, IsSoldOut bit NOT NULL DEFAULT 0,
 SoldOutBusinessDate date NULL, ImagePath nvarchar(500) NULL, ImageContentType varchar(30) NULL,
 ImageSizeBytes int NULL CHECK(ImageSizeBytes BETWEEN 1 AND 5242880),
 ImageWidth int NULL CHECK(ImageWidth BETWEEN 1 AND 1024), ImageHeight int NULL CHECK(ImageHeight BETWEEN 1 AND 1024),
 SortOrder int NOT NULL DEFAULT 0, CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 UpdatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), RowVersion rowversion NOT NULL,
 CHECK(ImageContentType IS NULL OR ImageContentType IN ('image/jpeg','image/png'))
);
CREATE INDEX IX_MenuItems_Category ON dbo.MenuItems(CategoryId,IsActive) INCLUDE(Name,Price,IsSoldOut);
CREATE TABLE dbo.MenuPriceHistory (
 Id bigint IDENTITY PRIMARY KEY, MenuItemId int NOT NULL REFERENCES dbo.MenuItems(Id),
 OldPrice decimal(18,0) NOT NULL, NewPrice decimal(18,0) NOT NULL,
 ChangedBy int NOT NULL REFERENCES dbo.Users(Id), ChangedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE TABLE dbo.MenuAvailabilityEvents (
 Id bigint IDENTITY PRIMARY KEY, MenuItemId int NOT NULL REFERENCES dbo.MenuItems(Id),
 IsSoldOut bit NOT NULL, BusinessDate date NOT NULL, ChangedBy int NULL REFERENCES dbo.Users(Id),
 ChangedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE TABLE dbo.Reservations (
 Id bigint IDENTITY PRIMARY KEY, Code char(6) NOT NULL UNIQUE,
 CustomerName nvarchar(100) NOT NULL, Phone varchar(10) NOT NULL CHECK(LEN(Phone)=10 AND Phone NOT LIKE '%[^0-9]%'),
 Email nvarchar(254) NULL, GuestCount int NOT NULL CHECK(GuestCount BETWEEN 1 AND 20),
 PreferredAreaId int NULL REFERENCES dbo.Areas(Id), TableId int NULL REFERENCES dbo.DiningTables(Id),
 StartsAt datetime2(3) NOT NULL, EndsAt datetime2(3) NOT NULL,
 Status varchar(20) NOT NULL DEFAULT 'Pending' CHECK(Status IN ('Pending','Confirmed','Rejected','Cancelled','Arrived','NoShow')),
 Notes nvarchar(500) NULL, RejectionReason varchar(30) NULL CHECK(RejectionReason IN ('NoTable','OutsideHours','Unreachable')),
 CancelReason nvarchar(500) NULL, CancelledAt datetime2(3) NULL, ConfirmedAt datetime2(3) NULL,
 ConfirmedBy int NULL REFERENCES dbo.Users(Id), ArrivedAt datetime2(3) NULL,
 NoShowAt datetime2(3) NULL, HoldExtendedUntil datetime2(3) NULL,
 ExtensionCount tinyint NOT NULL DEFAULT 0 CHECK(ExtensionCount BETWEEN 0 AND 1),
 CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), RowVersion rowversion NOT NULL,
 CHECK(EndsAt>StartsAt), CHECK(Status NOT IN ('Confirmed','Arrived') OR TableId IS NOT NULL)
);
CREATE INDEX IX_Reservations_TableTime ON dbo.Reservations(TableId,StartsAt,EndsAt) INCLUDE(Status);
CREATE INDEX IX_Reservations_PhoneStatus ON dbo.Reservations(Phone,Status,StartsAt);
CREATE TABLE dbo.ReservationEvents (
 Id bigint IDENTITY PRIMARY KEY, ReservationId bigint NOT NULL REFERENCES dbo.Reservations(Id),
 FromStatus varchar(20) NULL, ToStatus varchar(20) NOT NULL,
 OldTableId int NULL REFERENCES dbo.DiningTables(Id), NewTableId int NULL REFERENCES dbo.DiningTables(Id),
 ActorUserId int NULL REFERENCES dbo.Users(Id), Reason nvarchar(500) NULL,
 OccurredAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE TABLE dbo.Shifts (
 Id bigint IDENTITY PRIMARY KEY, Name nvarchar(100) NOT NULL, BusinessDate date NOT NULL,
 OpenedBy int NOT NULL REFERENCES dbo.Users(Id), OpenedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 OpeningCash decimal(18,0) NOT NULL DEFAULT 0 CHECK(OpeningCash>=0),
 Status varchar(10) NOT NULL DEFAULT 'Open' CHECK(Status IN ('Open','Closed')),
 ClosedBy int NULL REFERENCES dbo.Users(Id), ClosedAt datetime2(3) NULL,
 CountedCash decimal(18,0) NULL CHECK(CountedCash>=0), ExpectedCash decimal(18,0) NULL,
 CashDifference AS (CountedCash-ExpectedCash) PERSISTED,
 Explanation nvarchar(500) NULL, InvoiceCount int NULL, Revenue decimal(18,0) NULL,
 CashRevenue decimal(18,0) NULL, TransferRevenue decimal(18,0) NULL,
 DiscountTotal decimal(18,0) NULL, ServedTableCount int NULL, RowVersion rowversion NOT NULL
);
CREATE UNIQUE INDEX UX_Shifts_OneOpen ON dbo.Shifts(Status) WHERE Status='Open';
CREATE TABLE dbo.ShiftEvents (
 Id bigint IDENTITY PRIMARY KEY, ShiftId bigint NOT NULL REFERENCES dbo.Shifts(Id),
 Action varchar(20) NOT NULL CHECK(Action IN ('Opened','Closed','Reopened')),
 ActorUserId int NOT NULL REFERENCES dbo.Users(Id), Reason nvarchar(500) NULL,
 SnapshotJson nvarchar(max) NULL CHECK(SnapshotJson IS NULL OR ISJSON(SnapshotJson)=1),
 OccurredAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE TABLE dbo.DiningSessions (
 Id bigint IDENTITY PRIMARY KEY, ReservationId bigint NULL REFERENCES dbo.Reservations(Id),
 ShiftId bigint NOT NULL REFERENCES dbo.Shifts(Id), GuestCount int NOT NULL CHECK(GuestCount BETWEEN 1 AND 60),
 Status varchar(20) NOT NULL DEFAULT 'Open' CHECK(Status IN ('Open','AwaitingPayment','Closed')),
 BillingSessionId bigint NULL REFERENCES dbo.DiningSessions(Id),
 OpenedBy int NOT NULL REFERENCES dbo.Users(Id), OpenedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 PaymentRequestedAt datetime2(3) NULL, ClosedAt datetime2(3) NULL,
 ClosedBy int NULL REFERENCES dbo.Users(Id), RowVersion rowversion NOT NULL,
 CHECK(BillingSessionId IS NULL OR BillingSessionId<>Id)
);
CREATE UNIQUE INDEX UX_DiningSessions_Reservation ON dbo.DiningSessions(ReservationId) WHERE ReservationId IS NOT NULL;
CREATE INDEX IX_DiningSessions_Billing ON dbo.DiningSessions(BillingSessionId,Status);
CREATE TABLE dbo.SessionTables (
 Id bigint IDENTITY PRIMARY KEY, SessionId bigint NOT NULL REFERENCES dbo.DiningSessions(Id),
 TableId int NOT NULL REFERENCES dbo.DiningTables(Id),
 AssignedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), ReleasedAt datetime2(3) NULL,
 CHECK(ReleasedAt IS NULL OR ReleasedAt>=AssignedAt)
);
CREATE UNIQUE INDEX UX_SessionTables_ActiveTable ON dbo.SessionTables(TableId) WHERE ReleasedAt IS NULL;
CREATE UNIQUE INDEX UX_SessionTables_SessionTable ON dbo.SessionTables(SessionId,TableId);
CREATE TABLE dbo.SessionMerges (
 Id bigint IDENTITY PRIMARY KEY, MainSessionId bigint NOT NULL REFERENCES dbo.DiningSessions(Id),
 ChildSessionId bigint NOT NULL REFERENCES dbo.DiningSessions(Id),
 MergedBy int NOT NULL REFERENCES dbo.Users(Id), MergedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 UndoneBy int NULL REFERENCES dbo.Users(Id), UndoneAt datetime2(3) NULL,
 CHECK(MainSessionId<>ChildSessionId)
);
CREATE UNIQUE INDEX UX_SessionMerges_ActiveChild ON dbo.SessionMerges(ChildSessionId) WHERE UndoneAt IS NULL;
CREATE TABLE dbo.GuestSessions (
 Id bigint IDENTITY PRIMARY KEY, SessionId bigint NOT NULL REFERENCES dbo.DiningSessions(Id),
 TableQrCodeId bigint NOT NULL REFERENCES dbo.TableQrCodes(Id), TokenHash binary(32) NOT NULL UNIQUE,
 CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), ExpiresAt datetime2(3) NOT NULL,
 RevokedAt datetime2(3) NULL, CHECK(ExpiresAt>CreatedAt)
);
CREATE TABLE dbo.OrderBatches (
 Id bigint IDENTITY PRIMARY KEY, SessionId bigint NOT NULL REFERENCES dbo.DiningSessions(Id),
 BatchNumber int NOT NULL CHECK(BatchNumber>0), RequestId uniqueidentifier NOT NULL UNIQUE,
 CreatedBy int NULL REFERENCES dbo.Users(Id), GuestSessionId bigint NULL REFERENCES dbo.GuestSessions(Id),
 CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT UQ_OrderBatches_Number UNIQUE(SessionId,BatchNumber),
 CHECK((CreatedBy IS NOT NULL AND GuestSessionId IS NULL) OR (CreatedBy IS NULL AND GuestSessionId IS NOT NULL))
);
CREATE TABLE dbo.OrderItems (
 Id bigint IDENTITY PRIMARY KEY, BatchId bigint NOT NULL REFERENCES dbo.OrderBatches(Id),
 MenuItemId int NOT NULL REFERENCES dbo.MenuItems(Id), OriginalTableId int NOT NULL REFERENCES dbo.DiningTables(Id),
 ItemName nvarchar(150) NOT NULL, Unit nvarchar(30) NOT NULL, UnitPrice decimal(18,0) NOT NULL CHECK(UnitPrice>0),
 Quantity int NOT NULL CHECK(Quantity BETWEEN 1 AND 99), Notes nvarchar(200) NULL,
 EstimatedPrepMinutes int NOT NULL,
 Status varchar(20) NOT NULL DEFAULT 'Pending' CHECK(Status IN ('Pending','Preparing','Ready','Served','Cancelled')),
 SubmittedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), PreparingAt datetime2(3) NULL,
 ReadyAt datetime2(3) NULL, ServedAt datetime2(3) NULL, ServedBy int NULL REFERENCES dbo.Users(Id),
 CancelledAt datetime2(3) NULL, CancelledBy int NULL REFERENCES dbo.Users(Id),
 CancelReason varchar(30) NULL CHECK(CancelReason IN ('ChangedMind','Mistake','SoldOut','ManagerOverride')),
 CancellationNote nvarchar(500) NULL, ChargeWhenCancelled bit NOT NULL DEFAULT 0,
 LineTotal AS (CONVERT(decimal(18,0),Quantity*UnitPrice)) PERSISTED,
 RowVersion rowversion NOT NULL,
 CHECK(Status<>'Cancelled' OR (CancelledAt IS NOT NULL AND CancelledBy IS NOT NULL AND CancelReason IS NOT NULL))
);
CREATE INDEX IX_OrderItems_Kitchen ON dbo.OrderItems(Status,SubmittedAt) INCLUDE(BatchId,ItemName,Quantity);
CREATE TABLE dbo.OrderItemEvents (
 Id bigint IDENTITY PRIMARY KEY, OrderItemId bigint NOT NULL REFERENCES dbo.OrderItems(Id),
 FromStatus varchar(20) NULL, ToStatus varchar(20) NOT NULL,
 ActorUserId int NULL REFERENCES dbo.Users(Id), Reason nvarchar(500) NULL,
 OccurredAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE TABLE dbo.DailyInvoiceCounters (BusinessDate date NOT NULL PRIMARY KEY, LastNumber int NOT NULL CHECK(LastNumber>0));
CREATE TABLE dbo.Invoices (
 Id bigint IDENTITY PRIMARY KEY, SessionId bigint NOT NULL REFERENCES dbo.DiningSessions(Id),
 ShiftId bigint NOT NULL REFERENCES dbo.Shifts(Id), InvoiceNumber varchar(30) NOT NULL UNIQUE,
 BusinessDate date NOT NULL, RequestId uniqueidentifier NOT NULL UNIQUE,
 RestaurantName nvarchar(150) NOT NULL, RestaurantAddress nvarchar(300) NOT NULL, RestaurantPhone varchar(10) NOT NULL,
 TableLabels nvarchar(500) NOT NULL, GuestCount int NOT NULL,
 SessionOpenedAt datetime2(3) NOT NULL, IssuedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 IssuedBy int NOT NULL REFERENCES dbo.Users(Id), Subtotal decimal(18,0) NOT NULL CHECK(Subtotal>=0),
 DiscountType varchar(10) NOT NULL DEFAULT 'None' CHECK(DiscountType IN ('None','Amount','Percent')),
 DiscountValue decimal(18,2) NOT NULL DEFAULT 0 CHECK(DiscountValue>=0),
 DiscountAmount decimal(18,0) NOT NULL DEFAULT 0, DiscountReason nvarchar(500) NULL,
 Total AS (Subtotal-DiscountAmount) PERSISTED,
 Status varchar(10) NOT NULL DEFAULT 'Paid' CHECK(Status IN ('Paid','Voided')),
 VoidedBy int NULL REFERENCES dbo.Users(Id), VoidedAt datetime2(3) NULL, VoidReason nvarchar(500) NULL,
 RowVersion rowversion NOT NULL,
 CHECK(DiscountAmount>=0 AND DiscountAmount<=Subtotal*0.5),
 CHECK(DiscountAmount=0 OR LEN(LTRIM(RTRIM(DiscountReason)))>0),
 CHECK(Status<>'Voided' OR (VoidedBy IS NOT NULL AND VoidedAt IS NOT NULL AND VoidReason IS NOT NULL))
);
CREATE UNIQUE INDEX UX_Invoices_PaidSession ON dbo.Invoices(SessionId) WHERE Status='Paid';
CREATE INDEX IX_Invoices_Reports ON dbo.Invoices(BusinessDate,Status) INCLUDE(Total,Subtotal,DiscountAmount,GuestCount,ShiftId);
CREATE TABLE dbo.InvoiceLines (
 Id bigint IDENTITY PRIMARY KEY, InvoiceId bigint NOT NULL REFERENCES dbo.Invoices(Id),
 OrderItemId bigint NOT NULL REFERENCES dbo.OrderItems(Id), MenuItemId int NOT NULL REFERENCES dbo.MenuItems(Id),
 ItemName nvarchar(150) NOT NULL, Unit nvarchar(30) NOT NULL, Quantity int NOT NULL CHECK(Quantity>0),
 UnitPrice decimal(18,0) NOT NULL CHECK(UnitPrice>0), Notes nvarchar(200) NULL,
 OriginalTableCode varchar(20) NOT NULL, IsChargedCancellation bit NOT NULL,
 LineTotal AS (CONVERT(decimal(18,0),Quantity*UnitPrice)) PERSISTED,
 CONSTRAINT UQ_InvoiceLines_Item UNIQUE(InvoiceId,OrderItemId)
);
CREATE TABLE dbo.Payments (
 Id bigint IDENTITY PRIMARY KEY, InvoiceId bigint NOT NULL UNIQUE REFERENCES dbo.Invoices(Id),
 Method varchar(10) NOT NULL CHECK(Method IN ('Cash','Transfer')), Amount decimal(18,0) NOT NULL CHECK(Amount>=0),
 CashReceived decimal(18,0) NULL, ChangeAmount AS (CASE WHEN Method='Cash' THEN CashReceived-Amount ELSE NULL END) PERSISTED,
 TransferReference nvarchar(100) NULL, ReceivedBy int NOT NULL REFERENCES dbo.Users(Id),
 ReceivedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
 CHECK((Method='Cash' AND CashReceived IS NOT NULL AND CashReceived>=Amount AND TransferReference IS NULL)
 OR (Method='Transfer' AND CashReceived IS NULL AND LEN(LTRIM(RTRIM(TransferReference)))>0))
);
CREATE TABLE dbo.EmailOutbox (
 Id bigint IDENTITY PRIMARY KEY, ReservationId bigint NULL REFERENCES dbo.Reservations(Id),
 MessageType varchar(30) NOT NULL CHECK(MessageType IN ('BookingReceived','BookingConfirmed','BookingCancelled','BookingReminder','BookingRejected')),
 Recipient nvarchar(254) NOT NULL, Subject nvarchar(250) NOT NULL, PayloadJson nvarchar(max) NOT NULL CHECK(ISJSON(PayloadJson)=1),
 DedupeKey varchar(100) NOT NULL UNIQUE,
 Status varchar(15) NOT NULL DEFAULT 'Pending' CHECK(Status IN ('Pending','Processing','Sent','Failed','Cancelled')),
 AttemptCount int NOT NULL DEFAULT 0 CHECK(AttemptCount BETWEEN 0 AND 4),
 NextAttemptAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), LockedUntil datetime2(3) NULL,
 SentAt datetime2(3) NULL, LastError nvarchar(2000) NULL, CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_EmailOutbox_Worker ON dbo.EmailOutbox(Status,NextAttemptAt);
CREATE TABLE dbo.Notifications (
 Id bigint IDENTITY PRIMARY KEY, Kind varchar(40) NOT NULL, Title nvarchar(150) NOT NULL,
 Body nvarchar(1000) NOT NULL, TargetRoleId int NULL REFERENCES dbo.Roles(Id),
 TargetUserId int NULL REFERENCES dbo.Users(Id), EntityType varchar(40) NULL, EntityId bigint NULL,
 CreatedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), ExpiresAt datetime2(3) NULL,
 PublishedAt datetime2(3) NULL, CHECK(TargetRoleId IS NOT NULL OR TargetUserId IS NOT NULL)
);
CREATE TABLE dbo.NotificationReceipts (
 NotificationId bigint NOT NULL REFERENCES dbo.Notifications(Id), UserId int NOT NULL REFERENCES dbo.Users(Id),
 ReadAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), PRIMARY KEY(NotificationId,UserId)
);
CREATE TABLE dbo.ScheduledJobRuns (
 Id bigint IDENTITY PRIMARY KEY, JobName varchar(80) NOT NULL, ScheduledFor datetime2(3) NOT NULL,
 StartedAt datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), CompletedAt datetime2(3) NULL,
 Status varchar(15) NOT NULL CHECK(Status IN ('Running','Succeeded','Failed')),
 Error nvarchar(2000) NULL, CONSTRAINT UQ_JobRuns UNIQUE(JobName,ScheduledFor)
);
GO
