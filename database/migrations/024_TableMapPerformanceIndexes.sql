-- Cover only active map tiles; details and order totals are queried on demand.
CREATE INDEX IX_DiningTables_ActiveMap
 ON dbo.DiningTables(AreaId,SortOrder,Code)
 INCLUDE(MaxCapacity,Status,StatusChangedAt)
 WHERE IsActive=1;
CREATE INDEX IX_Areas_ActiveMap
 ON dbo.Areas(SortOrder,Id) INCLUDE(Name) WHERE IsActive=1;
GO
