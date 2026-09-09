# The Khadra BFF, with the admin console built into it.
#
# BUILD FROM THE REPOSITORY ROOT, not from this directory:
#
#     docker build -f Khadra.Bff/Dockerfile -t khadra-bff .
#
# ONE image, not two. In production the BFF serves the built Angular dashboard
# from wwwroot and falls back to index.html for client-side routes
# (Khadra.Bff/Program.cs — MapFallbackToFile). The console is therefore not a
# separate service to deploy; it is compiled here and copied in below. In
# development it is the other way round: `ng serve` runs on 4200 and proxies to
# this, which is why the dashboard's proxy.conf.json exists.
#
# The BFF has NO project references — it is a YARP proxy plus a session and
# antiforgery layer — so its restore needs only its own manifest.

# ── The console ───────────────────────────────────────────────────────────────
FROM node:22-bookworm-slim AS dashboard
WORKDIR /dashboard

# `npm ci` rather than `npm install`: it installs exactly the lockfile, fails if
# package.json and the lock disagree, and does not silently move a version between
# a developer's machine and the server.
COPY Khadra.Dashboard/package.json Khadra.Dashboard/package-lock.json ./
RUN npm ci

COPY Khadra.Dashboard/ ./
RUN npm run build -- --configuration production

# Angular's application builder emits to dist/<project>/browser, not dist/<project>.
# Named here once so the copy below cannot drift from it.

# ── The BFF ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY Khadra.Bff/Khadra.Bff.csproj Khadra.Bff/
RUN dotnet restore Khadra.Bff/Khadra.Bff.csproj

COPY Khadra.Bff/ Khadra.Bff/
RUN dotnet publish Khadra.Bff/Khadra.Bff.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# ── Runtime ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

USER app

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app .
COPY --from=dashboard /dashboard/dist/khadra-dashboard/browser ./wwwroot

ENTRYPOINT ["dotnet", "Khadra.Bff.dll"]
