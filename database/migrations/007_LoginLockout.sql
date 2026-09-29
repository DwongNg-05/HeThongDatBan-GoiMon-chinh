CREATE TABLE dbo.LoginLockoutSubjects (
 SubjectKey varchar(66) NOT NULL PRIMARY KEY,
 UserId int NULL REFERENCES dbo.Users(Id),
 LockedUntil datetime2(3) NULL
);
CREATE TABLE dbo.LoginFailures (
 Id bigint IDENTITY PRIMARY KEY,
 SubjectKey varchar(66) NOT NULL REFERENCES dbo.LoginLockoutSubjects(SubjectKey),
 FailedAt datetime2(3) NOT NULL,
 CountsTowardLimit bit NOT NULL
);
CREATE INDEX IX_LoginFailures_SubjectTime ON dbo.LoginFailures(SubjectKey,FailedAt) INCLUDE(CountsTowardLimit);
GO
-- Password verification stays in the application. Serialize the decision per account
-- and recheck the hash to reject an authentication racing with a password change.
CREATE OR ALTER PROCEDURE dbo.usp_CompleteLogin
 @UserId int=NULL, @Identifier nvarchar(50), @PasswordValid bit,
 @ExpectedHash varchar(100)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 BEGIN TRAN;
 DECLARE @active bit=0,@hash varchar(100),@key varchar(66),@until datetime2(3),@now datetime2(3),@count int;
 IF @UserId IS NOT NULL
  SELECT @active=IsActive,@hash=PasswordHash FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@UserId;
 SET @key=CASE WHEN @UserId IS NOT NULL THEN CONCAT('U:',@UserId)
  ELSE CONCAT('I:',CONVERT(varchar(64),HASHBYTES('SHA2_256',UPPER(LTRIM(RTRIM(@Identifier)))),2)) END;
 SELECT @until=LockedUntil FROM dbo.LoginLockoutSubjects WITH(UPDLOCK,HOLDLOCK) WHERE SubjectKey=@key;
 IF NOT EXISTS(SELECT 1 FROM dbo.LoginLockoutSubjects WHERE SubjectKey=@key)
  INSERT dbo.LoginLockoutSubjects(SubjectKey,UserId) VALUES(@key,@UserId);
 SET @now=SYSUTCDATETIME();
 DELETE dbo.LoginFailures WHERE SubjectKey=@key AND FailedAt<=DATEADD(minute,-15,@now);
 IF @until>@now
 BEGIN
  INSERT dbo.LoginFailures(SubjectKey,FailedAt,CountsTowardLimit) VALUES(@key,@now,0);
  COMMIT;
  SELECT CAST(0 AS bit) AS IsAuthenticated,CONVERT(int,CEILING(DATEDIFF_BIG(millisecond,@now,@until)/1000.0)) AS RemainingSeconds;
  RETURN;
 END;
 IF @until IS NOT NULL
 BEGIN
  DELETE dbo.LoginFailures WHERE SubjectKey=@key;
  UPDATE dbo.LoginLockoutSubjects SET LockedUntil=NULL WHERE SubjectKey=@key;
 END;
 IF @PasswordValid=1 AND @active=1 AND @hash IS NOT NULL AND @hash COLLATE Latin1_General_100_BIN2=@ExpectedHash COLLATE Latin1_General_100_BIN2
 BEGIN
  DELETE dbo.LoginFailures WHERE SubjectKey=@key;
  UPDATE dbo.Users SET FailedLoginCount=0,FailureWindowStartedAt=NULL,LockedUntil=NULL,LastLoginAt=@now WHERE Id=@UserId;
  COMMIT;
  SELECT CAST(1 AS bit) AS IsAuthenticated,0 AS RemainingSeconds;
  RETURN;
 END;
 INSERT dbo.LoginFailures(SubjectKey,FailedAt,CountsTowardLimit) VALUES(@key,@now,1);
 SELECT @count=COUNT(*) FROM dbo.LoginFailures WHERE SubjectKey=@key AND CountsTowardLimit=1;
 SET @until=CASE WHEN @count>=5 THEN DATEADD(minute,15,@now) ELSE NULL END;
 UPDATE dbo.LoginLockoutSubjects SET LockedUntil=@until WHERE SubjectKey=@key;
 UPDATE dbo.Users SET FailedLoginCount=@count,LockedUntil=@until,
  FailureWindowStartedAt=(SELECT MIN(FailedAt) FROM dbo.LoginFailures WHERE SubjectKey=@key AND CountsTowardLimit=1)
 WHERE Id=@UserId;
 COMMIT;
 SELECT CAST(0 AS bit) AS IsAuthenticated,CASE WHEN @until IS NULL THEN 0 ELSE 900 END AS RemainingSeconds;
END;
GO
GRANT EXECUTE ON dbo.usp_CompleteLogin TO restaurant_app;
GO
