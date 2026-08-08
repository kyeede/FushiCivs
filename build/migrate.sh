#!/usr/bin/env bash
#
# migrate.sh — EF Core migration wrapper for Fushi.
#
# Usage: build/migrate.sh <command> [arguments]
# Run `build/migrate.sh help` for the command list.
#
# Every `dotnet ef` invocation needs two projects: the one holding the DbContext
# and migrations (src/Fushi.Infrastructure) and the one that knows how to build
# a host so the tool can construct that context (src/Fushi.Host). Getting either
# wrong produces an error about the design-time factory rather than about the
# argument that was actually wrong, which is the entire reason this wrapper
# exists.
#
# The connection string comes from ConnectionStrings__Database in the
# environment. `dotnet ef` builds the host, so the host's own configuration
# chain applies and no connection string ever has to appear on a command line
# (where it would land in shell history).

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
MIGRATIONS_PROJECT="${REPO_ROOT}/src/Fushi.Infrastructure/Fushi.Infrastructure.csproj"
STARTUP_PROJECT="${REPO_ROOT}/src/Fushi.Host/Fushi.Host.csproj"
readonly SCRIPT_DIR REPO_ROOT MIGRATIONS_PROJECT STARTUP_PROJECT

die() {
  printf 'migrate.sh: %s\n' "$1" >&2
  exit 1
}

log() {
  printf '\n==> %s\n' "$1"
}

usage() {
  cat <<'EOF'
migrate.sh — EF Core migration wrapper for Fushi.

Usage: build/migrate.sh <command> [arguments]

Commands:
  add <Name>        Scaffold a new migration into
                    src/Fushi.Infrastructure/Migrations.
  update [Target]   Apply migrations to the database. With no target, applies
                    everything pending. Pass a migration name to move to that
                    point, or 0 to unapply everything.
  list              List migrations and mark which are applied.
  remove            Delete the most recent migration, provided it has not been
                    applied anywhere.
  script [From] [To]
                    Emit idempotent SQL instead of touching the database. This
                    is what to hand to a DBA for a production change.
  help              Print this help.

Environment:
  ConnectionStrings__Database  Npgsql connection string. Required for `update`,
                               `list`, and `remove`; `add` and `script` work
                               without a reachable database.
  DOTNET_ENVIRONMENT           Selects appsettings.{Environment}.json.
                               Defaults to Development here, so running this
                               script never points at production by accident.

Examples:
  build/migrate.sh add AddSubmissionCodeIndex
  build/migrate.sh update
  build/migrate.sh script > artifacts/migration.sql
EOF
}

require_connection_string() {
  if [ -n "${ConnectionStrings__Database:-}" ]; then
    return 0
  fi

  cat >&2 <<'EOF'
migrate.sh: ConnectionStrings__Database is not set.

Copy .env.example to .env, fill it in, and export it into this shell:

    set -a && . ./.env && set +a
EOF
  exit 1
}

# `dotnet ef` is a tool rather than part of the SDK, so its absence is the most
# common first failure and deserves an actionable message.
require_ef_tool() {
  if dotnet ef --version >/dev/null 2>&1; then
    return 0
  fi

  cat >&2 <<'EOF'
migrate.sh: the dotnet-ef tool is not available.

Install it with:

    dotnet tool install --global dotnet-ef --prerelease

--prerelease is required because the tool's version must match the EF Core
version in Directory.Packages.props, which is a preview build.
EOF
  exit 1
}

# Shared arguments for every subcommand. Verbose output is off by default;
# add --verbose to the command line to have it forwarded.
ef() {
  dotnet ef "$@" \
    --project "${MIGRATIONS_PROJECT}" \
    --startup-project "${STARTUP_PROJECT}"
}

: "${DOTNET_ENVIRONMENT:=Development}"
export DOTNET_ENVIRONMENT

[ "$#" -ge 1 ] || {
  usage
  exit 64
}

command="$1"
shift

cd -- "${REPO_ROOT}"

case "${command}" in
  add)
    [ "$#" -ge 1 ] || die "add requires a migration name"
    name="$1"
    shift
    require_ef_tool
    log "Adding migration ${name}"
    # The output directory is given explicitly so migrations land in one place
    # regardless of which directory the script was called from.
    ef migrations add "${name}" --output-dir Migrations "$@"
    printf '\nReview the generated migration before committing it. EF infers\n'
    printf 'intent from a model diff and cannot know that a rename is a rename\n'
    printf 'rather than a drop followed by an add.\n'
    ;;

  update)
    require_ef_tool
    require_connection_string
    if [ "$#" -ge 1 ] && [ "${1#-}" = "$1" ]; then
      target="$1"
      shift
      log "Updating database to ${target}"
      ef database update "${target}" "$@"
    else
      log "Applying all pending migrations"
      ef database update "$@"
    fi
    ;;

  list)
    require_ef_tool
    require_connection_string
    log "Migrations"
    ef migrations list "$@"
    ;;

  remove)
    require_ef_tool
    log "Removing the most recent migration"
    ef migrations remove "$@"
    ;;

  script)
    require_ef_tool
    # --idempotent guards every step with a check against the history table, so
    # the same script is safe to run against a database at any revision. It
    # deliberately does not need a live connection.
    log "Generating idempotent SQL"
    if [ "$#" -ge 2 ]; then
      from="$1"
      to="$2"
      shift 2
      ef migrations script "${from}" "${to}" --idempotent "$@"
    else
      ef migrations script --idempotent "$@"
    fi
    ;;

  help | -h | --help)
    usage
    ;;

  *)
    die "unknown command '${command}' (try 'help')"
    ;;
esac
