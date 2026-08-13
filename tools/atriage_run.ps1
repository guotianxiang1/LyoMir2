param(
    [Parameter(Mandatory = $true)][string[]]$Name,
    [switch]$NoBuild,
    [string[]]$ToolArgs = @(),
    [int]$TimeoutSec = 180
)

$repo = "D:\loym2\.claude\wt3\atriageA"
$shared = "D:\loym2\.claude\wt3\Build"

foreach ($n in $Name) {
    $proj = Join-Path $repo "AuditTools\$n\$n.csproj"
    if (-not (Test-Path $proj)) { Write-Host "$n : NOPROJ"; continue }

    if (-not $NoBuild) {
        $log = & dotnet build $proj -v q --nologo 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "$n : BUILDFAIL"
            $log | Select-String -Pattern "error" | Select-Object -First 8 | ForEach-Object { "    $_" }
            continue
        }
    }

    $cands = @()
    $cands += Get-ChildItem -Path (Join-Path $repo "AuditTools\$n\bin") -Recurse -Filter "$n.exe" -Force -ErrorAction SilentlyContinue
    $cands += Get-ChildItem -Path (Join-Path $shared "AuditTools\$n") -Recurse -Filter "$n.exe" -Force -ErrorAction SilentlyContinue
    if ($cands.Count -eq 0) { Write-Host "$n : NOEXE"; continue }
    $exe = ($cands | Sort-Object LastWriteTime -Descending)[0].FullName

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    if ($ToolArgs.Count -gt 0) {
        $psi.Arguments = ($ToolArgs | ForEach-Object { '"' + $_ + '"' }) -join ' '
    }
    $psi.WorkingDirectory = (Split-Path $exe -Parent)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $tOut = $proc.StandardOutput.ReadToEndAsync()
    $tErr = $proc.StandardError.ReadToEndAsync()
    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        try { $proc.Kill($true) } catch { }
        Write-Host "$n : TIMEOUT"
        continue
    }
    $code = $proc.ExitCode
    Write-Host ("=== {0} : exit={1} ({2}) ===" -f $n, $code, $exe)
    $o = $tOut.Result; $e = $tErr.Result
    if ($o) { ($o -split "`n" | Select-Object -Last 12) -join "`n" }
    if ($e) { "--- stderr ---"; ($e -split "`n" | Select-Object -First 12) -join "`n" }
}
