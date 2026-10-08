-- Phân trang màn hình Nhật ký hệ thống (S1-05). Không sửa 021; chỉ thay thủ tục đọc bằng phiên bản có @Skip.
-- @Skip = số dòng bỏ qua (trang n => (n-1)*@Top), @Top = số dòng mỗi trang. Gọi không truyền @Skip vẫn chạy như cũ.
-- Thứ tự OccurredAt DESC, Id DESC là duy nhất nên các trang không trùng hoặc sót dòng.
CREATE OR ALTER PROCEDURE dbo.usp_SecurityAuditList
 @ActorUserId int,
 @Top int=200,
 @FromUtc datetime2(3)=NULL,
 @ToUtcExclusive datetime2(3)=NULL,
 @UserId int=NULL,
 @UnknownAccounts bit=0,
 @Skip int=0
AS
BEGIN
 SET NOCOUNT ON;
 EXEC dbo.usp_RequirePermission @ActorUserId,'Audit.Read';
 IF @FromUtc IS NOT NULL AND @ToUtcExclusive IS NOT NULL AND @FromUtc>=@ToUtcExclusive
  THROW 51111,N'Khoảng thời gian lọc nhật ký không hợp lệ.',1;
 DECLARE @Take int=CASE WHEN @Top BETWEEN 1 AND 1000 THEN @Top ELSE 200 END;
 DECLARE @Offset int=CASE WHEN @Skip>0 THEN @Skip ELSE 0 END;
 SELECT l.Id,l.OccurredAt,l.UserName,l.RoleCode,r.Name AS RoleName,l.Action,l.Detail,l.IpAddress
 FROM dbo.SecurityAuditLogs l
 LEFT JOIN dbo.Roles r ON r.Code=l.RoleCode
 WHERE (@FromUtc IS NULL OR l.OccurredAt>=@FromUtc)
  AND (@ToUtcExclusive IS NULL OR l.OccurredAt<@ToUtcExclusive)
  AND (@UserId IS NULL OR l.UserId=@UserId)
  AND (@UnknownAccounts=0 OR l.UserId IS NULL)
 ORDER BY l.OccurredAt DESC,l.Id DESC
 OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY
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
GRANT EXECUTE ON dbo.usp_SecurityAuditList TO restaurant_app;
GO
