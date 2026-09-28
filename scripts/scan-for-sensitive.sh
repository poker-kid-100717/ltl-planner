#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

blocked=(
  "value[ -]?truck"
  "vc-for-valuetruck"
  "freight[ -]?dna"
  "valuetruck\\.com"
  "yard-application-uat"
  "fd-uat"
)

status=0
for pattern in "${blocked[@]}"; do
  if grep -RInE --exclude-dir=.git --exclude='scan-for-sensitive.sh' "$pattern" .; then
    echo "Blocked employer-specific pattern found: $pattern" >&2
    status=1
  fi
done

# High-signal secret shapes. Public placeholder words are excluded by requiring long values.
if grep -RInE --exclude-dir=.git --exclude='scan-for-sensitive.sh' '(client_secret|api[_-]?key|password)["'\'' ]*[:=]["'\'' ]*[A-Za-z0-9_+\/-]{20,}' .; then
  echo "Potential embedded secret found." >&2
  status=1
fi

if [ "$status" -ne 0 ]; then
  exit "$status"
fi

echo "Sensitive-content scan passed."
