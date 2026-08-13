# Re-run the failing subset against a baseline commit to separate pre-existing
# failures from regressions introduced by the window under audit.
#
# Setup (baseline side), from the real repo root D:\loym2\LyoMir2-master:
#   git worktree add --detach <BaselineWorktree> <baseline-commit>
#   copy AuditTools\_buildall.proj into <BaselineWorktree>\AuditTools\
#   dotnet build <BaselineWorktree>\AuditTools\_buildall.proj -m:8 -p:OutputPath=<ExeDir>\
#
# The -p:OutputPath override is load-bearing: without it the baseline build
# writes into ..\..\..\Build\AuditTools\<name>\, which is SHARED with every
# other worktree and would clobber the binaries under audit.
param(
    [string]$Repo = (Split-Path $PSScriptRoot -Parent),
    [string]$ExeDir = "D:\loym2\.claude\wt2\_ab_out",
    [string]$BaselineWorktree = "D:\loym2\.claude\wt2\abbase",
    [string]$FailListCsv,
    [int]$TimeoutSec = 120,
    [string]$OutDir
)
$ErrorActionPreference = 'Continue'
if (-not $FailListCsv) { $FailListCsv = Join-Path $Repo '_toolruns_root\_summary.csv' }
if (-not $OutDir) { $OutDir = Join-Path $Repo '_toolruns_ab' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$names = (Import-Csv $FailListCsv | Where-Object { $_.Status -ne 'PASS' }).Name
$results = @(); $i = 0
foreach ($name in $names) {
    $i++
    $exe = Join-Path $ExeDir "$name.exe"
    if (-not (Test-Path $exe)) {
        $results += [pscustomobject]@{ Name = $name; Status = 'NOEXE'; Exit = -999; Secs = 0; Tail = 'not built at baseline' }
        Write-Host ("[{0}/{1}] {2} : NOEXE" -f $i, $names.Count, $name); continue
    }
    $so = Join-Path $OutDir "$name.out.txt"; $se = Join-Path $OutDir "$name.err.txt"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.WorkingDirectory = $BaselineWorktree
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $sw = [System.Diagnostics.Stopwatch]::StartNew(); $status = 'UNKNOWN'; $code = -998
    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
        $tOut = $proc.StandardOutput.ReadToEndAsync(); $tErr = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit($TimeoutSec * 1000)) { try { $proc.Kill($true) } catch { }; $status = 'TIMEOUT'; $code = -1 }
        else { $code = $proc.ExitCode; $status = if ($code -eq 0) { 'PASS' } else { 'FAIL' } }
        try { $tOut.Result | Out-File -Encoding utf8 $so } catch { }
        try { $tErr.Result | Out-File -Encoding utf8 $se } catch { }
        $proc.Dispose()
    }
    catch { $status = 'LAUNCHERR'; $code = -997; $_.Exception.Message | Out-File -Encoding utf8 $se }
    $sw.Stop()
    $tail = ''
    $errLines = @(); if (Test-Path $se) { $errLines = Get-Content $se -ErrorAction SilentlyContinue | Where-Object { $_.Trim() -ne '' } }
    if ($errLines.Count -gt 0) { $tail = ($errLines | Select-Object -First 2) -join ' | ' }
    elseif (Test-Path $so) { $o = Get-Content $so -ErrorAction SilentlyContinue | Where-Object { $_.Trim() -ne '' }; if ($o.Count -gt 0) { $tail = ($o | Select-Object -Last 2) -join ' | ' } }
    if ($tail.Length -gt 300) { $tail = $tail.Substring(0, 300) }
    $results += [pscustomobject]@{ Name = $name; Status = $status; Exit = $code; Secs = [math]::Round($sw.Elapsed.TotalSeconds, 1); Tail = $tail }
    Write-Host ("[{0}/{1}] {2} : {3} exit={4}" -f $i, $names.Count, $name, $status, $code)
}
$results | Export-Csv -Path (Join-Path $OutDir '_summary.csv') -NoTypeInformation -Encoding UTF8
Write-Host "=== AB SUMMARY (baseline) ==="
$results | Group-Object Status | ForEach-Object { "{0} = {1}" -f $_.Name, $_.Count }
Write-Host "=== BASELINE-PASS but NOW-FAIL => introduced in the audited window ==="
$now = Import-Csv $FailListCsv
$bh = @{}; $results | ForEach-Object { $bh[$_.Name] = $_ }
$now | Where-Object { $_.Status -ne 'PASS' -and $bh[$_.Name] -and $bh[$_.Name].Status -eq 'PASS' } | ForEach-Object { $_.Name }
Write-Host "=== AB DONE ==="
