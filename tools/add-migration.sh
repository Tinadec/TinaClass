#!/bin/bash
# EF Core migrations for a template whose API keeps one DbContext per feature and one derived context per database provider
# (src/<Name>.Api/Features/<Feature>/Data/{Sqlite,Postgres,SqlServer}): runs the same `dotnet ef` command for every provider folder
# that is present, so that no provider is forgotten, and fails loudly when any of them fails.
#
#   add-migration.sh [--template <dir>] <Feature> <MigrationName>   add the migration to every provider of the feature
#   add-migration.sh [--template <dir>] --check [<Feature>]         drift check, writes nothing: `dotnet ef migrations
#                                                                   has-pending-model-changes` for every provider of every feature
#                                                                   (or of one); exits non-zero when a model changed without a migration
#
# <dir> is the template folder (the one with the .slnx and src/). Without --template the script finds it from the current folder
# upwards, so it also works from inside a template. Needs the .NET SDK and the dotnet-ef local tool pinned in .config/dotnet-tools.json
# (restored here). No database is opened: the design-time factories of the contexts point at placeholders.
#
# If any provider fails while adding, the migration files of the providers that succeeded are rolled back, so the three sets never
# disagree.
set -u

usage() { sed -n '2,/^set -u/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; exit "${1:-2}"; }

TEMPLATE=""; CHECK=0; FEATURE=""; NAME=""
while [ $# -gt 0 ]; do
  case "$1" in
    --template) TEMPLATE=${2:?--template needs a folder}; shift 2 ;;
    --check) CHECK=1; shift ;;
    -h|--help) usage 0 ;;
    -*) echo "unknown option: $1" >&2; usage ;;
    *) if [ -z "$FEATURE" ]; then FEATURE=$1; elif [ -z "$NAME" ]; then NAME=$1; else echo "too many arguments" >&2; usage; fi; shift ;;
  esac
done
if [ $CHECK = 0 ] && { [ -z "$FEATURE" ] || [ -z "$NAME" ]; }; then usage; fi
if [ $CHECK = 1 ] && [ -n "$NAME" ]; then echo "--check takes at most a feature" >&2; usage; fi
if [ -n "$NAME" ] && ! [[ "$NAME" =~ ^[A-Za-z][A-Za-z0-9_]*$ ]]; then echo "The migration name must be letters, digits and _ (PascalCase): $NAME" >&2; exit 2; fi

# The template folder: --template, else the nearest folder upwards that has src/<Name>.Api/<Name>.Api.csproj.
find_root() {
  local dir=$1
  while [ "$dir" != "/" ]; do
    if compgen -G "$dir/src/*.Api/*.Api.csproj" >/dev/null; then echo "$dir"; return 0; fi
    dir=$(dirname "$dir")
  done
  return 1
}
if [ -n "$TEMPLATE" ]; then
  ROOT=$(cd "$TEMPLATE" 2>/dev/null && pwd) || { echo "No such folder: $TEMPLATE" >&2; exit 2; }
else
  ROOT=$(find_root "$PWD") || { echo "No src/<Name>.Api project found from $PWD upwards. Run this inside a template or pass --template <dir>." >&2; exit 2; }
