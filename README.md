<!-- # AI Review Pipeline (ClickUp → Power Automate → .NET API → Worker → LLM → ClickUp + React dashboard)

```
ai-review-pipeline/
├── docker-compose.yml          # frontend + api + worker + postgres
├── .env.example                # copy to .env
├── scripts/
│   ├── smoke-test.ps1          # end-to-end test (Windows PowerShell)
│   └── smoke-test.sh           # end-to-end test (bash / Git Bash / WSL)
├── backend/
│   ├── Automation.sln
│   ├── src/
│   │   ├── Automation.Core/            # entities, contracts, validation, redaction, prompt, ReviewProcessor
│   │   ├── Automation.Infrastructure/  # EF Core + PostgreSQL, Anthropic + Mock LLM, ClickUp client, DI
│   │   ├── Automation.Api/             # minimal API: /api/v1/...   (Program.cs, Dockerfile)
│   │   └── Automation.Worker/          # background worker loops    (ReviewWorker.cs, Dockerfile)
│   └── tests/Automation.UnitTests/     # xUnit tests (CoreTests.cs)
└── frontend/                   # React + TypeScript (Vite) dashboard
    └── src/ (App.tsx, api.ts, styles.css, main.tsx)
```

## 1. Prerequisites
Docker Desktop, .NET 8 SDK, Node 20+ (Node/.NET only needed for dev mode or tests) Groq AI Test.

## 2. Run everything with Docker (recommended)
```bash
cp .env.example .env          # Windows PowerShell: Copy-Item .env.example .env
# edit .env: set DB_PASSWORD and WebhookAuth__ApiKey to your own values
docker compose up --build
```
- Dashboard: http://localhost:3000
- API: http://localhost:8080/api/v1/health/ready
- Stop: `docker compose down`  (add `-v` ONLY to delete the database volume)
- Logs: `docker compose logs -f api worker`

Default settings use the **Mock** LLM and ClickUp **DryRun**, so everything works with no API keys and no cost.

## 3. Run in dev mode (hot reload)
```bash
docker compose up db -d                       # only PostgreSQL (exposed on 127.0.0.1:5432)
```
PowerShell (one terminal for API, another for worker; same variables in both):
```powershell
$env:ConnectionStrings__Default="Host=localhost;Database=automation;Username=app;Password=<DB_PASSWORD>"
$env:WebhookAuth__ApiKey="<same as .env>"
cd backend
dotnet run --project src/Automation.Api        # start this FIRST (it creates the tables)
dotnet run --project src/Automation.Worker     # second terminal
```
bash: use `export NAME=value` instead of `$env:NAME=`.
Frontend: `cd frontend && npm install && npm run dev` → http://localhost:5173 (proxies /api to :8080).
The API listens on the port printed at startup; if it is not 8080 set `$env:ASPNETCORE_URLS="http://localhost:8080"`.

## 4. How to test (in this order)
**a) Unit tests** (no Docker needed)
```bash
cd backend && dotnet test
```
Covers: secret redaction, request validation (bad hosts, missing ids), JSON parsing/schema validation,
comment formatting, processor behaviour (happy path, ClickUp failing 3x keeps the analysis, retry on provider
timeout, malformed model output rejected, replay skips the LLM, secrets never reach the LLM).

**b) End-to-end smoke test** (stack must be running)
```powershell
.\scripts\smoke-test.ps1 -ApiKey "<WebhookAuth__ApiKey from .env>"
```
```bash
API_KEY="<key>" ./scripts/smoke-test.sh
```
Checks: health, 401 without/with wrong key, 400 invalid payload, 400 unapproved repo host, ignored status,
202 + jobId, duplicate event → no second job, job reaches Completed, write-back Delivered, secret not leaked, list + metrics.

**c) Manual scenarios** (replace KEY)
```bash
curl -X POST http://localhost:8080/api/v1/reviews -H "Content-Type: application/json" -H "X-Api-Key: KEY" \
  -d '{"eventId":"demo-1","taskId":"86abc123","taskUrl":"https://app.clickup.com/t/86abc123","status":"Under Review","title":"Review payment retry","description":"Check retry and idempotency handling"}'
```
Then open the dashboard (refreshes every 10 s) and click the row.

