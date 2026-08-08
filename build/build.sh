#!/usr/bin/env bash
#
# build.sh — restore, build, and test the solution the way CI does.
#
# Usage: build/build.sh [-c <configuration>] [-f] [-k] [--no-test] [--no-restore]
# Run `build/build.sh --help` for the full option list.
#
# Tests run on Microsoft.Testing.Platform rather than VSTest, because global.json
# selects that runner. VSTest's `--filter` expression syntax is not available;
# narrow a run with --filter-class, --filter-method, --filter-trait, or
# --filter-query instead.

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
SOLUTION="${REPO_ROOT}/Fushi.slnx"
readonly SCRIPT_DIR REPO_ROOT SOLUTION

configuration="Release"
run_format=0
collect_coverage=0
run_tests=1
run_restore=1

die() {
  printf 'build.sh: %s\n' "$1" >&2
  exit 1
}

log() {
  printf '\n==> %s\n' "$1"
}

usage() {
  cat <<'EOF'
build.sh — restore, build, and test the Fushi solution.

Usage: build/build.sh [options]

Options:
  -c, --configuration <name>  Build configuration. Default: Release.
  -f, --format                Also verify formatting (dotnet format).
  -k, --coverage              Write test results and coverage to
                              artifacts/coverage.
      --no-test               Restore and build only.
      --no-restore            Assume packages are already restored.
  -h, --help                  Print this help and exit.

Environment:
  FUSHI_COVERAGE_ARGS  Extra arguments forwarded to the test run when
                       --coverage is given. The coverage collector's own
                       options live here rather than being hard-coded, so
                       upgrading the collector does not mean editing this
                       script.
EOF
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    -c | --configuration)
      [ "$#" -ge 2 ] || die "--configuration requires a value"
      configuration="$2"
      shift 2
      ;;
    -f | --format)
      run_format=1
      shift
      ;;
    -k | --coverage)
      collect_coverage=1
      shift
      ;;
    --no-test)
      run_tests=0
      shift
      ;;
    --no-restore)
      run_restore=0
      shift
      ;;
    -h | --help)
      usage
      exit 0
      ;;
    *)
      die "unknown option '$1' (try --help)"
      ;;
  esac
done

command -v dotnet >/dev/null 2>&1 ||
  die "the .NET SDK is not on PATH; install the version named in global.json"

cd -- "${REPO_ROOT}"

# Reported first because every later failure mode looks different depending on
# which SDK global.json resolved to, and this is the cheapest way to rule that
# out.
log "SDK $(dotnet --version)"

if [ "${run_restore}" -eq 1 ]; then
  log "Restoring"
  dotnet restore "${SOLUTION}"
fi

if [ "${run_format}" -eq 1 ]; then
  log "Verifying formatting"
  # --verify-no-changes reports instead of rewriting, which is what makes this
  # usable as a gate. Drop the flag to have it fix the files in place.
  dotnet format "${SOLUTION}" --verify-no-changes --no-restore
fi

log "Building ${configuration}"
dotnet build "${SOLUTION}" --configuration "${configuration}" --no-restore

if [ "${run_tests}" -eq 0 ]; then
  log "Done — tests skipped"
  exit 0
fi

test_args=(
  test "${SOLUTION}"
  --configuration "${configuration}"
  --no-restore
)

if [ "${collect_coverage}" -eq 1 ]; then
  results_dir="${REPO_ROOT}/artifacts/coverage"
  mkdir -p -- "${results_dir}"
  test_args+=(--results-directory "${results_dir}")

  # --results-directory is the only coverage-related switch that belongs to the
  # platform itself. Everything else is the collector's, and collector option
  # names change between versions, so they are supplied from the environment
  # rather than baked in here. CI sets this; see .github/workflows/ci.yml.
  if [ -n "${FUSHI_COVERAGE_ARGS:-}" ]; then
    # Deliberately word-split: the variable holds a list of arguments.
    # shellcheck disable=SC2206
    extra=(${FUSHI_COVERAGE_ARGS})
    test_args+=("${extra[@]}")
  fi

  log "Testing with coverage into ${results_dir}"
else
  log "Testing"
fi

dotnet "${test_args[@]}"

log "Done"
