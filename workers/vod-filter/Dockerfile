FROM python:3.11-slim AS builder

ENV VIRTUAL_ENV=/opt/venv
RUN python -m venv "$VIRTUAL_ENV"
ENV PATH="$VIRTUAL_ENV/bin:$PATH"

WORKDIR /build
COPY requirements-production.txt .
RUN pip install --no-cache-dir --upgrade pip \
    && pip install --no-cache-dir -r requirements-production.txt

FROM python:3.11-slim AS final

ENV VIRTUAL_ENV=/opt/venv \
    PATH="/opt/venv/bin:$PATH" \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    DATABASE_PATH=/app/data/vod-filter.db \
    WORKER_HEARTBEAT_PATH=/app/data/last-run.json

RUN groupadd --gid 10001 watchlist \
    && useradd --uid 10001 --gid watchlist --no-log-init --create-home watchlist \
    && mkdir -p /app/data \
    && chown -R watchlist:watchlist /app

WORKDIR /app
COPY --from=builder /opt/venv /opt/venv
COPY --chown=watchlist:watchlist src/ ./src/
COPY --chown=watchlist:watchlist continuous_sync.py sync_movies.py reconcile_sync.py healthcheck.py ./

USER watchlist

HEALTHCHECK --interval=60s --timeout=10s --start-period=90s --retries=3 \
    CMD ["python", "healthcheck.py"]

ENTRYPOINT ["python", "continuous_sync.py"]
CMD ["--continuous"]
