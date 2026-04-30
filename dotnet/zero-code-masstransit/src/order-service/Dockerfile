FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY order-service.csproj ./
RUN dotnet restore order-service.csproj
COPY . .
RUN dotnet publish order-service.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "order-service.dll"]
