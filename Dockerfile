# syntax=docker/dockerfile:1

# One build, three images: `--target hub`, `--target proxy`, `--target loadgen`.

# ---- build -------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore in its own layer: it only re-runs when package versions or project files change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ScaleLab.Core/ScaleLab.Core.csproj src/ScaleLab.Core/
COPY src/ScaleLab.Hosting/ScaleLab.Hosting.csproj src/ScaleLab.Hosting/
COPY src/ScaleLab.HubServer/ScaleLab.HubServer.csproj src/ScaleLab.HubServer/
COPY src/ScaleLab.Proxy/ScaleLab.Proxy.csproj src/ScaleLab.Proxy/
COPY src/ScaleLab.LoadGen/ScaleLab.LoadGen.csproj src/ScaleLab.LoadGen/
RUN --mount=type=cache,id=signalr-scale-lab-nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet restore src/ScaleLab.HubServer/ScaleLab.HubServer.csproj && \
    dotnet restore src/ScaleLab.Proxy/ScaleLab.Proxy.csproj && \
    dotnet restore src/ScaleLab.LoadGen/ScaleLab.LoadGen.csproj

COPY .editorconfig ./
COPY src/ src/
RUN --mount=type=cache,id=signalr-scale-lab-nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet publish src/ScaleLab.HubServer/ScaleLab.HubServer.csproj -c Release --no-restore -o /app/hub -p:UseAppHost=false && \
    dotnet publish src/ScaleLab.Proxy/ScaleLab.Proxy.csproj -c Release --no-restore -o /app/proxy -p:UseAppHost=false && \
    dotnet publish src/ScaleLab.LoadGen/ScaleLab.LoadGen.csproj -c Release --no-restore -o /app/loadgen -p:UseAppHost=false

# ---- runtime images ----------------------------------------------------------------------------------
# Chiseled Ubuntu: no shell, no package manager, and a non-root "app" user (UID 1654) baked in.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS hub
LABEL org.opencontainers.image.title="signalr-scale-lab-hub" \
      org.opencontainers.image.source="https://github.com/hekimcan206/signalr-scale-lab" \
      org.opencontainers.image.licenses="MIT"
WORKDIR /app
COPY --from=build --link /app/hub .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ScaleLab.HubServer.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS proxy
LABEL org.opencontainers.image.title="signalr-scale-lab-proxy" \
      org.opencontainers.image.source="https://github.com/hekimcan206/signalr-scale-lab" \
      org.opencontainers.image.licenses="MIT"
WORKDIR /app
COPY --from=build --link /app/proxy .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ScaleLab.Proxy.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS loadgen
LABEL org.opencontainers.image.title="signalr-scale-lab-loadgen" \
      org.opencontainers.image.source="https://github.com/hekimcan206/signalr-scale-lab" \
      org.opencontainers.image.licenses="MIT"
WORKDIR /app
COPY --from=build --link /app/loadgen .
USER $APP_UID
ENTRYPOINT ["dotnet", "ScaleLab.LoadGen.dll"]
CMD ["demo"]
