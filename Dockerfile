# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем всё и публикуем
COPY . .
RUN dotnet restore ./JacRed.Api/JacRed.Api.csproj
RUN dotnet publish ./JacRed.Api/JacRed.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# В README у тебя дефолтный listen-port = 9117 :contentReference[oaicite:1]{index=1}
ENV ASPNETCORE_URLS=http://+:9117
EXPOSE 9117

ENTRYPOINT ["dotnet","JacRed.Api.dll"]
