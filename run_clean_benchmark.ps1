$url = "http://localhost:5240/Product"

# 1. Warm request test (10 runs)
$times = @()
for ($i = 1; $i -le 10; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $res = Invoke-WebRequest -Uri $url -UseBasicParsing
    $sw.Stop()
    $ms = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)
    $times += $ms
    Write-Host "Run $i : $ms ms (Status: $($res.StatusCode))"
}

$min = ($times | Measure-Object -Minimum).Minimum
$avg = [math]::Round(($times | Measure-Object -Average).Average, 1)
$max = ($times | Measure-Object -Maximum).Maximum

Write-Host "---"
Write-Host "BASELINE API BENCHMARK: GET /Product"
Write-Host "Min Latency: $min ms"
Write-Host "Avg Latency: $avg ms"
Write-Host "Max Latency: $max ms"
