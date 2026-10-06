# Builds the Quadra API (src/Quadra.Api) into a small runtime image.
# The container listens on port 8080 (the .NET container default).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first, from the project files only, so this layer is cached until a
# package reference changes.
COPY Directory.Build.props ./
COPY src/Quadra.Api/Quadra.Api.csproj src/Quadra.Api/
COPY src/Quadra.Infrastructure/Quadra.Infrastructure.csproj src/Quadra.Infrastructure/
COPY src/Quadra.Shared/Quadra.Shared.csproj src/Quadra.Shared/
COPY src/Quadra.Modules.Auth/Quadra.Modules.Auth.csproj src/Quadra.Modules.Auth/
COPY src/Quadra.Modules.Matches/Quadra.Modules.Matches.csproj src/Quadra.Modules.Matches/
COPY src/Quadra.Modules.InGame/Quadra.Modules.InGame.csproj src/Quadra.Modules.InGame/
COPY src/Quadra.Modules.Profile/Quadra.Modules.Profile.csproj src/Quadra.Modules.Profile/
COPY src/Quadra.Modules.Geo/Quadra.Modules.Geo.csproj src/Quadra.Modules.Geo/
COPY src/Quadra.Modules.Notifications/Quadra.Modules.Notifications.csproj src/Quadra.Modules.Notifications/
COPY src/Quadra.Modules.Gamification/Quadra.Modules.Gamification.csproj src/Quadra.Modules.Gamification/
COPY src/Quadra.Modules.Realtime/Quadra.Modules.Realtime.csproj src/Quadra.Modules.Realtime/
RUN dotnet restore src/Quadra.Api/Quadra.Api.csproj

COPY src/ src/
RUN dotnet publish src/Quadra.Api/Quadra.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# The aspnet image ships a non-root user; do not run the API as root.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Quadra.Api.dll"]
