# Multi-stage build for minimal production image
FROM python:3.11-slim as builder

WORKDIR /app

# Install build dependencies
RUN apt-get update && \
    apt-get install -y --no-install-recommends gcc && \
    rm -rf /var/lib/apt/lists/*

# Copy requirements and install dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir --user -r requirements.txt

# Production stage
FROM python:3.11-slim

WORKDIR /app

# Copy Python dependencies from builder
COPY --from=builder /root/.local /root/.local

# Copy application code
COPY src/ ./src/
COPY continuous_sync.py .
COPY run_all_syncs.py .
COPY cleanup_removed_movies.py .
COPY sync_library_to_watchlist.py .
COPY validate_providers.py .
COPY cache_inspect.py .
COPY compare_watchlist_sources.py .

# Create data directory for persistence
RUN mkdir -p /app/data

# Update PATH to include local Python packages
ENV PATH=/root/.local/bin:$PATH

# Set Python to run in unbuffered mode (better for Docker logs)
ENV PYTHONUNBUFFERED=1

# Default to running the combined sync loop. The interval comes from SYNC_INTERVAL.
ENTRYPOINT ["python", "continuous_sync.py"]
CMD ["--continuous"]
