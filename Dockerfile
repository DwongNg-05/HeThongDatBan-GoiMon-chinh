# syntax=docker/dockerfile:1
# Build once, then run a small ASP.NET runtime image.  Connection strings and
# SMTP credentials are supplied only by the hosting platform at runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "global.json", "NuGet.Config", "./"]
COPY ["src/RestaurantManagement.Data/RestaurantManagement.Data.csproj", "src/RestaurantManagement.Data/"]
COPY ["src/RestaurantManagement.Data/packages.lock.json", "src/RestaurantManagement.Data/"]
COPY ["src/RestaurantManagement.Web/RestaurantManagement.Web.csproj", "src/RestaurantManagement.Web/"]
COPY ["src/RestaurantManagement.Web/packages.lock.json", "src/RestaurantManagement.Web/"]
RUN dotnet restore "src/RestaurantManagement.Web/RestaurantManagement.Web.csproj" --locked-mode

COPY . .
RUN dotnet publish "src/RestaurantManagement.Web/RestaurantManagement.Web.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish ./

# Render and Azure App Service both provide the runtime port via an environment
# variable.  The shell form expands it only when the container starts.
EXPOSE 10000
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet RestaurantManagement.Web.dll"]
