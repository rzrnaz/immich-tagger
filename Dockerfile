FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PhotoAIApp.sln ./
COPY PhotoAIApp.Core/PhotoAIApp.Core.csproj PhotoAIApp.Core/
COPY PhotoAIApp.Server/PhotoAIApp.Server.csproj PhotoAIApp.Server/
RUN dotnet restore PhotoAIApp.Server/PhotoAIApp.Server.csproj

COPY . .
RUN dotnet publish PhotoAIApp.Server/PhotoAIApp.Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends gosu \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    IMMICH_TAGGER__PHOTO_ROOT=/photos \
    IMMICH_TAGGER__CONFIG_ROOT=/config \
    IMMICH_TAGGER__LOG_ROOT=/config/logs \
    PUID=99 \
    PGID=100 \
    UMASK=000

EXPOSE 8080
VOLUME ["/photos", "/config"]

COPY --from=build /app/publish .
COPY docker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "ImmichTagger.Server.dll"]
