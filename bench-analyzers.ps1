# Measures clean-rebuild wall time for each analyzer configuration.
# 8 configs × 3 runs each. Uses --no-incremental so analyzers run on every file.
# Restore is done once per config (since the package set differs); --no-restore on builds.

$ErrorActionPreference = 'Stop'
$slnx = 'Locate.slnx'
$baseFlags = @('-p:Configuration=Debug', '-p:Platform=x64', '-p:TreatWarningsAsErrors=false', '-v:q', '-nologo')

# Each config disables certain analyzers via /p flags. The two existing analyzers
# (IDisposableAnalyzers, Lindhart) stay ON in every config so we measure ONLY the
# six newly-added analyzers.
$AllSix = @('Threading', 'Meziantou', 'AsyncFixer', 'NetAnalyzers', 'Roslynator', 'Sonar')
$NameToProp = @{
    'Threading'    = 'EnableThreadingAnalyzers'
    'Meziantou'    = 'EnableMeziantouAnalyzer'
    'AsyncFixer'   = 'EnableAsyncFixer'
    'NetAnalyzers' = 'EnableNetAnalyzers'
    'Roslynator'   = 'EnableRoslynator'
    'Sonar'        = 'EnableSonarAnalyzer'
}

function DisableFlags([string[]] $names) {
    $flags = @()
    foreach ($n in $names) {
        $flags += "-p:$($NameToProp[$n])=false"
    }
    return $flags
}

# Build the configurations: baseline (all 6 off), one analyzer at a time, then all 6 on.
$configs = [ordered]@{
    'baseline'     = DisableFlags $AllSix                                 # only the 2 existing
    'Threading'    = DisableFlags ($AllSix | ? { $_ -ne 'Threading' })
    'Meziantou'    = DisableFlags ($AllSix | ? { $_ -ne 'Meziantou' })
    'AsyncFixer'   = DisableFlags ($AllSix | ? { $_ -ne 'AsyncFixer' })
    'NetAnalyzers' = DisableFlags ($AllSix | ? { $_ -ne 'NetAnalyzers' })
    'Roslynator'   = DisableFlags ($AllSix | ? { $_ -ne 'Roslynator' })
    'Sonar'        = DisableFlags ($AllSix | ? { $_ -ne 'Sonar' })
    'all-six'      = @()                                                  # all 6 enabled
}

$results = [ordered]@{}

# Global warm-up: build once before the loop so the first measured config doesn't pay
# the cold-cache penalty (file system, MSBuild node spawn, Roslyn JIT). Without this,
# baseline-run-first always reads as ~1.5 s slower than every other config.
Write-Host "warm-up build..." -ForegroundColor DarkGray
& dotnet restore $slnx @baseFlags *> $null
& dotnet build $slnx @baseFlags '--no-restore' '--no-incremental' *> $null

foreach ($name in $configs.Keys) {
    $flags = $configs[$name]
    Write-Host ""
    Write-Host "=== $name ===" -ForegroundColor Cyan

    # Restore once for this config (package set may differ)
    & dotnet restore $slnx @baseFlags @flags *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  restore failed"; continue }

    $times = @()
    # 5 iterations: throw away the first (warm-up — JIT, file cache effects), average the rest.
    # Better signal than median-of-3 for sub-second deltas on a ~12 s build.
    for ($i = 1; $i -le 5; $i++) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & dotnet build $slnx @baseFlags @flags '--no-restore' '--no-incremental' *> $null
        $sw.Stop()
        $secs = [Math]::Round($sw.Elapsed.TotalSeconds, 2)
        $times += $secs
        Write-Host ("  run {0}: {1:N2}s" -f $i, $secs)
    }
    $kept = $times[1..4]
    $mean = ($kept | Measure-Object -Average).Average
    $stdev = [Math]::Sqrt((($kept | ForEach-Object { ($_ - $mean) * ($_ - $mean) }) | Measure-Object -Sum).Sum / ($kept.Length - 1))
    $results[$name] = @{ times = $times; mean = $mean; stdev = $stdev }
    Write-Host ("  mean (runs 2-5): {0:N2}s  stdev {1:N2}s" -f $mean, $stdev) -ForegroundColor Yellow
}

# Final report
Write-Host ""
Write-Host "================ Build-cost summary ================" -ForegroundColor Green
$baseline = $results['baseline'].mean
foreach ($name in $configs.Keys) {
    $m = $results[$name].mean
    $sd = $results[$name].stdev
    $delta = $m - $baseline
    if ($name -eq 'baseline') {
        Write-Host ("  {0,-14} {1,6:N2}s ± {2,4:N2}s            (only IDisposableAnalyzers + Lindhart)" -f $name, $m, $sd)
    } else {
        $sign = if ($delta -ge 0) { '+' } else { '' }
        Write-Host ("  {0,-14} {1,6:N2}s ± {2,4:N2}s    {3}{4:N2}s" -f $name, $m, $sd, $sign, $delta)
    }
}
