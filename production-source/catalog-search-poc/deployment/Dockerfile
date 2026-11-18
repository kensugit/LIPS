FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore CatalogSearch.slnx
RUN dotnet test CatalogSearch.slnx --no-restore -c Release
RUN dotnet publish src/CatalogSearch.Web -c Release --no-restore -o /out/web && \
    dotnet publish src/CatalogSearch.ImportCli -c Release --no-restore -o /out/cli
FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app/web
COPY --from=build /out/web /app/web
COPY --from=build /out/cli /app/cli
COPY --chmod=755 deployment/entrypoint.sh /app/entrypoint.sh
USER app
ENTRYPOINT ["/app/entrypoint.sh"]
CMD ["web"]
