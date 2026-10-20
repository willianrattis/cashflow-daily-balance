# Build context is the repository root (see docker-compose.yml), so global.json,
# Directory.Packages.props and NuGet.Config are visible to the restore step.
#
# Keep this tag's SDK version equal to `sdk.version` in global.json and change both
# Dockerfiles and global.json together (ADR-009). global.json's `rollForward: latestFeature`
# lets `dotnet` use any installed 10.0 SDK at or above that version — a later patch or a
# higher feature band (e.g. 10.0.5xx) — but never an older one or another major/minor.
FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /src

COPY global.json Directory.Packages.props NuGet.Config ./
COPY src/LaunchService/LaunchService.csproj src/LaunchService/
RUN dotnet restore src/LaunchService/LaunchService.csproj

COPY src/LaunchService/ src/LaunchService/
RUN dotnet publish src/LaunchService/LaunchService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Non-root: $APP_UID is defined by the base aspnet image (not us).
USER $APP_UID

HEALTHCHECK --interval=10s --timeout=5s --retries=5 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "LaunchService.dll"]
