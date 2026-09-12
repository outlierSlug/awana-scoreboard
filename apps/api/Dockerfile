# syntax=docker/dockerfile:1

# Build context is apps/api, which is what render.yaml sets as rootDir.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH=amd64
WORKDIR /src

# Copy only the project files first, restore, and only then bring in the
# source. Restore is the slow step and it only depends on these files, so
# Docker reuses that layer on every build where no dependency changed. Copying
# everything up front instead turns a 25 second rebuild into 90 seconds, which
# adds up fast on a day when you deploy ten times.
COPY Directory.Build.props Awana.slnx ./
COPY src/Awana.Scoring/Awana.Scoring.csproj              src/Awana.Scoring/
COPY src/Awana.Data/Awana.Data.csproj                    src/Awana.Data/
COPY src/Awana.Api/Awana.Api.csproj                      src/Awana.Api/
COPY tests/Awana.Scoring.Tests/Awana.Scoring.Tests.csproj tests/Awana.Scoring.Tests/
COPY tests/Awana.Api.Tests/Awana.Api.Tests.csproj         tests/Awana.Api.Tests/

# PublishReadyToRun has to be set here as well as on publish. It pulls in a
# runtime pack, and restore is what fetches it. Setting it only on publish
# fails with NETSDK1094, "a valid runtime package was not found".
RUN dotnet restore src/Awana.Api/Awana.Api.csproj \
      --runtime linux-x64 \
      -p:PublishReadyToRun=true

COPY . .

# PublishReadyToRun pre-compiles IL to native code ahead of time, which cuts
# startup. It requires an explicit runtime identifier, and --self-contained
# false keeps the shared framework from the base image rather than shipping a
# second copy of it.
#
# Native AOT is deliberately NOT used. SignalR and EF Core both fight it and
# the result is a multi-day detour for a startup win that does not matter here,
# since the Render Starter plan does not spin down.
RUN dotnet publish src/Awana.Api/Awana.Api.csproj \
      --configuration Release \
      --runtime linux-x64 \
      --self-contained false \
      --no-restore \
      --output /app \
      -p:PublishReadyToRun=true

# Chiseled: no shell, no package manager, and a non-root user by default. Small
# attack surface and a much smaller image than the full aspnet base.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .

# Render supplies $PORT at runtime and Program.cs binds it. It is deliberately
# not set here: ENV ASPNETCORE_URLS=http://+:${PORT} does not expand in an
# exec-form ENTRYPOINT, and the failure is silent.
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Awana.Api.dll"]
