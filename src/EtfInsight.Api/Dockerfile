# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files for restore (better layer caching)
COPY ["EtfInsight.Core/EtfInsight.Core.csproj", "EtfInsight.Core/"]
COPY ["EtfInsight.Infrastructure/EtfInsight.Infrastructure.csproj", "EtfInsight.Infrastructure/"]
COPY ["EtfInsight.Api/EtfInsight.Api.csproj", "EtfInsight.Api/"]

# Restore dependencies for the API project (will restore all dependencies including referenced projects)
RUN dotnet restore "EtfInsight.Api/EtfInsight.Api.csproj"

# Copy all source files
COPY ["EtfInsight.Core/", "EtfInsight.Core/"]
COPY ["EtfInsight.Infrastructure/", "EtfInsight.Infrastructure/"]
COPY ["EtfInsight.Api/", "EtfInsight.Api/"]

# Build
WORKDIR /src/EtfInsight.Api
RUN dotnet build "EtfInsight.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "EtfInsight.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Create non-root user for security
RUN adduser --disabled-password --gecos "" --uid 1000 dotnetuser && \
    chown -R dotnetuser:dotnetuser /app

USER dotnetuser

COPY --from=publish --chown=dotnetuser:dotnetuser /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "EtfInsight.Api.dll"]