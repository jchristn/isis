# Isis MCP server image (Isis.McpServer, Voltaic 0.6.1 streamable HTTP).
# Build context is the repository ROOT (so the whole src/ tree is available for project references).
#   docker build -f docker/mcp/Dockerfile -t jchristn77/isis-mcp:v0.1.0 .
# Or via the root helper: build-mcp.bat v0.1.0

# ---- build stage -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/ ./src/
RUN dotnet publish src/Isis.McpServer/Isis.McpServer.csproj -c Release -o /app

# ---- runtime stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# curl is required by the compose healthcheck (the runtime image ships without it).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# The MCP server takes all of its docker configuration from environment variables
# (ISIS_MCP_HOSTNAME, ISIS_MCP_PORT, ISIS_MCP_REST_HOSTNAME, ISIS_MCP_REST_PORT); it writes a
# default isis.mcp.json into the working directory on first load, which is fine (it is writable).
EXPOSE 8720
ENTRYPOINT ["dotnet", "Isis.McpServer.dll"]
