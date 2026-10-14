# Game server image (BackendServer + SharedLogic). Built by .github/workflows/ci.yml, run by deploy/.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY SharedLogic/ SharedLogic/
COPY BackendServer/ BackendServer/
RUN dotnet publish BackendServer/BackendServer.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BackendServer.dll"]
