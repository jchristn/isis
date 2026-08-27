#!/bin/sh
# Isis factory/demo seeder. Runs once from the isis-factory-seed container (curlimages/curl).
# Best-effort: every call is guarded so a route that is not yet implemented is skipped, never fatal.
#
# The base stack already seeds the default administrator + tenant on first boot (Isis DefaultSeeder).
# This script layers illustrative memory content on top by creating a demo tenant and printing guidance
# for applying the full demo seed pack (demo-seedpack.json) from the dashboard.
#
# Env (from compose.factory.yaml):
#   ISIS_REST_BASE   e.g. http://isis-server:8700
#   ISIS_ADMIN_KEY   platform admin key, sent as x-api-key

set -u

BASE="${ISIS_REST_BASE:-http://isis-server:8700}"
ADMIN="${ISIS_ADMIN_KEY:-isisadmin}"

echo "[seed] Isis factory seeder starting against ${BASE}"

# 1) Confirm the server is reachable (compose already gated us on the healthcheck, this is belt-and-suspenders).
if curl -fsS "${BASE}/v1.0/api/health" >/dev/null 2>&1; then
  echo "[seed] REST server is healthy"
else
  echo "[seed] WARN: health endpoint not reachable; continuing best-effort"
fi

# 2) Create a demo tenant (best-effort). The response id is not parsed here (the curl image has no jq);
#    the demo scope/categories/policies are applied interactively via the dashboard's "Apply seed pack".
echo "[seed] creating demo tenant (best-effort)"
curl -fsS -X POST "${BASE}/v1.0/api/tenants" \
  -H "x-api-key: ${ADMIN}" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Demo"}' >/dev/null 2>&1 \
  && echo "[seed] demo tenant created" \
  || echo "[seed] demo tenant not created (route unavailable or already exists) — skipping"

# 3) Point the operator at the demo seed pack for the rest.
echo "[seed] ------------------------------------------------------------------"
echo "[seed] Demo seed pack available at docker/factory/seed/demo-seedpack.json"
echo "[seed] Apply it from the dashboard (Govern > Seed Packs > Apply) or via:"
echo "[seed]   POST ${BASE}/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/seed"
echo "[seed] Default admin key:  ${ADMIN}   (x-api-key header)"
echo "[seed] ------------------------------------------------------------------"
echo "[seed] done"
exit 0
