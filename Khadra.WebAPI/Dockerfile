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

# ── Runtime ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# The runtime image ships a non-root `app` user; use it. A web server that can
# write to its own binaries is a web server that can be persuaded to.
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