| Test | What to send | Expected |
|---|---|---|
| Provider timeout | description contains `FORCE_TIMEOUT` | job re-queued, retried up to 3 times, then **Failed** (visible on dashboard) |
| Malformed model output | description contains `FORCE_BAD_JSON` | nothing saved, retried, then **Failed** |
| Replay a failed job | `POST /api/v1/reviews/{jobId}/retry` with `X-Api-Key` | job goes Queued → processed again |
| Duplicate delivery | send the same `eventId` twice | second response has `"duplicate":true`, no second job |
| Worker crash | `docker compose stop worker`, send a job, `docker compose start worker` | job stays Queued, then is processed |
| Check data | `docker compose exec db psql -U app -d automation -c 'select "Status","WriteBackStatus","Attempts" from "ReviewJobs";'` | matches dashboard |

## 5. Switch to the real LLM and real ClickUp
In `.env`:
```
Llm__Provider=Groq            # or Anthropic
Llm__ApiKey=<your key>
Llm__Model=                   # optional override
ClickUp__ApiToken=<ClickUp personal API token>
ClickUp__DryRun=false
```
Then `docker compose up --build -d`. Test with a ClickUp task ID that really exists, otherwise the comment POST fails
(you will see Write-back: Failed on the dashboard, and `POST /reviews/{id}/retry` replays it after you fix the token).
Only the task text is reviewed; linked repo/PR content is NOT fetched (the model is told this and lists it under limitations).


## 5b. FREE setup: Groq (LLM) + native ClickUp webhook (no Power Automate, no local installs)
Only Docker Desktop is needed. All .NET/Node packages are installed automatically *inside* the Docker build.

**1. Groq key (free, no credit card):** https://console.groq.com/keys -> Create API Key (starts with `gsk_`). In `.env`:
```
Llm__Provider=Groq
Llm__ApiKey=gsk_xxxxxxxx
Llm__Model=                 # empty = llama-3.3-70b-versatile; see console.groq.com/docs/models if it is renamed
Review__MaxConcurrentJobs=2
```
Free tier has per-minute/day limits; a 429 is retried automatically (backoff, up to 3 attempts).

**2. Test the flow without ClickUp first:** keep `Llm__Provider=Mock`, run the smoke test, then switch to Groq and send one review.

**3. Free public URL for ClickUp to reach your PC** (ClickUp only calls public HTTPS URLs):
```powershell
docker compose up -d
docker compose --profile tunnel up tunnel      # prints https://<random>.trycloudflare.com  (Ctrl+C stops it)
```
The URL changes every time the tunnel restarts (re-register the webhook then). Alternatives: ngrok free plan, VS Code Dev Tunnels.

**4. Register the ClickUp webhook (free API feature):**
```powershell
.\scripts\register-clickup-webhook.ps1 -Token "pk_..." -Endpoint "https://<random>.trycloudflare.com/api/v1/webhooks/clickup"
```
Copy the printed secret into `.env` as `ClickUp__WebhookSecret=...`, set `ClickUp__ApiToken=pk_...` and `ClickUp__DryRun=false`, then `docker compose up -d`.

**5. Test:** move a task to the status named in `Review__TargetStatus` (default `Under Review`) -> a job appears on the dashboard -> a comment is posted on the task.
Without ClickUp: `.\scripts\test-clickup-webhook.ps1 -Secret "<ClickUp__WebhookSecret>"` sends a correctly signed fake event
(and a wrong-signature one that must return 401).

Notes: the webhook reads task name/description from ClickUp and reviews that text only. The generic `POST /api/v1/reviews`
(Power Automate or any HTTP client) still works too. Webhook testing helper: https://smee.io or https://webhook.site can show
what ClickUp sends, but they cannot forward to your API signed, use the tunnel above.

