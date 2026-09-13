FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY fiap-cloud-games-notifications-function.csproj ./
RUN dotnet restore fiap-cloud-games-notifications-function.csproj
COPY . .
RUN dotnet publish fiap-cloud-games-notifications-function.csproj -c Release -o /publish --no-restore

FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
COPY --from=build /publish /home/site/wwwroot
EXPOSE 80
