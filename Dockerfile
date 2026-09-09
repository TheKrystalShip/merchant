# Build and run merchant. The image is the portable half of the deal: the same artefact runs from a
# systemd unit on the host that built it, or on a host holding nothing but Docker and a token.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# .editorconfig comes along because the analyzers and style rules run as part of the build: without
# it the image builds against different rules than a local build does, and warnings-as-errors turns
# that difference into a failure nobody can reproduce outside Docker.
COPY Directory.Build.props nuget.config global.json .editorconfig ./
COPY src/Merchant/Merchant.csproj src/Merchant/
RUN dotnet restore src/Merchant/Merchant.csproj

COPY src/ src/
# The example catalog is a Content item of the project: it has to be in the build context, or the
# published image ships without the file it seeds a fresh volume from.
COPY deploy/appsettings.example.jsonc deploy/
RUN dotnet publish src/Merchant/Merchant.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# The ledger is the only state. Mount this to keep it across upgrades; lose it and merchant reposts
# whatever each feed is currently offering, once.
ENV MERCHANT_DB=/data/merchant.db

# The feed catalog. Nothing is baked into the image: on a first run merchant writes the shipped
# example here, so an empty volume still produces a working bot and leaves behind the file to edit.
ENV MERCHANT_CONFIG=/data/appsettings.json

# The account and the directory it owns come before VOLUME, and have to: a build step that changes
# a path already declared as a volume is discarded by the classic builder, so declaring it first
# leaves /data owned by root, and merchant — running unprivileged — cannot open its own ledger or
# seed its own settings file. The image builds either way, which is what makes the order the only
# thing standing between this and a container that starts and immediately gives up.
RUN useradd --system --uid 10001 merchant && mkdir -p /data && chown merchant /data

VOLUME /data
USER merchant

# No token baked in, ever. Pass it at run time: docker run -e MERCHANT_TOKEN=...

ENTRYPOINT ["dotnet", "/app/merchant.dll"]
