# syntax=docker/dockerfile:1
#
# Curatarr API — Docker image
# ---------------------------
# Build context is the REPO ROOT (so the Engine project reference resolves):
#   docker build -t curatarr-api -f Curatarr.Api/Dockerfile .
#
# The image is self-sufficient: on first start it creates the SQLite schema
# and seeds the Admin/Curator/Viewer roles (Program.cs SeedAsync), then
# writes curatarr.db to the content root. We point the content root at
# /app/data and expect the app-data named volume there (see
# docker-compose.yml) so the database persists across container rebuilds.
#
# Config is 100% environment-variable driven (there is no appsettings.json):
#   Tmdb__ApiKey         optional TMDB key (32 chars); without it scanning
#                        falls back to folder-name parsing, no posters
#   Frontend__BaseUrl    base URL used in invite/reset email links,
#                        e.g. http://localhost:8080
#   ASPNETCORE_URLS      default set below to 0.0.0.0 so nginx/other
#                        containers can reach Kestrel; override if needed

# ---- Stage 1: restore + publish ------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the .csproj files first so the restore layer can be cached
# independently of source changes (nuget restore is the slow step).
COPY Curatarr.Api/Curatarr.Api.csproj Curatarr.Api/
COPY Curatarr.Engine/Curatarr.Engine.csproj Curatarr.Engine/

# Restore the API project graph only (Api -> Engine). EngineDemo is not
# part of the runtime surface, so it is deliberately left out.
RUN dotnet restore Curatarr.Api/Curatarr.Api.csproj

# Copy the full source and publish
COPY Curatarr.Api/ Curatarr.Api/
COPY Curatarr.Engine/ Curatarr.Engine/

RUN dotnet publish Curatarr.Api/Curatarr.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---- Stage 2: runtime -----------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install wget and curl for health checks and diagnostics
RUN apt-get update && apt-get install -y --no-install-recommends wget curl \
    && rm -rf /var/lib/apt/lists/*

# Curatarr writes "curatarr.db" into ContentRootPath (Program.cs). Pointing
# the content root at /app/data keeps the DB on the mounted named volume
# (app-data) instead of inside the container's writable layer. Content root
# is safe to move: the API has no wwwroot/static content and no
# appsettings.json to discover.
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENV ASPNETCORE_CONTENTROOT=/app/data

# /app/data must be writable by the non-root app user. Docker initializes a
# new named volume from the image directory it mounts, so pre-creating +
# chown here makes a fresh `curatarr-data` volume app-writable automatically.
# $APP_UID is provided by the aspnet base image (1654).
RUN mkdir -p /app/data && chown $APP_UID /app/data

COPY --from=build --chown=$APP_UID /app/publish .

# Built-in non-root user; drop root before running the app
USER $APP_UID

EXPOSE 5000
ENTRYPOINT ["dotnet", "Curatarr.Api.dll"]