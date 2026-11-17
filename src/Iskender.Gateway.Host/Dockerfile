# The data plane's image (container contract, Images; research R16). Build it with the repository root as the context:
#   docker build -f src/Iskender.Gateway.Host/Dockerfile --build-arg REVISION="$(git rev-parse HEAD)" .
# Every base image is pinned by digest, and packages restore from nuget.org against the committed lock files.

FROM mcr.microsoft.com/dotnet/sdk:10.0.404-noble@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
ARG REVISION=unknown
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Iskender.Core.Contracts/Iskender.Core.Contracts.csproj src/Iskender.Core.Contracts/packages.lock.json src/Iskender.Core.Contracts/
COPY src/Iskender.Domain/Iskender.Domain.csproj src/Iskender.Domain/packages.lock.json src/Iskender.Domain/
COPY src/Iskender.Hosting/Iskender.Hosting.csproj src/Iskender.Hosting/packages.lock.json src/Iskender.Hosting/
COPY src/Iskender.Infrastructure/Iskender.Infrastructure.csproj src/Iskender.Infrastructure/packages.lock.json src/Iskender.Infrastructure/
COPY src/Iskender.Infrastructure.Container/Iskender.Infrastructure.Container.csproj src/Iskender.Infrastructure.Container/packages.lock.json src/Iskender.Infrastructure.Container/
COPY src/Iskender.Infrastructure.PostgreSql/Iskender.Infrastructure.PostgreSql.csproj src/Iskender.Infrastructure.PostgreSql/packages.lock.json src/Iskender.Infrastructure.PostgreSql/
COPY src/Iskender.Infrastructure.Redis/Iskender.Infrastructure.Redis.csproj src/Iskender.Infrastructure.Redis/packages.lock.json src/Iskender.Infrastructure.Redis/
COPY src/Iskender.Policy.Sdk/Iskender.Policy.Sdk.csproj src/Iskender.Policy.Sdk/packages.lock.json src/Iskender.Policy.Sdk/
COPY src/Iskender.Providers.Abstractions/Iskender.Providers.Abstractions.csproj src/Iskender.Providers.Abstractions/packages.lock.json src/Iskender.Providers.Abstractions/
COPY src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj src/Iskender.Gateway.Host/packages.lock.json src/Iskender.Gateway.Host/
RUN dotnet restore src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj -c Release --no-restore -p:UseAppHost=false -p:SourceRevisionId=${REVISION} -o /app

# The chiseled runtime has no shell or package manager. The application is copied in owned by root, so the app user,
# 1654, cannot change it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled@sha256:7c625e44e5d588b4d7b2a913e94ca4bd5098ce47442da404829e4c7936a764e1 AS runtime
ARG REVISION=unknown
LABEL org.opencontainers.image.source="https://github.com/Stonefly-Labs/Iskender-Gateway" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.licenses="MIT"
COPY --from=build /app /app
USER 1654:1654
WORKDIR /app
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "/app/Iskender.Gateway.Host.dll"]
