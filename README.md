# Hartonomous

**Atomic Content-Addressable Storage System**

Copyright © 2025 Anthony Hart. All Rights Reserved.

## Overview

Universal atomic decomposition and content-addressable storage for all digital content through deterministic spatial projection and Hilbert curve indexing.

## Features

- **Universal Deduplication**: All constants across all modalities stored once
- **Landmark Projection**: Hash-based deterministic 3D coordinate mapping
- **PostGIS Geometric Queries**: Spatial operations (k-NN, convex hulls, clustering)
- **BPE Composition**: Automatic pattern learning through Byte Pair Encoding
- **Cross-Modal**: Unified storage for text, images, audio, video, AI models
- **SIMD/AVX Optimized**: 4-8x performance improvement
- **GPU Acceleration**: Optional CUDA support for massive batches

## Architecture

- **Constants as Landmarks**: Hash ? (X,Y,Z) ? Hilbert ID
- **Graph Structure**: BPE creates compositional relationships
- **Database IS the Model**: Intelligence emerges from graph patterns
- **PostgreSQL + PostGIS**: 3D spatial queries and indexing

## Projects

- **Hartonomous.Core**: Domain models and interfaces
- **Hartonomous.Data**: EF Core + PostGIS data access
- **Hartonomous.Infrastructure**: Caching, storage, external services
- **Hartonomous.API**: REST API for ingestion and queries
- **Hartonomous.Worker**: Background BPE processing
- **Hartonomous.App**: Blazor + MAUI cross-platform UI

## Quick Start

```bash
# Clone repository
git clone git@ssh.dev.azure.com:v3/aharttn/Hartonomous/Hartonomous

# Build
dotnet build --configuration Release

# Run API
dotnet run --project Hartonomous.API
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Technical Specification](docs/TECHNICAL_SPECIFICATION.md)
- [Database Schema](docs/DATABASE_SCHEMA.md)
- [Azure DevOps Integration](docs/AZURE_DEVOPS_INTEGRATION.md)
- [Azure Arc Integration](docs/AZURE_ARC_INTEGRATION.md)

## License

Copyright © 2025 Anthony Hart. All Rights Reserved.

This software and associated documentation files may not be used, copied, modified, merged, published, distributed, sublicensed, or sold without express written permission from the copyright holder.

## Contact

Anthony Hart - aharttn@gmail.com
