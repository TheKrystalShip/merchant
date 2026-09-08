# Build and run merchant. The image is the portable half of the deal: the same artefact runs from a
# systemd unit on the host that built it, or on somebody else's machine with nothing but a token.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# .editorconfig comes along because the analyzers and style rules run as part of the build: without
# it the image builds against different rules than anybody else does, and warnings-as-errors turns
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

VOLUME /data

# No token baked in, ever. Pass it at run time: docker run -e MERCHANT_TOKEN=... 
RUN useradd --system --uid 10001 merchant && mkdir -p /data && chown merchant /data
USER merchant

ENTRYPOINT ["dotnet", "/app/merchant.dll"]
