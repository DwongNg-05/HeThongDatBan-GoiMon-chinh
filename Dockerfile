# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore src/RestaurantManagement.Web/RestaurantManagement.Web.csproj --locked-mode \
    && dotnet restore tools/RestaurantManagement.DbTool/RestaurantManagement.DbTool.csproj --locked-mode
RUN --mount=type=secret,id=sixlabors_license,required=true \
    dotnet publish src/RestaurantManagement.Web/RestaurantManagement.Web.csproj -c Release --no-restore -o /out/web /p:UseAppHost=false /p:SixLaborsLicenseFile=/run/secrets/sixlabors_license \
    && dotnet publish tools/RestaurantManagement.DbTool/RestaurantManagement.DbTool.csproj -c Release --no-restore -o /out/dbtool /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS dbtool
WORKDIR /app
COPY --from=build /out/dbtool/ .
COPY database/ ./database/
USER $APP_UID
ENTRYPOINT ["dotnet", "RestaurantManagement.DbTool.dll"]
CMD ["migrate"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
WORKDIR /app
COPY --from=build --chown=app:app /out/web/ .
RUN mkdir -p App_Data/data-protection-keys App_Data/emails wwwroot/uploads/mon-an \
    && chown -R app:app App_Data wwwroot/uploads
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RestaurantManagement.Web.dll"]
