FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Automation.Core/Automation.Core.csproj src/Automation.Core/
COPY src/Automation.Infrastructure/Automation.Infrastructure.csproj src/Automation.Infrastructure/
COPY src/Automation.Api/Automation.Api.csproj src/Automation.Api/
RUN dotnet restore src/Automation.Api/Automation.Api.csproj
COPY src/ src/
RUN dotnet publish src/Automation.Api/Automation.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# curl is only needed for the compose health check
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Automation.Api.dll"]
