$url = "http://localhost:5240/Product"

# 1. Cold Request Measurement
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$res = Invoke-WebRequest -Uri $url -UseBasicParsing
$sw.Stop()
$coldMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)
Write-Host "COLD REQUEST: $coldMs ms (Status: $($res.StatusCode))"

# 2. Warm Requests (20 iterations)
$times = @()
$client = [System.Net.Http.HttpClient]::new()
for ($i = 0; $i -lt 20; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $response = $client.GetAsync($url).Result
    $content = $response.Content.ReadAsStringAsync().Result
    $sw.Stop()
    $times += $sw.Elapsed.TotalMilliseconds
}

$minMs = [math]::Round(($times | Measure-Object -Minimum).Minimum, 2)
$avgMs = [math]::Round(($times | Measure-Object -Average).Average, 2)
$maxMs = [math]::Round(($times | Measure-Object -Maximum).Maximum, 2)
$p95Ms = [math]::Round(($times | Sort-Object)[[math]::Floor($times.Count * 0.95)], 2)

Write-Host "WARM REQUESTS (20 runs):"
Write-Host "  Min: $minMs ms"
Write-Host "  Avg: $avgMs ms"
Write-Host "  P95: $p95Ms ms"
Write-Host "  Max: $maxMs ms"

# 3. Filter / Search request
$searchUrl = "http://localhost:5240/Product?searchString=Fluidity&sortOrder=price_desc"
$searchTimes = @()
for ($i = 0; $i -lt 10; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $response = $client.GetAsync($searchUrl).Result
    $content = $response.Content.ReadAsStringAsync().Result
    $sw.Stop()
    $searchTimes += $sw.Elapsed.TotalMilliseconds
}
$searchAvg = [math]::Round(($searchTimes | Measure-Object -Average).Average, 2)
Write-Host "SEARCH & FILTER REQUEST (Avg): $searchAvg ms"

$client.Dispose()
