# The API image. Build from the repository root:  docker build -f deploy/Dockerfile -t tinaclass-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/TinaClass.Contracts/TinaClass.Contracts.csproj src/TinaClass.Contracts/
COPY src/TinaClass.Api/TinaClass.Api.csproj src/TinaClass.Api/
RUN dotnet restore src/TinaClass.Api/TinaClass.Api.csproj
COPY src/TinaClass.Contracts/ src/TinaClass.Contracts/
COPY src/TinaClass.Api/ src/TinaClass.Api/
RUN dotnet publish src/TinaClass.Api/TinaClass.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
# The development settings (sample users and their public password) never ship. /data holds the Data Protection keys (and the SQLite
# file): mount a volume there. The app runs as the image's non-root user and listens on 8080 only.
RUN rm -f appsettings.Development.json && mkdir -p /data/keys && chown -R $APP_UID /data
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DataProtection__KeysPath=/data/keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TinaClass.Api.dll"]
