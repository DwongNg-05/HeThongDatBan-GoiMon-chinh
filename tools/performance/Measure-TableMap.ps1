param(
    [int[]]$TableCounts = @(60,80),
    [ValidateRange(10,3600)][int]$SoakSeconds = 120,
    [string]$ConnectionString = 'Server=lpc:.\SQLEXPRESS;Database=RestaurantManagement_Dev;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True',
    [string]$NodePath = 'C:/Users/khacb/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe',
    [string]$NodeModules = 'C:/Users/khacb/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path '.').Path
if (!(Test-Path (Join-Path $projectRoot 'RestaurantManagement.sln'))) { throw 'Run from repository root.' }
if (@($TableCounts | Where-Object { $_ -notin @(60,80) }).Count) { throw 'Only 60 and 80 table fixtures are supported.' }
$outputDir = Join-Path $projectRoot '.local/task4-measurements'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$buildDir = Join-Path $projectRoot '.local/task4-verify'
$toolDll = Join-Path $buildDir 'RestaurantManagement.DbTool.dll'
$webDll = Join-Path $buildDir 'RestaurantManagement.Web.dll'
if (!(Test-Path $webDll)) { throw 'Build the solution into .local/task4-verify first.' }
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$computer = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem
@{ cpu=$cpu.Name; logicalProcessors=$cpu.NumberOfLogicalProcessors; ramGiB=[Math]::Round($computer.TotalPhysicalMemory/1GB,1); os=$os.Caption; osVersion=$os.Version; network='HTTP loopback; SQL shared memory; no physical LAN/Wi-Fi latency measured'; approvedByPO=$false } | ConvertTo-Json | Set-Content (Join-Path $outputDir 'environment.json') -Encoding utf8
$names = @('RM_CONNECTION_STRING','RM_DEMO_PASSWORD','RM_PERF_TABLE_COUNT','RM_PERF_URL','RM_PERF_REPORT','RM_PERF_SOAK_SECONDS','NODE_PATH','ASPNETCORE_ENVIRONMENT')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
try {
    $env:NODE_PATH = $NodeModules
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:RM_PERF_SOAK_SECONDS = [string]$SoakSeconds
    $env:RM_DEMO_PASSWORD = 'Perf!' + [Guid]::NewGuid().ToString('N')
    foreach ($count in $TableCounts) {
        $database = 'RestaurantManagement_Perf_' + [Guid]::NewGuid().ToString('N')
        $env:RM_CONNECTION_STRING = $ConnectionString -replace '(?i)Database=[^;]+', ('Database=' + $database)
        if ($env:RM_CONNECTION_STRING -notmatch [regex]::Escape('Database=' + $database)) { throw 'ConnectionString must contain Database=.' }
        $env:RM_PERF_TABLE_COUNT = [string]$count
        $env:RM_PERF_URL = 'http://127.0.0.1:5217'
        $env:RM_PERF_REPORT = Join-Path $outputDir "tables-$count.json"
        $server = $null
        try {
            & dotnet $toolDll seed-map-performance | Out-File (Join-Path $outputDir "fixture-$count.log")
            if ($LASTEXITCODE -ne 0) { throw "Fixture $count failed; see its log." }
            $dotnetExe = (Get-Command dotnet).Source
            $server = Start-Process -FilePath $dotnetExe -ArgumentList @('"'+$webDll+'"','--urls',$env:RM_PERF_URL,'--contentRoot','"'+(Join-Path $projectRoot 'src/RestaurantManagement.Web')+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $outputDir "web-$count.log") -RedirectStandardError (Join-Path $outputDir "web-$count-error.log")
            $ready = $false
            for ($attempt=0; $attempt -lt 40; $attempt++) {
                if ($server.HasExited) { throw 'Performance web process exited unexpectedly.' }
                try { Invoke-WebRequest "$($env:RM_PERF_URL)/Account/Login" -TimeoutSec 2 -UseBasicParsing | Out-Null; $ready=$true; break } catch { Start-Sleep -Milliseconds 250 }
            }
            if (!$ready) { throw 'Performance web process did not start.' }
            & $NodePath (Join-Path $projectRoot 'tools/performance/table-map-benchmark.cjs')
            if ($LASTEXITCODE -ne 0) { Write-Warning "Benchmark $count did not complete; inspect its JSON report." }
        } finally {
            if ($server -and !$server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit() }
            & dotnet $toolDll drop-map-performance
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not clean up isolated database $database." }
        }
    }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
}
Write-Output "Reports: $outputDir"
