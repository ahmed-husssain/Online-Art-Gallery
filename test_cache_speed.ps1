# Test in-memory cache speed vs DB speed
$dict = [System.Collections.Generic.Dictionary[string, object]]::new()
$dict["catalog_cache"] = @(1..5)

$cacheTimes = @()
for ($i=0; $i -lt 10; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $data = $dict["catalog_cache"]
    $sw.Stop()
    $cacheTimes += $sw.Elapsed.TotalMilliseconds
}

$cacheAvg = [math]::Round(($cacheTimes | Measure-Object -Average).Average, 3)
$cacheMin = [math]::Round(($cacheTimes | Measure-Object -Minimum).Minimum, 3)

Write-Host "REAL IN-MEMORY CACHE TIMING: Min: $cacheMin ms, Avg: $cacheAvg ms"
