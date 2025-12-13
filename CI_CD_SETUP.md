# Hartonomous CI/CD Pipeline

## Docker-based CI/CD (Recommended)

### Dockerfile for Hartonomous
```dockerfile
FROM postgres:18-bullseye

# Install PostGIS, build tools
RUN apt-get update && apt-get install -y \
    postgresql-18-postgis-3 \
    build-essential cmake \
    libpq-dev postgresql-server-dev-18 \
    libeigen3-dev \
    rustc cargo \
    python3 python3-pip \
    git && rm -rf /var/lib/apt/lists/*

# Copy source code
COPY . /hartonomous
WORKDIR /hartonomous

# Build Shader (Rust)
RUN cd shader && cargo build --release && cp target/release/hartonomous-shader /usr/local/bin/

# Build Cortex (C++ PostgreSQL extension)
RUN cd cortex && mkdir build && cd build && \
    cmake .. -DCMAKE_BUILD_TYPE=Release && \
    make && make install

# Install Python connector
RUN pip3 install --no-cache-dir -r connector/requirements.txt

# Initialize database schema
RUN service postgresql start && \
    psql -U postgres -c "CREATE DATABASE hartonomous;" && \
    psql -U postgres -d hartonomous -f database/schema.sql && \
    psql -U postgres -d hartonomous -f database/migrations/001_monitoring.sql && \
    psql -U postgres -d hartonomous -f database/migrations/002_audit.sql && \
    service postgresql stop

EXPOSE 5432
CMD ["postgres"]
```

### GitHub Actions Workflow
```yaml
# .github/workflows/ci.yml
name: Hartonomous CI

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    
    services:
      postgres:
        image: postgis/postgis:18-3.6
        env:
          POSTGRES_PASSWORD: postgres
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Install Rust
        uses: actions-rs/toolchain@v1
        with:
          toolchain: stable
          
      - name: Build Shader
        run: cd shader && cargo build --release && cargo test
        
      - name: Install C++ dependencies
        run: sudo apt-get install -y libeigen3-dev postgresql-server-dev-18
        
      - name: Build Cortex
        run: |
          cd cortex
          mkdir build && cd build
          cmake .. -DCMAKE_BUILD_TYPE=Release
          make
          sudo make install
          
      - name: Setup Python
        uses: actions/setup-python@v5
        with:
          python-version: '3.11'
          
      - name: Install Python dependencies
        run: pip install -r connector/requirements.txt pytest
        
      - name: Initialize database
        run: |
          psql -h localhost -U postgres -c "CREATE DATABASE hartonomous;"
          psql -h localhost -U postgres -d hartonomous -f database/schema.sql
          
      - name: Run integration tests
        env:
          PGHOST: localhost
          PGUSER: postgres
          PGPASSWORD: postgres
          PGDATABASE: hartonomous
        run: pytest tests/test_integration.py -v
```

### GitLab CI Pipeline
```yaml
# .gitlab-ci.yml
stages:
  - build
  - test
  - deploy

variables:
  POSTGRES_DB: hartonomous
  POSTGRES_USER: postgres
  POSTGRES_PASSWORD: postgres

build_shader:
  stage: build
  image: rust:latest
  script:
    - cd shader
    - cargo build --release
    - cargo test
  artifacts:
    paths:
      - shader/target/release/hartonomous-shader

build_cortex:
  stage: build
  image: gcc:latest
  before_script:
    - apt-get update && apt-get install -y cmake libeigen3-dev postgresql-server-dev-18
  script:
    - cd cortex
    - mkdir build && cd build
    - cmake .. -DCMAKE_BUILD_TYPE=Release
    - make
  artifacts:
    paths:
      - cortex/build/cortex.so

integration_test:
  stage: test
  image: postgis/postgis:18-3.6
  services:
    - postgres:18
  dependencies:
    - build_shader
    - build_cortex
  before_script:
    - apt-get update && apt-get install -y python3 python3-pip
    - pip3 install -r connector/requirements.txt pytest
  script:
    - psql -h postgres -U postgres -c "CREATE DATABASE hartonomous;"
    - psql -h postgres -U postgres -d hartonomous -f database/schema.sql
    - pytest tests/test_integration.py -v

deploy_production:
  stage: deploy
  only:
    - main
  script:
    - ./scripts/deploy_production.ps1
```

## Windows Dev Environment (Current)

For local Windows development without Docker:

1. **Manual PostgreSQL Extension Install** (requires admin once):
   ```powershell
   # Run as Administrator
   .\cortex\install_cortex.ps1
   ```

2. **Idempotent Database Setup** (no admin required):
   ```powershell
   .\scripts\setup_database.ps1   # Fresh install
   .\scripts\repair_database.ps1  # Repair existing
   ```

3. **Build Components**:
   ```powershell
   .\scripts\build_shader.ps1     # Builds Rust shader
   # Cortex already built via CMake
   ```

4. **Run Tests**:
   ```powershell
   pytest tests/test_integration.py -v
   ```

## Migration to CI/CD

### Immediate Steps:
1. Containerize with Docker (see Dockerfile above)
2. Set up GitHub/GitLab CI (see workflows above)
3. Remove admin requirements from test suite
4. Use PostgreSQL Docker images with PostGIS pre-installed

### Current Limitations:
- ❌ Cortex extension requires admin install on Windows
- ✅ Database schema is fully idempotent
- ✅ Shader builds without admin
- ✅ Python connector works without admin

### CI/CD Best Practices:
- Use Docker containers for reproducible builds
- Run PostgreSQL as service in CI
- Avoid admin privilege requirements
- Separate unit tests from integration tests
- Cache build artifacts between stages
