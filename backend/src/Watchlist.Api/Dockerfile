FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Watchlist.sln backend/Watchlist.sln
COPY backend/src/Watchlist.Domain/Watchlist.Domain.csproj backend/src/Watchlist.Domain/
COPY backend/src/Watchlist.Application/Watchlist.Application.csproj backend/src/Watchlist.Application/
COPY backend/src/Watchlist.Infrastructure/Watchlist.Infrastructure.csproj backend/src/Watchlist.Infrastructure/
COPY backend/src/Watchlist.Api/Watchlist.Api.csproj backend/src/Watchlist.Api/
RUN dotnet restore backend/src/Watchlist.Api/Watchlist.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/src/Watchlist.Api/Watchlist.Api.csproj --configuration Release --output /app/publish --no-restore

FROM runtime AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "Watchlist.Api.dll"]
