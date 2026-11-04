# Set the major version of dotnet
ARG DOTNET_VERSION=10.0
# Set the major version of nodejs
ARG NODEJS_VERSION_MAJOR=22

# Note: For optimal Docker caching, ensure all steps within a build stage are arranged in order of "least likely to be impacted by changes"

### Build frontend assets using node js ###
FROM node:${NODEJS_VERSION_MAJOR}-bookworm-slim AS assets
WORKDIR /app

# - Copy over and restore npm packages separately so they can be cached by Docker
COPY ./src/Dfe.Briefing.Tool.Web/wwwroot/package.json      /app/
COPY ./src/Dfe.Briefing.Tool.Web/wwwroot/package-lock.json /app/
RUN npm ci --ignore-scripts

# - Only copy over frontend assets so this can be cached independently to other app changes
COPY ./src/Dfe.Briefing.Tool.Web/wwwroot/ /app/
# - "npm run build" starts the three webpack builds in the background with "&", so the step could finish before they do:
#   run them one after another instead, so a failed build fails the image
RUN npx webpack --config webpack.assets.config.js --mode=production && \
    npx webpack --config webpack.config.js --mode=production && \
    npx webpack --config webpack.appinsights.config.js --mode=production

### Build the app using the dotnet SDK ###
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-azurelinux3.0 AS build
WORKDIR /build

# Mount GitHub Token as a Docker secret so that NuGet Feed can be accessed
RUN --mount=type=secret,id=github_token dotnet nuget add source --username USERNAME --password $(cat /run/secrets/github_token) --store-password-in-clear-text --name github "https://nuget.pkg.github.com/DFE-Digital/index.json"

# - Copy over csprojs and restore nuget packages separately so they can be cached by Docker
COPY ./src/Dfe.Briefing.Tool.Domain/Dfe.Briefing.Tool.Domain.csproj                 /build/Dfe.Briefing.Tool.Domain/
COPY ./src/Dfe.Briefing.Tool.Application/Dfe.Briefing.Tool.Application.csproj       /build/Dfe.Briefing.Tool.Application/
COPY ./src/Dfe.Briefing.Tool.Infrastructure/Dfe.Briefing.Tool.Infrastructure.csproj /build/Dfe.Briefing.Tool.Infrastructure/
COPY ./src/Dfe.Briefing.Tool.Web/Dfe.Briefing.Tool.Web.csproj                       /build/Dfe.Briefing.Tool.Web/
RUN dotnet restore Dfe.Briefing.Tool.Web

# - Only copy over relevant C# code so this can be cached independently to other app changes
COPY ./src/Dfe.Briefing.Tool.Domain/         /build/Dfe.Briefing.Tool.Domain/
COPY ./src/Dfe.Briefing.Tool.Application/    /build/Dfe.Briefing.Tool.Application/
COPY ./src/Dfe.Briefing.Tool.Infrastructure/ /build/Dfe.Briefing.Tool.Infrastructure/
COPY ./src/Dfe.Briefing.Tool.Web/            /build/Dfe.Briefing.Tool.Web/

# - Frontend assets go in before publishing: MapStaticAssets only serves the files in the manifest publish writes
COPY --from=assets /app/dist   /build/Dfe.Briefing.Tool.Web/wwwroot/dist/
COPY --from=assets /app/assets /build/Dfe.Briefing.Tool.Web/wwwroot/assets/

RUN dotnet build Dfe.Briefing.Tool.Web --no-restore -c Release && \
    dotnet publish Dfe.Briefing.Tool.Web --no-build -c Release -o /app -p:UseAppHost=false

### Build a runtime environment ###
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-azurelinux3.0 AS final
WORKDIR /app
LABEL org.opencontainers.image.source="https://github.com/DFE-Digital/briefing-tool"

# - Copy and configure docker entrypoint script
COPY ./scripts/docker-entrypoint.sh /app/docker-entrypoint.sh
RUN chmod +x /app/docker-entrypoint.sh

# - Copy dotnet app
COPY --from=build /app /app

# - Azure Container Apps ingress targets this port (the image's default ASPNETCORE_HTTP_PORTS); TLS ends at the ingress
EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["/app/docker-entrypoint.sh"]
CMD ["dotnet", "Dfe.Briefing.Tool.Web.dll"]
