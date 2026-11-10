#!/usr/bin/env bash
# Computes the minimum tier a PR needs from the files it changes.
# Fails if the PR's tier label is LOWER than required. Agents can raise a tier, never lower it.
# Usage: scripts/tier-check.sh <base-ref> <declared-tier 0|1|2>
set -euo pipefail

BASE="${1:-origin/main}"
DECLARED="${2:-0}"
CHANGED=$(git diff --name-only "$BASE"...HEAD)
DIFF=$(git diff "$BASE"...HEAD)
REQUIRED=0

bump() { if [ "$1" -gt "$REQUIRED" ]; then REQUIRED="$1"; echo "  -> tier $1: $2"; fi; }

while IFS= read -r f; do
  [ -z "$f" ] && continue
  case "$f" in
    .github/*|CLAUDE.md|.claude/settings.json|scripts/*|infra/*|Dockerfile|docker-compose*) bump 2 "$f (CI/infra/rules)";;
    */Features/Payments/*|src/Plugins/Salon.Plugin.Payments.*) bump 2 "$f (payments)";;
    */Features/Identity/*|*/Auth/*|*/Tenancy/*|*/Features/Billing/*) bump 2 "$f (auth/tenancy/billing)";;
    */Features/Clients/Consent*|*/Features/Clients/Erase*|*/Features/Clients/Export*) bump 2 "$f (GDPR)";;
    *.csproj|Directory.Packages.props) bump 2 "$f (dependencies)";;
    */Migrations/*) bump 1 "$f (migration)";;
    src/*.cs|src/*.cshtml|src/*.js) bump 1 "$f (code)";;
  esac
done <<< "$CHANGED"

# Destructive schema or filter bypass anywhere in the diff
if echo "$DIFF" | grep -E '^\+.*(DropColumn|DropTable|RenameColumn|AlterColumn|IgnoreQueryFilters|ExecuteSqlRaw)' >/dev/null; then
  bump 2 "destructive migration / filter bypass / raw SQL"
fi

LINES=$(git diff --shortstat "$BASE"...HEAD -- . ':(exclude)*/Migrations/*.Designer.cs' ':(exclude)*ModelSnapshot.cs' | grep -oE '[0-9]+ insertion|[0-9]+ deletion' | grep -oE '[0-9]+' | paste -sd+ | bc || echo 0)
if [ "${LINES:-0}" -gt 600 ]; then
  echo "PR has $LINES changed lines (> 600). Split it."; exit 1
fi

echo "Required tier: $REQUIRED, declared tier: $DECLARED"
if [ "$DECLARED" -lt "$REQUIRED" ]; then
  echo "Declared tier is lower than required. Raise the label to tier:$REQUIRED."; exit 1
fi
echo "required_tier=$REQUIRED" >> "${GITHUB_OUTPUT:-/dev/null}"
