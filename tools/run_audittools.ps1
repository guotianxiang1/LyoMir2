# Sweep runner for AuditTools/.
#
# Two things the ad-hoc sweep got wrong, both of which manufactured red:
#
#  1. Working directory. The old runner launched every exe with its own bin
#     directory as the CWD. That directory lives outside the checkout (the
#     shared Debug OutputPath is ..\..\..\Build\AuditTools\<name>\), so every
#     source-scanning audit walked up from it, never found GameSvr/GameSvr.csproj
#     and threw DirectoryNotFoundException before running a single assertion.
#     Four checks in one 21-tool slice went green purely from running with the
#     repository root as CWD. -RepoRoot defaults to this script's parent.
#
#  2. Exit-code classification. Several audits use exit 2 as this tree's
#     INCOMPLETE/SKIP convention -- NativeHonorDbCheck needs a live MySQL
#     connection string, MovementReliveCheck needs a plaintext client root, and
#     both say so on stdout instead of throwing. Scoring those as FAIL buries the
#     real failures. They are reported as SKIP here, and deliberately NOT as PASS:
#     a skipped audit proves nothing, so it must stay visibly not-green.
#
# Tools that need arguments still need them; pass them through -ToolArgs, e.g.
#   -ToolArgs @{ NativeHonorDbCheck = @('server=...;uid=...;pwd=...') }
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$OutDir = (Join-Path $PSScriptRoot '_toolruns'),
    [string[]]$Only = @(),
    [hashtable]$ToolArgs = @{},
    [int]$TimeoutSec = 120
)

$ErrorActionPreference = 'Continue'
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$buildRoot = Join-Path (Split-Path $RepoRoot -Parent) 'Build'
$artifactRoot = Join-Path $RepoRoot 'artifacts\obj'
$projects = Get-ChildItem -Path (Join-Path $RepoRoot 'AuditTools') -Recurse -Filter '*.csproj' | Sort-Object Name
if ($Only.Count -gt 0) { $projects = $projects | Where-Object { $Only -contains $_.BaseName } }

$results = @()
$index = 0
foreach ($project in $projects) {
    $index++
    $name = $project.BaseName

    # Prefer outputs whose parent chain cannot be mistaken for a source root.
    # artifacts/obj contains a generated GameSvr directory, so source scanners
    # that stop at the first matching directory would otherwise inspect the
    # wrong tree. Fall back to artifacts only when no canonical output exists.
    $candidateGroups = @(
        (Get-ChildItem -Path (Join-Path $project.Directory.FullName 'bin') -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue),
        (Get-ChildItem -Path (Join-Path $buildRoot "AuditTools\$name") -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue),
        (Get-ChildItem -Path (Join-Path $buildRoot 'Mir200') -Filter "$name.exe" -ErrorAction SilentlyContinue),
        (Get-ChildItem -Path (Join-Path $artifactRoot $name) -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue)
    )
    $exe = $null
    foreach ($group in $candidateGroups) {
        $candidate = @($group) | Where-Object { $_.BaseName -eq $name } |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($null -ne $candidate) { $exe = $candidate.FullName; break }
    }
    if (-not $exe) {
        $results += [pscustomobject]@{ Name = $name; Status = 'NOEXE'; Exit = -999; Secs = 0; Tail = 'no built exe found' }
        Write-Host ("[{0}/{1}] {2} : NOEXE" -f $index, $projects.Count, $name)
        continue
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    foreach ($argument in @($ToolArgs[$name])) {
        if ($null -ne $argument) { $psi.ArgumentList.Add([string]$argument) }
    }
    # The audits resolve repository files relative to the CWD; the bin directory
    # is outside the checkout, so it has to be the repository root.
    $psi.WorkingDirectory = $RepoRoot
    $psi.UseShellExecute = $false
    $psi.Environment['M2_REPO_ROOT'] = $RepoRoot
    $psi.Environment['LYOMIR_REPO_ROOT'] = $RepoRoot
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $psi.CreateNoWindow = $true

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $status = 'UNKNOWN'; $code = -998; $stdout = ''; $stderr = ''
    try {
        $process = [System.Diagnostics.Process]::Start($psi)
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSec * 1000)) {
            try { $process.Kill($true) } catch { }
            try { $process.WaitForExit(5000) | Out-Null } catch { }
            $status = 'TIMEOUT'; $code = -1
        }
        else { $code = $process.ExitCode }
        $stdout = $outTask.Result
        $stderr = $errTask.Result
        $process.Dispose()
    }
    catch {
        $status = 'LAUNCHERR'; $code = -997; $stderr = $_.Exception.Message
    }
    $watch.Stop()

    $stdout | Out-File -Encoding utf8 (Join-Path $OutDir "$name.out.txt")
    $stderr | Out-File -Encoding utf8 (Join-Path $OutDir "$name.err.txt")

    if ($status -eq 'UNKNOWN') {
        $announcedSkip = ($stdout + "`n" + $stderr) -match '(?m)^\s*(SKIP|INCOMPLETE)\b'
        if ($code -eq 0) { $status = 'PASS' }
        elseif ($code -eq 2 -and $announcedSkip) { $status = 'SKIP' }
        else { $status = 'FAIL' }
    }

    $tail = ''
    $errLines = ($stderr -split "`n") | Where-Object { $_.Trim() -ne '' }
    if ($errLines.Count -gt 0) { $tail = ($errLines | Select-Object -First 4) -join ' | ' }
    else {
        $outLines = ($stdout -split "`n") | Where-Object { $_.Trim() -ne '' }
        if ($outLines.Count -gt 0) { $tail = ($outLines | Select-Object -Last 3) -join ' | ' }
    }
    if ($tail.Length -gt 500) { $tail = $tail.Substring(0, 500) }

    $results += [pscustomobject]@{ Name = $name; Status = $status; Exit = $code; Secs = [math]::Round($watch.Elapsed.TotalSeconds, 1); Tail = $tail }
    Write-Host ("[{0}/{1}] {2} : {3} exit={4} ({5}s)" -f $index, $projects.Count, $name, $status, $code, [math]::Round($watch.Elapsed.TotalSeconds, 1))
}

$results | Export-Csv -Path (Join-Path $OutDir '_summary.csv') -NoTypeInformation -Encoding UTF8
Write-Host '=== SUMMARY ==='
$results | Group-Object Status | ForEach-Object { '{0} = {1}' -f $_.Name, $_.Count }
Write-Host '=== NON-PASS ==='
$results | Where-Object { $_.Status -ne 'PASS' } | ForEach-Object { "{0}`t{1}`texit={2}`t{3}" -f $_.Name, $_.Status, $_.Exit, $_.Tail }
Write-Host '=== DONE ==='
