-- Persistent label belongs to the billing root, so every member shares one identity.
ALTER TABLE dbo.DiningSessions ADD TableGroupLabel varchar(30) NULL;
GO
UPDATE root SET TableGroupLabel=CONCAT('N-',root.Id)
FROM dbo.DiningSessions root
WHERE EXISTS(SELECT 1 FROM dbo.SessionMerges m WHERE m.MainSessionId=root.Id);
CREATE UNIQUE INDEX UX_DiningSessions_TableGroupLabel ON dbo.DiningSessions(TableGroupLabel)
 WHERE TableGroupLabel IS NOT NULL;
GO
CREATE OR ALTER TRIGGER dbo.tr_SessionMerges_GroupLabel ON dbo.SessionMerges AFTER INSERT AS
BEGIN
 SET NOCOUNT ON;
 UPDATE root SET TableGroupLabel=CONCAT('N-',root.Id)
 FROM dbo.DiningSessions root
 WHERE root.TableGroupLabel IS NULL
  AND EXISTS(SELECT 1 FROM inserted m WHERE m.MainSessionId=root.Id);
END;
GO
CREATE OR ALTER VIEW dbo.vw_ActiveTableGroups AS
 SELECT st.TableId,root.TableGroupLabel AS GroupLabel,root.Id AS MainSessionId,
  mainTable.Code AS MainTableCode,root.Status AS GroupStatus,
  (SELECT mt.Code AS code FROM dbo.SessionTables mst
   JOIN dbo.DiningSessions ms ON ms.Id=mst.SessionId
   JOIN dbo.DiningTables mt ON mt.Id=mst.TableId
   WHERE COALESCE(ms.BillingSessionId,ms.Id)=root.Id
     AND mst.ReleasedAt IS NULL AND ms.Status IN ('Open','AwaitingPayment')
   ORDER BY mt.Code FOR JSON PATH) AS MemberCodesJson
 FROM dbo.SessionTables st
 JOIN dbo.DiningSessions s ON s.Id=st.SessionId
 JOIN dbo.DiningSessions root ON root.Id=COALESCE(s.BillingSessionId,s.Id)
 CROSS APPLY (SELECT TOP(1) t.Code FROM dbo.SessionTables mainSt
   JOIN dbo.DiningTables t ON t.Id=mainSt.TableId
   WHERE mainSt.SessionId=root.Id AND mainSt.ReleasedAt IS NULL ORDER BY mainSt.Id) mainTable
 WHERE st.ReleasedAt IS NULL AND s.Status IN ('Open','AwaitingPayment')
  AND root.Status IN ('Open','AwaitingPayment') AND root.TableGroupLabel IS NOT NULL
  AND EXISTS(SELECT 1 FROM dbo.DiningSessions child
    JOIN dbo.SessionTables childSt ON childSt.SessionId=child.Id AND childSt.ReleasedAt IS NULL
    WHERE child.BillingSessionId=root.Id AND child.Status IN ('Open','AwaitingPayment'));
GO
GRANT SELECT ON dbo.vw_ActiveTableGroups TO restaurant_app;
GO
