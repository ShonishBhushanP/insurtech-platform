#!/usr/bin/env bash
# Set the minimum replica count on every InsurTech Container App.
#   Demo / always-on (no cold starts):   bash scale-apps.sh <rg> 1
#   After the demo / scale-to-zero:        bash scale-apps.sh <rg> 0
#
# min=1 keeps one replica running 24/7 (standing cost, but instant responses);
# min=0 lets each app scale to zero when idle (near-zero cost, ~20-30s cold start).
set -euo pipefail

RG="${1:-rg-azuser7069_mml.local-yyRMB}"
MIN="${2:-1}"
PREFIX="${3:-insurtech}"

APPS=(gateway policy claims fraud documents payments partner underwriting notification audit)

echo "Setting min-replicas=$MIN on ${#APPS[@]} apps in $RG ..."
for s in "${APPS[@]}"; do
  if az containerapp update -g "$RG" -n "${PREFIX}-${s}" --min-replicas "$MIN" --max-replicas 3 -o none 2>/dev/null; then
    echo "  ok   ${PREFIX}-${s}  (min=$MIN)"
  else
    echo "  SKIP ${PREFIX}-${s}  (not found?)"
  fi
done
echo "Done. (min=1 = always-on for the demo; re-run with 0 afterwards to save budget.)"
