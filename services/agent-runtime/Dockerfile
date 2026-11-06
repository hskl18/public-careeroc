# Build stage: uv installs the locked dependencies into /app/.venv; only that venv ships.
FROM python:3.14-slim AS build

COPY --from=ghcr.io/astral-sh/uv:0.13.6 /uv /usr/local/bin/uv
ENV UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_PYTHON_DOWNLOADS=never

WORKDIR /app

# Dependencies before the source, so a code change does not reinstall them.
COPY pyproject.toml uv.lock ./
RUN uv sync --locked --no-dev --no-install-project

COPY src ./src
RUN uv sync --locked --no-dev --no-editable

FROM python:3.14-slim AS runtime

LABEL org.opencontainers.image.source="https://github.com/hskl18/public-careeroc" \
      org.opencontainers.image.licenses="MIT"

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PATH="/app/.venv/bin:${PATH}"

WORKDIR /app

RUN useradd --system --uid 10001 --no-create-home careeroc
COPY --from=build /app/.venv /app/.venv

# Numeric so Kubernetes runAsNonRoot can verify it.
USER 10001
EXPOSE 8123

# Some platforms inject PORT; 8123 stays the local default.
# exec makes uvicorn PID 1, so it receives SIGTERM and shuts down cleanly.
CMD ["sh", "-c", "exec uvicorn careeroc_agent_runtime.app:app --host 0.0.0.0 --port ${PORT:-8123}"]
