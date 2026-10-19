FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
# Apple Silicon VM workaround: disable hardware intrinsics and restore serially.
# Keep these scoped to the container; native host builds use normal defaults.
ENV DOTNET_EnableHWIntrinsic=0 DOTNET_EnableArm64Sve=0 DOTNET_EnableArm64Sve2=0
WORKDIR /src
COPY Directory.Build.props ./
COPY OncoMosaic.Domain/OncoMosaic.Domain.csproj OncoMosaic.Domain/packages.lock.json ./OncoMosaic.Domain/
COPY OncoMosaic.Application/OncoMosaic.Application.csproj OncoMosaic.Application/packages.lock.json ./OncoMosaic.Application/
COPY OncoMosaic.Infrastructure/OncoMosaic.Infrastructure.csproj OncoMosaic.Infrastructure/packages.lock.json ./OncoMosaic.Infrastructure/
COPY OncoMosaic.Api/OncoMosaic.Api.csproj OncoMosaic.Api/packages.lock.json ./OncoMosaic.Api/
RUN dotnet restore OncoMosaic.Api --locked-mode --disable-parallel
COPY OncoMosaic.Domain ./OncoMosaic.Domain
COPY OncoMosaic.Application ./OncoMosaic.Application
COPY OncoMosaic.Infrastructure ./OncoMosaic.Infrastructure
COPY OncoMosaic.Api ./OncoMosaic.Api
RUN dotnet publish OncoMosaic.Api -c Release --no-restore -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0.0
ENV DOTNET_EnableHWIntrinsic=0 DOTNET_EnableArm64Sve=0 DOTNET_EnableArm64Sve2=0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "OncoMosaic.Api.dll"]
