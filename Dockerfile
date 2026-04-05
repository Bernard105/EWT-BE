FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY EasyWorkTogether.Api.csproj ./
RUN dotnet restore EasyWorkTogether.Api.csproj

COPY . ./
RUN dotnet publish EasyWorkTogether.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=10000

COPY --from=build /app/publish ./

EXPOSE 10000

ENTRYPOINT ["dotnet", "EasyWorkTogether.Api.dll"]
