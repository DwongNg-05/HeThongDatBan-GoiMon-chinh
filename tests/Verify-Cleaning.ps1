$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$db='RestaurantCleaningTest_'+[Guid]::NewGuid().ToString('N')
$env:RM_CONNECTION_STRING="Server=(localdb)\MSSQLLocalDB;Database=$db;Integrated Security=true;TrustServerCertificate=true"
$env:RM_DEMO_PASSWORD='CleaningTest-'+[Guid]::NewGuid().ToString('N')
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:Smtp__Username=''
$env:Email__RetryPollSeconds='0'
$env:EmailVerification__Enabled='false'
$proc=$null
function Sql($query){sqlcmd -S '(localdb)\MSSQLLocalDB' -d $db -E -b -I -Q $query; if($LASTEXITCODE -ne 0){throw 'SQL failed'}}
try{
 dotnet .local/db-startup-fix/RestaurantManagement.DbTool.dll migrate
 if($LASTEXITCODE -ne 0){throw 'Migration failed'}
 dotnet .local/db-startup-fix/RestaurantManagement.DbTool.dll seed-login-demo
 if($LASTEXITCODE -ne 0){throw 'Seed failed'}
 Sql @'
 DECLARE @a int=(SELECT TOP(1) Id FROM dbo.Areas),@u int=(SELECT Id FROM dbo.Users WHERE UserName='demo-manager');
 INSERT dbo.DiningTables(AreaId,Code,MaxCapacity,Status)
 VALUES(@a,'CLEAN1',4,'Cleaning'),(@a,'FREE1',4,'Available'),(@a,'RES1',4,'Reserved'),(@a,'SERV1',4,'Serving'),(@a,'BUSY1',4,'Cleaning'),(@a,'RACE1',4,'Cleaning');
 INSERT dbo.Shifts(Name,BusinessDate,OpenedBy) VALUES('Test',CONVERT(date,GETDATE()),@u);
 DECLARE @shift bigint=SCOPE_IDENTITY();
 INSERT dbo.DiningSessions(ShiftId,GuestCount,OpenedBy) VALUES(@shift,2,@u);
 DECLARE @session bigint=SCOPE_IDENTITY();
 INSERT dbo.SessionTables(SessionId,TableId) SELECT @session,Id FROM dbo.DiningTables WHERE Code='BUSY1';
 INSERT dbo.Users(RoleId,FullName,UserName,Phone) VALUES(2,'Waiter','clean-waiter','0900000098'),(3,'Kitchen','clean-kitchen','0900000097');
 DECLARE @k int=(SELECT Id FROM dbo.Users WHERE UserName='clean-kitchen');
 BEGIN TRY
  EXEC dbo.usp_CompleteTableCleaning 'RACE1',@k;
  THROW 51999,'Kitchen permission check failed',1;
 END TRY BEGIN CATCH IF ERROR_NUMBER()<>51001 THROW; END CATCH;
'@
 $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
 $env:CLEANING_TEST_URL="http://127.0.0.1:$port/"
 $proc=Start-Process dotnet -ArgumentList @((Join-Path $root '.local/cleaning-check/RestaurantManagement.Web.dll'),'--urls',$env:CLEANING_TEST_URL) -WorkingDirectory (Join-Path $root 'src/RestaurantManagement.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root '.local/cleaning-check/web.log') -RedirectStandardError (Join-Path $root '.local/cleaning-check/web.err.log')
 $ready=$false
 for($i=0;$i -lt 40;$i++){try{$null=Invoke-WebRequest ($env:CLEANING_TEST_URL+'Account/Login') -TimeoutSec 2;$ready=$true;break}catch{Start-Sleep -Milliseconds 250}}
 if(!$ready){throw 'Web did not start'}
 node tests/cleaning-browser.cjs
 if($LASTEXITCODE -ne 0){throw 'Browser tests failed'}
 Sql @'
 IF (SELECT COUNT(*) FROM dbo.TableStatusChangeEvents WHERE TableCode='RACE1' AND PreviousStatus='Cleaning' AND Status='Available')<>1 THROW 51999,'Duplicate event',1;
 IF (SELECT Status FROM dbo.DiningTables WHERE Code='BUSY1')<>'Cleaning' THROW 51999,'Active session released',1;
 UPDATE dbo.DiningTables SET Status='Cleaning' WHERE Code='RACE1';
 DECLARE @w int=(SELECT Id FROM dbo.Users WHERE UserName='clean-waiter');
 EXEC dbo.usp_CompleteTableCleaning 'RACE1',@w;
 IF (SELECT Status FROM dbo.DiningTables WHERE Code='RACE1')<>'Available' THROW 51999,'Waiter failed',1;
 PRINT 'PASS: Waiter allowed, Kitchen blocked, active session retained, one event for concurrent completion';
'@
}finally{
 if($proc -and !$proc.HasExited){Stop-Process -Id $proc.Id}
 sqlcmd -S '(localdb)\MSSQLLocalDB' -d master -E -b -Q "IF DB_ID('$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END;"
}
