FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Automation.Core/Automation.Core.csproj src/Automation.Core/
COPY src/Automation.Infrastructure/Automation.Infrastructure.csproj src/Automation.Infrastructure/
COPY src/Automation.Worker/Automation.Worker.csproj src/Automation.Worker/
RUN dotnet restore src/Automation.Worker/Automation.Worker.csproj
COPY src/ src/
RUN dotnet publish src/Automation.Worker/Automation.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "Automation.Worker.dll"]
