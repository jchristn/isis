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

# 2) Create a demo tenant (best-effort). POST /tenants now auto-provisions a tenant-admin user, a
#    credential, and a default instruction set; the admin password and credential secret key are
#    returned ONLY in this response, so we parse and print them for the operator. The curl image has
#    no jq, so fields are extracted with grep/sed. The demo scope/categories/policies are still
#    applied interactively via the dashboard's "Apply seed pack".
#
# Tenant creation is system-administrator only; authenticate with the default credential ACCESS KEY
# seeded on first boot (override with ISIS_AUTH_DEFAULT_ACCESS_KEY). The access key authenticates on its
# own; the secret key is never sent.
ACCESS_KEY="${ISIS_AUTH_DEFAULT_ACCESS_KEY:-isisdefaultkey}"

# extract "<key>":"<value>" from a flat-ish JSON blob (first match wins).
json_field() {
  printf '%s' "$1" | grep -o "\"$2\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" | head -n1 \
    | sed 's/.*:[[:space:]]*"//; s/"$//'
}

echo "[seed] creating demo tenant (best-effort)"
RESP="$(curl -sS -X POST "${BASE}/v1.0/api/tenants" \
  -H "x-access-key: ${ACCESS_KEY}" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Demo"}' 2>/dev/null)"

DEMO_ADMIN_EMAIL="$(json_field "${RESP}" email)"
DEMO_ADMIN_PASSWORD="$(json_field "${RESP}" password)"
DEMO_ACCESS_KEY="$(json_field "${RESP}" accessKey)"
DEMO_SECRET_KEY="$(json_field "${RESP}" secretKey)"

if [ -n "${DEMO_ADMIN_EMAIL}" ] && [ -n "${DEMO_SECRET_KEY}" ]; then
  echo "[seed] demo tenant created and provisioned"
  echo "[seed] ------------------------------------------------------------------"
  echo "[seed] Demo tenant admin + credential (shown ONCE — copy them now):"
  echo "[seed]   admin email:    ${DEMO_ADMIN_EMAIL}"
  echo "[seed]   admin password: ${DEMO_ADMIN_PASSWORD}"
  echo "[seed]   access key:     ${DEMO_ACCESS_KEY}"
  echo "[seed]   secret key:     ${DEMO_SECRET_KEY}"
  echo "[seed] ------------------------------------------------------------------"
else
  echo "[seed] demo tenant not created (route unavailable or already exists) — skipping"
fi

# 3) Point the operator at the demo seed pack for the rest.
echo "[seed] ------------------------------------------------------------------"
echo "[seed] Demo seed pack available at docker/factory/seed/demo-seedpack.json"
echo "[seed] Apply it from the dashboard (Govern > Seed Packs > Apply) or via:"
echo "[seed]   POST ${BASE}/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/seed"
echo "[seed] Default admin key:  ${ADMIN}   (x-api-key header)"
echo "[seed] ------------------------------------------------------------------"
echo "[seed] done"
exit 0
