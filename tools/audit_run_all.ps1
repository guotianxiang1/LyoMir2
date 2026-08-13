# Run every AuditTools project and record PASS/FAIL.
#
# Why this exists: three separate gotchas make a naive `dotnet run` sweep
# report garbage in this environment.
#   1. `dotnet run --project X -- <arg>` does not forward argv here, so tools
#      that take arguments must be launched as executables.
#   2. Build output lands in three different places: local bin for standalone
#      analysers, the SHARED ..\..\..\Build\AuditTools\<name>\ for anything
#      with a ProjectReference (that directory is shared across worktrees), and
#      ..\..\Build\Mir200 for GameSvr itself.
#   3. Many tools resolve the repository through AppContext.BaseDirectory and
#      then the current directory, so the working directory must be the repo
#      root or they fail before reaching a single assertion.
#
# Build first:  dotnet build AuditTools\_buildall.proj -m:8
param(
    [string]$Repo = (Split-Path $PSScriptRoot -Parent),
    [int]$TimeoutSec = 90,
    [string]$OutDir
)
$ErrorActionPreference = 'Continue'
if (-not $OutDir) { $OutDir = Join-Path $Repo '_toolruns_root' }
$sharedBuild = Join-Path (Split-Path $Repo -Parent) 'Build'
$extraRoots = @(
    (Join-Path $sharedBuild 'AuditTools'),
    (Join-Path $sharedBuild 'Mir200'),
    (Join-Path (Split-Path $Repo -Parent) 'tmp')
)
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$projects = Get-ChildItem -Path (Join-Path $Repo 'AuditTools') -Recurse -Filter '*.csproj' | Sort-Object Name
$results = @(); $i = 0

foreach ($p in $projects) {
    $i++
    $name = $p.BaseName
    $cands = @()
    $cands += Get-ChildItem -Path (Join-Path $p.Directory.FullName 'bin') -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue
    foreach ($r in $extraRoots) { $cands += Get-ChildItem -Path $r -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue }
    $exe = if ($cands.Count -gt 0) { ($cands | Sort-Object LastWriteTime -Descending)[0].FullName } else { $null }

    if (-not $exe) {
        $results += [pscustomobject]@{ Name = $name; Status = 'NOEXE'; Exit = -999; Secs = 0; Tail = 'no built exe found' }
        Write-Host ("[{0}/{1}] {2} : NOEXE" -f $i, $projects.Count, $name); continue
    }

    $so = Join-Path $OutDir "$name.out.txt"; $se = Join-Path $OutDir "$name.err.txt"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.WorkingDirectory = $Repo
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $sw = [System.Diagnostics.Stopwatch]::StartNew(); $status = 'UNKNOWN'; $code = -998
    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
        $tOut = $proc.StandardOutput.ReadToEndAsync(); $tErr = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
            try { $proc.Kill($true) } catch { }
            $status = 'TIMEOUT'; $code = -1
        }
        else {
            $code = $proc.ExitCode
            $status = if ($code -eq 0) { 'PASS' } else { 'FAIL' }
        }
        try { $tOut.Result | Out-File -Encoding utf8 $so } catch { }
        try { $tErr.Result | Out-File -Encoding utf8 $se } catch { }
        $proc.Dispose()
    }
    catch { $status = 'LAUNCHERR'; $code = -997; $_.Exception.Message | Out-File -Encoding utf8 $se }
    $sw.Stop()

    $tail = ''
    $errLines = @(); if (Test-Path $se) { $errLines = Get-Content $se -ErrorAction SilentlyContinue | Where-Object { $_.Trim() -ne '' } }
    if ($errLines.Count -gt 0) { $tail = ($errLines | Select-Object -First 4) -join ' | ' }
    elseif (Test-Path $so) {
        $o = Get-Content $so -ErrorAction SilentlyContinue | Where-Object { $_.Trim() -ne '' }
        if ($o.Count -gt 0) { $tail = ($o | Select-Object -Last 3) -join ' | ' }
    }
    if ($tail.Length -gt 500) { $tail = $tail.Substring(0, 500) }

    $results += [pscustomobject]@{ Name = $name; Status = $status; Exit = $code; Secs = [math]::Round($sw.Elapsed.TotalSeconds, 1); Tail = $tail }
    Write-Host ("[{0}/{1}] {2} : {3} exit={4} ({5}s)" -f $i, $projects.Count, $name, $status, $code, [math]::Round($sw.Elapsed.TotalSeconds, 1))
}

$results | Export-Csv -Path (Join-Path $OutDir '_summary.csv') -NoTypeInformation -Encoding UTF8
Write-Host "=== SUMMARY ==="
$results | Group-Object Status | ForEach-Object { "{0} = {1}" -f $_.Name, $_.Count }
Write-Host "=== NON-PASS ==="
$results | Where-Object { $_.Status -ne 'PASS' } | ForEach-Object { "{0}`t{1}`texit={2}`t{3}" -f $_.Name, $_.Status, $_.Exit, $_.Tail }
Write-Host "=== DONE ==="
