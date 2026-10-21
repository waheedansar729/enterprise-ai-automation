# Registers a FREE native ClickUp webhook that calls this project directly (no Power Automate).
# Usage:
#   .\scripts\register-clickup-webhook.ps1 -Token "pk_..." -Endpoint "https://<your-tunnel>/api/v1/webhooks/clickup"
#   (optional) -TeamId 1234567 -ListId 901234567
param(
  [Parameter(Mandatory)][string]$Token,
  [Parameter(Mandatory)][string]$Endpoint,
  [string]$TeamId,
  [string]$ListId
)
$h = @{ Authorization = $Token }
if (-not $TeamId) {
  $teams = (Invoke-RestMethod -Uri "https://api.clickup.com/api/v2/team" -Headers $h).teams
  $teams | ForEach-Object { Write-Host "Workspace: $($_.name)  id=$($_.id)" }
  if ($teams.Count -ne 1) { Write-Host "Several workspaces found. Re-run with -TeamId <id>."; exit 1 }
  $TeamId = $teams[0].id
}
$body = @{ endpoint = $Endpoint; events = @("taskStatusUpdated") }
if ($ListId) { $body.list_id = $ListId }
$r = Invoke-RestMethod -Method Post -Uri "https://api.clickup.com/api/v2/team/$TeamId/webhook" -Headers $h `
     -ContentType "application/json" -Body ($body | ConvertTo-Json)
Write-Host "Webhook created. id = $($r.id)"
Write-Host "Put this in .env as ClickUp__WebhookSecret=$($r.webhook.secret)  then run: docker compose up -d"
