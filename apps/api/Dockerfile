# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c AS runtime
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
