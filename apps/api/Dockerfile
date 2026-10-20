# syntax=docker/dockerfile:1

# The Support Desk API. Build from the repository root (DK-1):
#   docker build -f apps/api/Dockerfile -t northwind-support-api .
# or let docker compose build it: docker compose up --build api

# ---- restore: only the files that decide which packages are needed --------------------------
# Copied before any source, so this layer (and the NuGet download) is reused until a project
# file, the central package versions or the SDK pin changes - not on every code edit.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY apps/api/src/SupportDesk.Domain/SupportDesk.Domain.csproj apps/api/src/SupportDesk.Domain/
COPY apps/api/src/SupportDesk.Application/SupportDesk.Application.csproj apps/api/src/SupportDesk.Application/
COPY apps/api/src/SupportDesk.Infrastructure/SupportDesk.Infrastructure.csproj apps/api/src/SupportDesk.Infrastructure/
COPY apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj apps/api/src/SupportDesk.Presentation/

RUN dotnet restore apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj

# ---- build and publish: the source, compiled in Release -------------------------------------
FROM restore AS publish

COPY apps/api/src/ apps/api/src/

RUN dotnet publish apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

# ---- runtime: the ASP.NET Core runtime only - no SDK, compiler or source ---------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=publish /app/publish .

# DK-3: plain HTTP on 8080 (TLS belongs to whatever sits in front of the container) and the
# image's built-in unprivileged "app" user, never root.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

# DK-4: no connection string, signing key or environment name is set here - all come from the
# environment at run time (see docker-compose.yml and .env.example).
ENTRYPOINT ["dotnet", "SupportDesk.Presentation.dll"]
