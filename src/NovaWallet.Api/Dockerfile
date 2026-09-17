FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY NovaWallet.sln ./
COPY src/NovaWallet.Api/NovaWallet.Api.csproj ./src/NovaWallet.Api/
RUN dotnet restore src/NovaWallet.Api/NovaWallet.Api.csproj
COPY src/NovaWallet.Api/ ./src/NovaWallet.Api/
RUN dotnet publish src/NovaWallet.Api/NovaWallet.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "NovaWallet.Api.dll"]
