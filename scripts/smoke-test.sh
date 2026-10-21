#!/usr/bin/env bash
# End-to-end smoke test. Usage: API_KEY=... ./scripts/smoke-test.sh [http://localhost:8080]
set -u
BASE="${1:-http://localhost:8080}"; KEY="${API_KEY:?set API_KEY (same as WebhookAuth__ApiKey in .env)}"
pass=0; fail=0
check() { if [ "$2" = "$3" ]; then echo "PASS  $1"; pass=$((pass+1)); else echo "FAIL  $1 (expected $3, got $2)"; fail=$((fail+1)); fi; }
post() { curl -s -o /tmp/resp.json -w "%{http_code}" -X POST "$BASE/api/v1/reviews" -H "Content-Type: application/json" "${@:2}" -d "$1"; }
ID="smoke-$(date +%s)"
BODY='{"eventId":"'$ID'","taskId":"86abc123","taskUrl":"https://app.clickup.com/t/86abc123","status":"Under Review","title":"Review payment retry handling","description":"Check retry behaviour and idempotency handling. password=hunter2","repositoryUrl":"https://github.com/acme/svc"}'

check "health/live"  "$(curl -s -o /dev/null -w '%{http_code}' $BASE/api/v1/health/live)" 200
check "health/ready" "$(curl -s -o /dev/null -w '%{http_code}' $BASE/api/v1/health/ready)" 200
check "no api key -> 401"   "$(post "$BODY")" 401
check "wrong api key -> 401" "$(post "$BODY" -H "X-Api-Key: nope")" 401
check "missing taskId -> 400" "$(post '{"eventId":"x","status":"Under Review"}' -H "X-Api-Key: $KEY")" 400
check "bad repo host -> 400"  "$(post '{"eventId":"x","taskId":"t","status":"Under Review","repositoryUrl":"https://evil.example.com/r"}' -H "X-Api-Key: $KEY")" 400
check "other status ignored -> 200" "$(post '{"eventId":"y","taskId":"t","status":"In Progress"}' -H "X-Api-Key: $KEY")" 200
check "valid request -> 202" "$(post "$BODY" -H "X-Api-Key: $KEY")" 202
JOB=$(sed -E 's/.*"jobId":"([^"]+)".*/\1/' /tmp/resp.json)
check "same eventId again -> 202 (duplicate)" "$(post "$BODY" -H "X-Api-Key: $KEY")" 202
grep -q '"duplicate":true' /tmp/resp.json && check "duplicate flag set" ok ok || check "duplicate flag set" missing ok

STATUS=""; for i in $(seq 1 30); do
  STATUS=$(curl -s "$BASE/api/v1/reviews/$JOB" | sed -E 's/.*"status":"([^"]+)".*/\1/'); [ "$STATUS" = "Completed" ] && break; sleep 1; done
check "job reaches Completed" "$STATUS" Completed
DETAIL=$(curl -s "$BASE/api/v1/reviews/$JOB")
echo "$DETAIL" | grep -q '"writeBackStatus":"Delivered"' && check "write-back Delivered" ok ok || check "write-back Delivered" no ok
echo "$DETAIL" | grep -q 'hunter2' && check "secret not stored/leaked" leaked ok || check "secret not stored/leaked" ok ok
check "list endpoint" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/reviews?pageSize=5")" 200
check "metrics endpoint" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/v1/metrics/summary")" 200
echo; echo "passed=$pass failed=$fail"; [ $fail -eq 0 ]
