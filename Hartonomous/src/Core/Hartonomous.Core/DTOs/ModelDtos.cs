/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains Data Transfer Objects (DTOs) for model-related API communications,
 * supporting clean separation between domain models and external interfaces.
 */

namespace Hartonomous.Core.DTOs;

public record ModelMetadataDto(Guid ModelId, string ModelName, string Version, string License);