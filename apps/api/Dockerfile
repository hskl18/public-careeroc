# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src

# Directory.Build.props carries the release version and build rules every project shares.
COPY global.json Directory.Build.props ./
COPY apps/api/Careeroc.Api.csproj apps/api/
COPY src/Careeroc.Application/Careeroc.Application.csproj src/Careeroc.Application/
COPY src/Careeroc.Contracts/Careeroc.Contracts.csproj src/Careeroc.Contracts/
COPY src/Careeroc.Domain/Careeroc.Domain.csproj src/Careeroc.Domain/
COPY src/Careeroc.Infrastructure/Careeroc.Infrastructure.csproj src/Careeroc.Infrastructure/

RUN dotnet restore apps/api/Careeroc.Api.csproj

# Only the projects the API compiles, so doc or web changes keep the publish layer cached.
COPY apps/api apps/api
COPY src src
RUN dotnet publish apps/api/Careeroc.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e AS runtime
LABEL org.opencontainers.image.source="https://github.com/hskl18/public-careeroc" \
      org.opencontainers.image.licenses="MIT"
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ForwardedHeaders__Enabled=true
ENV DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish ./
# The model-backed email parser reads this playbook from the working directory.
COPY docs/agent-memory/recruiting-email-playbook.md docs/agent-memory/

USER $APP_UID
EXPOSE 8080

# exec makes dotnet PID 1, so it receives SIGTERM and shuts down cleanly.
CMD ["sh", "-c", "exec dotnet Careeroc.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]
