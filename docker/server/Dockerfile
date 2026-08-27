# Isis REST server image (Isis.Server, Watson 7.1).
# Build context is the repository ROOT (so the whole src/ tree is available for project references).
#   docker build -f docker/server/Dockerfile -t jchristn77/isis-server:v0.1.0 .
# Or via the root helper: build-server.bat v0.1.0

# ---- build stage -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the source tree and publish only the REST server (restore pulls its project references).
COPY src/ ./src/
RUN dotnet publish src/Isis.Server/Isis.Server.csproj -c Release -o /app

# ---- runtime stage -----------------------------------------------------------
# Isis.Server is a console Exe hosting Watson; the runtime image (not aspnet) is sufficient.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# curl is required by the compose healthcheck (the runtime image ships without it).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# The settings file is bind-mounted at run time; the server also writes it back on load, so the
# mount must be writable (do NOT mount it :ro). Environment variables (ISIS_DB_*, ISIS_AUTH_*, ...)
# override the file after it loads.
ENV ISIS_SETTINGS_FILE=/app/config/isis.json

EXPOSE 8700
ENTRYPOINT ["dotnet", "Isis.Server.dll"]
