# syntax=docker/dockerfile:1
#
# Fushi.Host container image.
#
# Build from the repository root, not from build/:
#   docker build -f build/Dockerfile -t fushi:local .
#
# The context has to be the root because restore needs Directory.Build.props,
# Directory.Packages.props, global.json, and nuget.config, all of which live
# there and all of which sit above build/ in the tree.

# ── Stage 1: restore and publish ─────────────────────────────────────────────
# The floating 11.0-preview tag resolves to the SDK band pinned in global.json.
# It is floating rather than fixed so that a preview servicing update does not
# require editing this file; global.json is what actually enforces the band, and
# it will fail the build loudly if the image ever drifts out of it.
FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /source

# The repository-wide MSBuild configuration comes first and on its own layer.
# These files change rarely, so the expensive restore below stays cached until
# a dependency actually moves. Copying the whole tree first instead would
# invalidate restore on every source edit.
COPY global.json nuget.config ./
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props ./

# Project files next, still without any source. Restore only reads these, so
# this layer plus the one above is the complete restore input.
COPY src/Fushi.Core/Fushi.Core.csproj                     src/Fushi.Core/
COPY src/Fushi.Application/Fushi.Application.csproj       src/Fushi.Application/
COPY src/Fushi.Infrastructure/Fushi.Infrastructure.csproj src/Fushi.Infrastructure/
COPY src/Fushi.Interactions/Fushi.Interactions.csproj     src/Fushi.Interactions/
COPY src/Fushi.Gateway/Fushi.Gateway.csproj               src/Fushi.Gateway/
COPY src/Fushi.Host/Fushi.Host.csproj                     src/Fushi.Host/

# A cache mount keeps the NuGet package cache across builds without baking it
# into a layer, which would otherwise add hundreds of megabytes to the image.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/Fushi.Host/Fushi.Host.csproj

COPY src/ src/

# --no-restore because the layer above already did it, and repeating it here
# would defeat the caching this file is arranged around. Output goes to an
# explicit path rather than the repository's artifacts/ directory so the copy
# in the final stage does not depend on the UseArtifactsOutput layout.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/Fushi.Host/Fushi.Host.csproj \
        --configuration "$BUILD_CONFIGURATION" \
        --no-restore \
        --output /app/publish

# ── Stage 2: runtime ─────────────────────────────────────────────────────────
# runtime, not aspnet: Fushi is a gateway client driven by a WebSocket, not a
# web server. The Ubuntu-based default tag is chosen deliberately over the
# Alpine and distroless variants, for the globalization reason below.
FROM mcr.microsoft.com/dotnet/runtime:11.0-preview AS final

# ── Globalization: do not "optimise" this away ───────────────────────────────
# DOTNET_SYSTEM_GLOBALIZATION_INVARIANT is deliberately NOT set, and
# InvariantGlobalization stays false in Directory.Build.props.
#
# Cycle scheduling resolves IANA time zone identifiers — "Europe/Berlin" by
# default — through TimeZoneInfo.FindSystemTimeZoneById. That call needs both
# the ICU libraries and the tzdata database. Under invariant globalization every
# such lookup throws TimeZoneNotFoundException, and it throws at the moment the
# scheduler tries to open a cycle rather than at startup, so the failure looks
# like "the bot silently stopped running votes" rather than a crash on boot.
#
# libicu ships in the Ubuntu-based runtime image already. tzdata is installed
# explicitly rather than assumed, because the zone database is what turns
# "Europe/Berlin" into the correct UTC offset across a daylight saving change,
# and a stale or absent one shifts every winter cycle by an hour.
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends tzdata \
    && rm --recursive --force /var/lib/apt/lists/*

ENV DOTNET_ENVIRONMENT=Production
ENV DOTNET_NOLOGO=1
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./

# The .NET base images ship a non-root user (app) whose id is in APP_UID.
# Running as that user rather than root means a container escape lands on an
# account that owns nothing and can install nothing. Fushi opens no listening
# port below 1024, so nothing here needs privilege.
USER $APP_UID

# No HEALTHCHECK: the meaningful signal is whether the Discord gateway session
# is connected, which the host reports through its own health endpoints. See
# docs/operations.md for how to wire those into an orchestrator probe.

ENTRYPOINT ["dotnet", "Fushi.Host.dll"]
