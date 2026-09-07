# Build and run merchant. The image is the portable half of the deal: the same artefact runs from a
# systemd unit on the host that built it, or on somebody else's machine with nothing but a token.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props nuget.config ./
COPY src/Merchant/Merchant.csproj src/Merchant/
RUN dotnet restore src/Merchant/Merchant.csproj

COPY src/ src/
RUN dotnet publish src/Merchant/Merchant.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# The ledger is the only state. Mount this to keep it across upgrades; lose it and merchant reposts
# whatever each feed is currently offering, once.
ENV MERCHANT_DB=/data/merchant.db
VOLUME /data

# No token baked in, ever. Pass it at run time: docker run -e MERCHANT_TOKEN=... 
RUN useradd --system --uid 10001 merchant && mkdir -p /data && chown merchant /data
USER merchant

ENTRYPOINT ["dotnet", "/app/merchant.dll"]
