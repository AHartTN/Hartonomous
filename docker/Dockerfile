# ============================================================================
# Hartonomous PostgreSQL + Citus + PostGIS + PL/Python
# Three tables, universal atoms, spatial semantics, columnar storage
# ============================================================================

FROM citusdata/citus:12.1.5-pg16

LABEL maintainer="aharttn@gmail.com"
LABEL description="Hartonomous: Self-organizing intelligence substrate on PostgreSQL with Citus columnar storage"

# Install PostGIS and additional dependencies
RUN apt-get update && apt-get install -y \
    postgresql-16-postgis-3 \
    postgresql-16-postgis-3-scripts \
    postgis \
    postgresql-plpython3-16 \
    python3-pip \
    python3-dev \
    build-essential \
    gcc \
    postgresql-client \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Install Python packages for GPU acceleration (optional)
RUN pip3 install --no-cache-dir \
    numpy \
    scipy

# Optional: Install PyTorch and CuPy for GPU support
# Uncomment if deploying with CUDA GPU
# RUN pip3 install --no-cache-dir \
#     torch torchvision --index-url https://download.pytorch.org/whl/cu118 \
#     cupy-cuda11x

# Copy initialization scripts
COPY schema/ /docker-entrypoint-initdb.d/

# Copy requirements
COPY api/requirements.txt /app/requirements.txt

# Install Python dependencies
RUN pip install --no-cache-dir -r /app/requirements.txt

# Copy application code
COPY api/ /app/api/
COPY run.py /app/run.py

# Set working directory
WORKDIR /app

# Environment variables
ENV POSTGRES_DB=hartonomous
ENV POSTGRES_USER=postgres

# PostgreSQL configuration optimizations
RUN echo "shared_buffers = 256MB" >> /usr/share/postgresql/postgresql.conf.sample && \
    echo "effective_cache_size = 1GB" >> /usr/share/postgresql/postgresql.conf.sample && \
    echo "work_mem = 64MB" >> /usr/share/postgresql/postgresql.conf.sample && \
    echo "maintenance_work_mem = 256MB" >> /usr/share/postgresql/postgresql.conf.sample && \
    echo "max_parallel_workers_per_gather = 2" >> /usr/share/postgresql/postgresql.conf.sample && \
    echo "random_page_cost = 1.1" >> /usr/share/postgresql/postgresql.conf.sample

EXPOSE 5432 8000

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
    CMD pg_isready -U postgres -d hartonomous || exit 1

# Run application with proper event loop configuration
CMD ["python3", "run.py"]
