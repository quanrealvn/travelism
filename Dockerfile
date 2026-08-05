# Travelism — production image.
#
# Three stages, because the SPA and the API have completely different toolchains
# and neither belongs in the runtime image. Vite writes into the API project's
# wwwroot, so the web build has to finish before `dotnet publish` runs — that
# ordering is the whole reason this is not a single stage.

# ---- 1. The SPA ------------------------------------------------------------
FROM node:22-alpine AS web

WORKDIR /src/web

# Manifests first: this layer is cached until a dependency actually changes,
# which is the difference between a 10-second and a 90-second rebuild.
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci

COPY src/web/ ./
# `npm run build` typechecks first, so a type error fails the image rather than
# shipping a broken bundle.
RUN npm run build


# ---- 2. The API ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS api

WORKDIR /src

# Directory.Build.props carries TargetFramework, LangVersion and the
# warnings-as-errors settings for every project; global.json pins the SDK.
# Without them `dotnet restore` fails with an empty TargetFramework, because
# not one csproj in this solution declares its own.
COPY Directory.Build.props global.json ./
COPY WeGo.sln ./
COPY src/WeGo.Api/WeGo.Api.csproj src/WeGo.Api/
COPY src/WeGo.Domain/WeGo.Domain.csproj src/WeGo.Domain/
COPY src/WeGo.Infrastructure/WeGo.Infrastructure.csproj src/WeGo.Infrastructure/
COPY tests/WeGo.Api.Tests/WeGo.Api.Tests.csproj tests/WeGo.Api.Tests/
COPY tests/WeGo.Domain.Tests/WeGo.Domain.Tests.csproj tests/WeGo.Domain.Tests/
RUN dotnet restore src/WeGo.Api/WeGo.Api.csproj

COPY src/ src/

# The compiled SPA, dropped in before publish so it is picked up as content.
COPY --from=web /src/WeGo.Api/wwwroot/ src/WeGo.Api/wwwroot/

# Warnings are errors in this solution, so this also gates the deploy on a
# clean build.
RUN dotnet publish src/WeGo.Api/WeGo.Api.csproj \
    -c Release \
    -o /app \
    --no-restore


# ---- 3. Runtime ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime

WORKDIR /app

# icu-libs: SQLite needs ICU for the collations EF Core emits, and Alpine's
#   .NET images ship with globalization switched off by default.
# tzdata: Alpine has no IANA time zone database at all. Without it
#   TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh") throws, which is
#   every trip in this app — the default zone is Vietnam. Windows and the
#   Debian-based images carry their own copy, so the whole test suite passes
#   locally and every single trip creation fails in production.
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Where the volume gets mounted. Created here so the app still starts if it is
# run without one — it will simply write a database that does not survive.
RUN mkdir -p /data

# Fly routes to 8080 by default; binding explicitly means the image does not
# depend on the platform's environment to listen on the right port.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Not root. The .NET 8 images already ship a non-root `app` user, so this
# adopts it rather than creating one — `adduser app` fails outright on these
# images. /data is chowned so migrations can create the database file and its
# WAL sidecars on first boot.
RUN chown -R app:app /app /data
USER app

COPY --from=api --chown=app:app /app ./

ENTRYPOINT ["dotnet", "WeGo.Api.dll"]
