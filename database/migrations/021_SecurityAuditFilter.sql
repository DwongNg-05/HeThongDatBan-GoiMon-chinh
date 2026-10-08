-- S1-05 Task 2: lọc nhật ký bảo mật theo khoảng thời gian (UTC, do ứng dụng quy đổi từ ngày Việt Nam) và theo tài khoản.
-- Không sửa 020; chỉ thay thủ tục đọc và thêm chỉ mục cho lọc theo tài khoản.
CREATE INDEX IX_SecurityAuditLogs_User ON dbo.SecurityAuditLogs(UserId,OccurredAt DESC,Id DESC)
 INCLUDE(UserName,RoleCode,Action,IpAddress);
GO
-- @FromUtc bao gồm, @ToUtcExclusive không bao gồm. @UserId NULL = mọi tài khoản; @UnknownAccounts=1 = chỉ định danh không tồn tại.
-- Trả về 2 tập kết quả: tối đa @Top dòng mới nhất, và tổng số dòng khớp điều kiện.
CREATE OR ALTER PROCEDURE dbo.usp_SecurityAuditList
 @ActorUserId int,
 @Top int=200,
 @FromUtc datetime2(3)=NULL,
 @ToUtcExclusive datetime2(3)=NULL,
 @UserId int=NULL,
 @UnknownAccounts bit=0
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Audit.Read';
 IF @FromUtc IS NOT NULL AND @ToUtcExclusive IS NOT NULL AND @FromUtc>=@ToUtcExclusive
  THROW 51111,N'Khoảng thời gian lọc nhật ký không hợp lệ.',1;
 SELECT TOP(CASE WHEN @Top BETWEEN 1 AND 1000 THEN @Top ELSE 200 END)
  l.Id,l.OccurredAt,l.UserName,l.RoleCode,r.Name AS RoleName,l.Action,l.Detail,l.IpAddress
 FROM dbo.SecurityAuditLogs l
 LEFT JOIN dbo.Roles r ON r.Code=l.RoleCode
 WHERE (@FromUtc IS NULL OR l.OccurredAt>=@FromUtc)
  AND (@ToUtcExclusive IS NULL OR l.OccurredAt<@ToUtcExclusive)
  AND (@UserId IS NULL OR l.UserId=@UserId)
  AND (@UnknownAccounts=0 OR l.UserId IS NULL)
 ORDER BY l.OccurredAt DESC,l.Id DESC
 OPTION(RECOMPILE);
 SELECT COUNT_BIG(*) AS TotalCount
 FROM dbo.SecurityAuditLogs l
 WHERE (@FromUtc IS NULL OR l.OccurredAt>=@FromUtc)
  AND (@ToUtcExclusive IS NULL OR l.OccurredAt<@ToUtcExclusive)
  AND (@UserId IS NULL OR l.UserId=@UserId)
  AND (@UnknownAccounts=0 OR l.UserId IS NULL)
 OPTION(RECOMPILE);
END;
GO
-- Danh sách tài khoản cho bộ lọc: chọn từ danh sách có sẵn (kể cả tài khoản đã ngừng hoạt động để xem lịch sử).
CREATE OR ALTER PROCEDURE dbo.usp_SecurityAuditAccounts @ActorUserId int
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Audit.Read';
 SELECT u.Id,u.UserName,r.Name AS RoleName,u.IsActive
 FROM dbo.Users u JOIN dbo.Roles r ON r.Id=u.RoleId
 ORDER BY u.IsActive DESC,u.UserName;
END;
GO
GRANT EXECUTE ON dbo.usp_SecurityAuditList TO restaurant_app;
GRANT EXECUTE ON dbo.usp_SecurityAuditAccounts TO restaurant_app;
GO
