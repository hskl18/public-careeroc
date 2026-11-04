FROM python:3.14-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PATH="/app/.venv/bin:${PATH}"

WORKDIR /app

RUN pip install --no-cache-dir uv==0.13.6

COPY pyproject.toml uv.lock ./
COPY src ./src

RUN uv sync --frozen --no-dev \
    && useradd --system --uid 10001 --no-create-home careeroc

# Numeric so Kubernetes runAsNonRoot can verify it.
USER 10001
EXPOSE 8123

# Platforms such as Railway inject PORT; 8123 stays the local default.
# exec makes uvicorn PID 1, so it receives SIGTERM and shuts down cleanly.
CMD ["sh", "-c", "exec uvicorn careeroc_agent_runtime.app:app --host 0.0.0.0 --port ${PORT:-8123}"]
