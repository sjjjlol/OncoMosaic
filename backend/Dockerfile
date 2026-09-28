FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
WORKDIR /src
COPY OncoMosaic.Api/OncoMosaic.Api.csproj OncoMosaic.Api/packages.lock.json ./OncoMosaic.Api/
RUN dotnet restore OncoMosaic.Api --locked-mode
COPY OncoMosaic.Api ./OncoMosaic.Api
RUN dotnet publish OncoMosaic.Api -c Release --no-restore -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "OncoMosaic.Api.dll"]
