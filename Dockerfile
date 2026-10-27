# --- Build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore layer (cached until a csproj / nuget.config changes)
COPY Genkan.Gateway.slnx Directory.Build.props nuget.config ./
COPY src/Genkan.Gateway.Application/Genkan.Gateway.Application.csproj src/Genkan.Gateway.Application/
COPY src/Genkan.Gateway.Api/Genkan.Gateway.Api.csproj src/Genkan.Gateway.Api/
# Genkan.Shared comes from GitHub Packages; nuget.config reads the token from GITHUB_PACKAGES_TOKEN.
#   docker build --secret id=github_token,env=GITHUB_PACKAGES_TOKEN .
RUN --mount=type=secret,id=github_token \
    GITHUB_PACKAGES_TOKEN="$(cat /run/secrets/github_token 2>/dev/null || true)" dotnet restore Genkan.Gateway.slnx

COPY src/ src/
RUN dotnet publish src/Genkan.Gateway.Api -c Release -o /app --no-restore

# --- Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Genkan.Gateway.Api.dll"]
