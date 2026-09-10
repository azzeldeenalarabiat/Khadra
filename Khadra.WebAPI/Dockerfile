# The Khadra API.
#
# BUILD FROM THE REPOSITORY ROOT, not from this directory:
#
#     docker build -f Khadra.WebAPI/Dockerfile -t khadra-webapi .
#
# The build needs global.json, Directory.Build.props and Directory.Packages.props,
# which live at the root; a context of this folder alone cannot see them.
#
# Nothing is configured here. Every setting arrives as an environment variable at
# run time — see docs/deployment.md — because a connection string or a signing key
# baked into a layer is readable by anyone who can pull the image.

# ── Build ─────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# The manifests first, and only the manifests. Restore is the slow step and it
# depends on nothing else, so this layer survives every change that does not touch
# a package reference. Copying the source first would re-download the world on
# each edit.
#
# Directory.Packages.props is not optional: this solution uses central package
# management, so versions live there and a restore without it fails.
#
# Neither is .editorconfig. TreatWarningsAsErrors is on, and the root .editorconfig
# is what silences CA1825 and CA1861 for EF-GENERATED migrations -- code nobody
# hand-writes and the analyzer cannot be satisfied in. Without it copied, Release
# fails on generated files that build perfectly well on a developer machine, which
# is a confusing way to discover it.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY Khadra.Domain/Khadra.Domain.csproj Khadra.Domain/
COPY Khadra.Application/Khadra.Application.csproj Khadra.Application/
COPY Khadra.Infrastructure/Khadra.Infrastructure.csproj Khadra.Infrastructure/
COPY Khadra.WebAPI/Khadra.WebAPI.csproj Khadra.WebAPI/

RUN dotnet restore Khadra.WebAPI/Khadra.WebAPI.csproj

COPY Khadra.Domain/ Khadra.Domain/
COPY Khadra.Application/ Khadra.Application/
COPY Khadra.Infrastructure/ Khadra.Infrastructure/
COPY Khadra.WebAPI/ Khadra.WebAPI/

RUN dotnet publish Khadra.WebAPI/Khadra.WebAPI.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# The migration bundle, shipped INSIDE the image beside the app it matches.
#
# A bundle rather than `dotnet ef database update` at deploy time, for three
# reasons: the runtime image has no SDK and no dotnet-ef, so the alternative is
# shipping a second much larger image; the bundle is built from the same source
# in the same step, so the schema and the code cannot drift apart; and it reads
# its connection string from the environment rather than the command line, where
# `docker inspect` and a process list would both show a database password.
#
# Framework-dependent, which is the default: it runs on the aspnet runtime already
# in the final stage, so it adds a few hundred kilobytes rather than a self-contained
# copy of .NET.
RUN dotnet tool install --global dotnet-ef --version 10.* \
 && /root/.dotnet/tools/dotnet-ef migrations bundle \
      --project Khadra.Infrastructure \
      --startup-project Khadra.WebAPI \
      --configuration Release \
      --no-build \
      --force \
      --output /app/efbundle

# ── Runtime ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# Documents:RootPath is "App_Data/documents", RELATIVE, so it resolves under this
# working directory — and /app is root:root 755 in the base image while the server
# runs as the unprivileged `app` user. So the very first document upload called
# Directory.CreateDirectory("/app/App_Data/documents/dealers/<id>"), was refused,
# and answered 500 "An unexpected error occurred".
#
# Nothing caught it earlier because nothing had ever reached it: uploading a file
# needs a dealer owner who has registered, verified, signed in and filled in the
# whole application form, and the first person to do that in production was the
# first person to try writing a file at all.
#
# Created here rather than left to the application, so the ONE writable path is
# declared in the image instead of appearing at runtime. Everything else under
# /app stays root-owned and read-only to the server: a web server that can write
# to its own binaries is a web server that can be persuaded to.
#
# NOT persistence. This directory lives and dies with the container, so on a
# platform with an ephemeral filesystem every licence scan and every customer's
# identity document is gone on the next deploy. Attach a disk and point
# Documents__RootPath at it before real applications arrive — pre-launch
# checklist, "Uploaded documents outlive the container".
RUN mkdir -p /app/App_Data/documents && chown -R app:app /app/App_Data

# The runtime image ships a non-root `app` user; use it.
USER app

# 8080 because Khadra.Bff already expects the API at http://khadra-webapi:8080/ —
# see the YARP destination in Khadra.Bff/appsettings.json. Changing this means
# changing that too.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# HTTPS is terminated by the reverse proxy on the host, so Kestrel speaks plain
# HTTP on the container network and never holds a certificate. The API reads the
# real client address from X-Forwarded-For, and trusts that header only from the
# addresses named in KnownProxies — which must therefore name the proxy. An empty
# KnownProxies does not mean "trust nobody", it means "trust anybody", and the
# rate limiter partitions on that value.

COPY --from=build /app .

ENTRYPOINT ["dotnet", "Khadra.WebAPI.dll"]