## 6. Power Automate flow (optional, only if you prefer it)
1. Trigger: ClickUp task/status change (connector, webhook, or scheduled poll, whichever your tenant supports).
2. Condition: status equals `Under Review` and list is enabled. Ignore changes made by the automation account (prevents loops).
3. Action **HTTP**: Method POST, URI `https://<public-host>/api/v1/reviews`, Headers `Content-Type: application/json` and `X-Api-Key: <WebhookAuth__ApiKey>`,
   Body (map the dynamic content from your trigger):
```json
{ "eventId": "<unique id of the update>", "taskId": "<task id>", "taskUrl": "<task url>", "status": "<new status>",
  "title": "<task name>", "description": "<task description>",
  "repositoryUrl": "<optional, approved host only>", "pullRequestUrl": "<optional>", "triggeredAtUtc": "<utcNow()>" }
```
4. Retry policy on the HTTP action: exponential, 3 retries. On repeated failure send an alert.
5. Power Automate cannot reach `localhost`. For local testing expose the API with a dev tunnel or ngrok
   (`devtunnel host -p 8080 --allow-anonymous` or `ngrok http 8080`) and use that HTTPS URL.
6. Do NOT post the final comment from Power Automate; the worker does it.

## 7. Before production (known gaps in v1)
- Tables are created with `EnsureCreated`; switch to EF Core migrations.
- The webhook uses a shared API key; use OAuth client-credentials or signed short-lived tokens. Dashboard GET endpoints are
  unauthenticated in this version; put them behind your SSO / ALB auth.
- The queue is database-backed (`FOR UPDATE SKIP LOCKED`); move to SQS + dead-letter queue when volume grows.
- No worker heartbeat yet (dashboard shows queue depth/age and API readiness instead).
- Containers: run as non-root, pin image digests, scan images; AWS ECS/Fargate layout is described in section 11 of the design document.

## 8. Troubleshooting
- `401` from POST: header must be `X-Api-Key` and match `WebhookAuth__ApiKey` exactly.
- API container unhealthy: `docker compose logs api` (usually wrong `DB_PASSWORD` after changing .env: run `docker compose down -v` once).
- Jobs stay Queued: worker not running or crashed: `docker compose logs worker`.
- `Llm__Provider=Anthropic requires Llm__ApiKey`: the API/worker refuse to start without the key (by design). -->
# AI Review Pipeline (ClickUp / GitHub → .NET API → Worker → LLM → ClickUp / GitHub PR + React Dashboard)

Yeh ek automated AI code review system hai. Jab bhi ClickUp mein koi task "Under Review" hota hai ya GitHub par koi naya Pull Request (PR) aata hai, yeh system automatically us code ko LLM (Groq/Anthropic) ke paas bhejta hai, bugs aur security issues dhoondta hai, aur seedha ClickUp task ya GitHub PR par comment post karta hai.
# AI Review Pipeline (ClickUp / GitHub → .NET API → Worker → LLM → ClickUp / GitHub PR + React Dashboard)

Yeh ek automated AI code review system hai. Jab bhi ClickUp mein koi task "Under Review" hota hai ya GitHub par koi naya Pull Request (PR) aata hai, yeh system automatically us code ko LLM (Groq/Anthropic) ke paas bhejta hai, bugs aur security issues dhoondta hai, aur seedha ClickUp task ya GitHub PR par comment post karta hai.

```text
enterprise-ai-automation/
├── docker-compose.yml          # frontend + api + worker + postgres + ngrok
├── .env.example                # copy to .env
├── scripts/
│   ├── smoke-test.ps1          # end-to-end test (Windows PowerShell)
│   └── smoke-test.sh           # end-to-end test (bash / Git Bash / WSL)
├── backend/
│   ├── Automation.sln
│   ├── src/
│   │   ├── Automation.Core/            # entities, contracts, validation, prompt, ReviewProcessor
│   │   ├── Automation.Infrastructure/  # EF Core + PostgreSQL, Groq/Anthropic LLM, ClickUp + GitHub clients
│   │   ├── Automation.Api/             # minimal API: /api/v1/... (Program.cs, Dockerfile)
│   │   └── Automation.Worker/          # background worker loops
│   └── tests/Automation.UnitTests/
└── frontend/                   # React + TypeScript (Vite) dashboard
