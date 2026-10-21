# Simulates ClickUp calling the webhook (signed with your secret), without needing ClickUp or a tunnel.
# Usage: .\scripts\test-clickup-webhook.ps1 -Secret "<ClickUp__WebhookSecret from .env>" [-TaskId 86abc123] [-Status "under review"]
param(
  [Parameter(Mandatory)][string]$Secret,
  [string]$TaskId = "86abc123",
  [string]$Status = "under review",
  [string]$Base = "http://localhost:8080"
)
$id = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$body = '{"event":"taskStatusUpdated","webhook_id":"wh-test","task_id":"' + $TaskId + '","history_items":[{"id":"' + $id + '","date":"' + $id + '","field":"status","before":{"status":"in progress"},"after":{"status":"' + $Status + '"}}]}'
$hmac = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Secret))
$sig = ([BitConverter]::ToString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($body))) -replace "-", "").ToLower()

Write-Host "1) valid signature:"
Invoke-RestMethod -Method Post -Uri "$Base/api/v1/webhooks/clickup" -Headers @{ "X-Signature" = $sig } -ContentType "application/json" -Body $body | ConvertTo-Json
Write-Host "2) wrong signature (expect 401):"
try { Invoke-RestMethod -Method Post -Uri "$Base/api/v1/webhooks/clickup" -Headers @{ "X-Signature" = "deadbeef" } -ContentType "application/json" -Body $body }
catch { Write-Host "HTTP $([int]$_.Exception.Response.StatusCode)" }
