# End-to-end smoke test for Windows PowerShell.
# Usage: .\scripts\smoke-test.ps1 -ApiKey "<WebhookAuth__ApiKey>" [-Base http://localhost:8080]
param([Parameter(Mandatory)][string]$ApiKey, [string]$Base = "http://localhost:8080")
$script:pass = 0; $script:fail = 0
function Check($name, $actual, $expected) {
  if ("$actual" -eq "$expected") { Write-Host "PASS  $name" -ForegroundColor Green; $script:pass++ }
  else { Write-Host "FAIL  $name (expected $expected, got $actual)" -ForegroundColor Red; $script:fail++ }
}
function Post($body, $key) {
  $h = @{}; if ($key) { $h["X-Api-Key"] = $key }
  try { $r = Invoke-WebRequest -Method Post -Uri "$Base/api/v1/reviews" -Headers $h -ContentType "application/json" -Body ($body | ConvertTo-Json) -UseBasicParsing
        return @{ Code = [int]$r.StatusCode; Json = ($r.Content | ConvertFrom-Json) } }
  catch { return @{ Code = [int]$_.Exception.Response.StatusCode; Json = $null } }
}
$id = "smoke-" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$body = @{ eventId=$id; taskId="86abc123"; taskUrl="https://app.clickup.com/t/86abc123"; status="Under Review";
  title="Review payment retry handling"; description="Check retry behaviour and idempotency. password=hunter2";
  repositoryUrl="https://github.com/acme/svc" }

Check "health/ready" (Invoke-WebRequest "$Base/api/v1/health/ready" -UseBasicParsing).StatusCode 200
Check "no api key -> 401" (Post $body $null).Code 401
Check "wrong api key -> 401" (Post $body "nope").Code 401
Check "missing taskId -> 400" (Post @{ eventId="x"; status="Under Review" } $ApiKey).Code 400
Check "bad repo host -> 400" (Post @{ eventId="x"; taskId="t"; status="Under Review"; repositoryUrl="https://evil.example.com/r" } $ApiKey).Code 400
Check "other status ignored -> 200" (Post @{ eventId="y"; taskId="t"; status="In Progress" } $ApiKey).Code 200
$first = Post $body $ApiKey
Check "valid request -> 202" $first.Code 202
$dup = Post $body $ApiKey
Check "same eventId -> 202 duplicate" ($dup.Code, $dup.Json.duplicate -join ",") "202,True"

$job = $first.Json.jobId; $status = ""
for ($i = 0; $i -lt 30; $i++) { $d = Invoke-RestMethod "$Base/api/v1/reviews/$job"; $status = $d.status; if ($status -eq "Completed") { break }; Start-Sleep 1 }
Check "job reaches Completed" $status "Completed"
Check "write-back Delivered" $d.writeBackStatus "Delivered"
Check "secret not leaked" (($d | ConvertTo-Json -Depth 6) -match "hunter2") $false
Check "metrics endpoint" (Invoke-WebRequest "$Base/api/v1/metrics/summary" -UseBasicParsing).StatusCode 200
Write-Host "`npassed=$script:pass failed=$script:fail"; if ($script:fail -gt 0) { exit 1 }
