-- S2-07: covering indexes for the read-only table-map snapshot.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DiningTables') AND name=N'IX_DiningTables_ActiveMap')
 CREATE INDEX IX_DiningTables_ActiveMap ON dbo.DiningTables(AreaId,SortOrder,Code)
 INCLUDE(MaxCapacity,Status,StatusChangedAt) WHERE IsActive=1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Areas') AND name=N'IX_Areas_ActiveMap')
 CREATE INDEX IX_Areas_ActiveMap ON dbo.Areas(SortOrder,Id) INCLUDE(Name) WHERE IsActive=1;
GO