fi
PROJECT=$(ls "$ROOT"/src/*.Api/*.Api.csproj 2>/dev/null | head -1)
[ -n "$PROJECT" ] || { echo "$ROOT has no src/<Name>.Api/<Name>.Api.csproj (was the template made without the API?)." >&2; exit 2; }
PROJECT_DIR=$(dirname "$PROJECT")
cd "$ROOT" || exit 2

# Features with a Data folder, and the provider folders each has.
features=()
if [ -n "$FEATURE" ]; then
  [ -d "$PROJECT_DIR/Features/$FEATURE/Data" ] || { echo "No feature $FEATURE (looked for ${PROJECT_DIR#$ROOT/}/Features/$FEATURE/Data)." >&2; exit 2; }
  features+=("$FEATURE")
else
  for d in "$PROJECT_DIR"/Features/*/Data; do [ -d "$d" ] && features+=("$(basename "$(dirname "$d")")"); done
fi
[ ${#features[@]} -gt 0 ] || { echo "The API has no feature with a Data folder." >&2; exit 2; }

jobs=()   # "Feature|Provider"
for f in "${features[@]}"; do
  found=0
  for p in Sqlite Postgres SqlServer; do
    if [ -d "$PROJECT_DIR/Features/$f/Data/$p" ]; then jobs+=("$f|$p"); found=1; fi
  done
  [ $found = 1 ] || { echo "Feature $f has no provider folder (Data/Sqlite, Data/Postgres, Data/SqlServer)." >&2; exit 2; }
done

echo "template: $ROOT"
echo "providers per feature:"
for f in "${features[@]}"; do
  list=""; for j in "${jobs[@]}"; do [ "${j%%|*}" = "$f" ] && list="$list ${j##*|}"; done
  echo "  $f:$list"
done

dotnet tool restore >/dev/null || { echo "dotnet tool restore failed (the dotnet-ef tool is pinned in .config/dotnet-tools.json)." >&2; exit 1; }
echo "building ${PROJECT#$ROOT/} ..."
dotnet build "$PROJECT" -v q --nologo >/dev/null || { echo "The API does not build: fix that first (dotnet build ${PROJECT#$ROOT/})." >&2; exit 1; }

EF=(dotnet ef)
failed=()

if [ $CHECK = 1 ]; then
  for j in "${jobs[@]}"; do
    f=${j%%|*}; p=${j##*|}; ctx="$p${f}DbContext"
    if out=$("${EF[@]}" migrations has-pending-model-changes --project "$PROJECT" --context "$ctx" --no-build 2>&1); then
      echo "ok       $ctx"
    else
      echo "DRIFT    $ctx"
      echo "$out" | grep -v '^Build ' | sed 's/^/         /'
      failed+=("$ctx")
    fi
  done
  if [ ${#failed[@]} -gt 0 ]; then
    echo
    echo "FAILED: the model of ${failed[*]} changed since its last migration (or the command failed). Add one with:"
    echo "  tools/add-migration.sh <Feature> <MigrationName>"
    exit 1
  fi
  echo "no drift: every provider's migrations match its model."
  exit 0
fi

# Add: back up every Migrations folder first, so that a failure leaves nothing half done.
BACKUP=$(mktemp -d)
trap 'rm -rf "$BACKUP"' EXIT
for j in "${jobs[@]}"; do
  f=${j%%|*}; p=${j##*|}; dir="$PROJECT_DIR/Features/$f/Data/$p/Migrations"
  mkdir -p "$BACKUP/$f-$p"
  if [ -d "$dir" ]; then cp -R "$dir/." "$BACKUP/$f-$p/"; echo "existed" > "$BACKUP/$f-$p.state"; else echo "absent" > "$BACKUP/$f-$p.state"; fi
done

done_ok=()
for j in "${jobs[@]}"; do
  f=${j%%|*}; p=${j##*|}; ctx="$p${f}DbContext"; out_dir="${PROJECT_DIR#$ROOT/}/Features/$f/Data/$p/Migrations"
  # --output-dir is relative to the project; the namespace follows the folder.
  if out=$("${EF[@]}" migrations add "$NAME" --project "$PROJECT" --context "$ctx" --output-dir "Features/$f/Data/$p/Migrations" --no-build 2>&1); then
    echo "added    $ctx -> $out_dir"
    done_ok+=("$j")
  else
    echo "FAILED   $ctx"
    echo "$out" | sed 's/^/         /'
    failed+=("$ctx")
    break
  fi
done

if [ ${#failed[@]} -gt 0 ]; then
  echo
  echo "FAILED: ${failed[*]}. Rolling back the providers that had succeeded so the sets stay in step."
  for j in "${jobs[@]}"; do
    f=${j%%|*}; p=${j##*|}; dir="$PROJECT_DIR/Features/$f/Data/$p/Migrations"
    rm -rf "$dir"
    if [ "$(cat "$BACKUP/$f-$p.state")" = existed ]; then mkdir -p "$dir"; cp -R "$BACKUP/$f-$p/." "$dir/"; fi
  done
  exit 1
fi
echo "done: migration $NAME added for ${#done_ok[@]} context(s). Review the generated files, then run  tools/add-migration.sh --check"
exit 0
