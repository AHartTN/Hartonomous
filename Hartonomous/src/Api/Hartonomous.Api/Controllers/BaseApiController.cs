/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * CLEANUP: Base controller consolidating common patterns eliminating 300+ lines
 * of duplicate error handling, user context extraction, and validation logic.
 */

using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Hartonomous.Core.Services;

namespace Hartonomous.Api.Controllers;

/// <summary>
/// Base API controller providing common functionality to eliminate duplication
/// Consolidates user context extraction, validation patterns, and error handling
/// </summary>
[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Get current user ID with consistent error handling
    /// Eliminates repeated User.GetUserId() patterns across controllers
    /// </summary>
    protected string GetUserIdOrThrow()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? User.FindFirstValue("http://schemas.xmlsoap.org/wsdl/identity/claims/nameidentifier");

        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException("User ID not found in claims");

        return userId;
    }

    /// <summary>
    /// Validate input and execute action with unified error handling
    /// Consolidates validation + action execution pattern used across controllers
    /// </summary>
    protected async Task<ActionResult<T>> ValidateAndExecuteAsync<T>(
        Func<Task<T>> action,
        params System.ComponentModel.DataAnnotations.ValidationResult[] validations)
    {
        // Check validations
        var validationError = ValidationService.ValidateAndReturnBadRequest(validations);
        if (validationError != null)
            return validationError;

        try
        {
            var result = await action();
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { Error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (Exception ex)
        {
            // Log exception here if logging service available
            return StatusCode(500, new { Error = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Validate input and execute action returning ActionResult
    /// For actions that don't return data
    /// </summary>
    protected async Task<ActionResult> ValidateAndExecuteAsync(
        Func<Task> action,
        params System.ComponentModel.DataAnnotations.ValidationResult[] validations)
    {
        // Check validations
        var validationError = ValidationService.ValidateAndReturnBadRequest(validations);
        if (validationError != null)
            return validationError;

        try
        {
            await action();
            return Ok();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { Error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (Exception ex)
        {
            // Log exception here if logging service available
            return StatusCode(500, new { Error = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Generic CRUD endpoint for getting entity by ID
    /// Eliminates repetitive GetById controller methods
    /// </summary>
    protected async Task<ActionResult<TDto>> GetByIdAsync<TDto>(
        Guid id,
        Func<Guid, string, Task<TDto?>> getByIdFunc,
        string entityName = "Entity")
    {
        var userId = GetUserIdOrThrow();

        return await ValidateAndExecuteAsync(
            async () =>
            {
                var entity = await getByIdFunc(id, userId);
                if (entity == null)
                    throw new UnauthorizedAccessException($"{entityName} not found or access denied");
                return entity;
            },
            ValidationService.ValidateGuidNotEmpty(id, $"{entityName} ID")
        );
    }

    /// <summary>
    /// Generic CRUD endpoint for getting entities by user
    /// Eliminates repetitive GetByUser controller methods
    /// </summary>
    protected async Task<ActionResult<IEnumerable<TDto>>> GetByUserAsync<TDto>(
        Func<string, Task<IEnumerable<TDto>>> getByUserFunc)
    {
        var userId = GetUserIdOrThrow();

        return await ValidateAndExecuteAsync(
            () => getByUserFunc(userId)
        );
    }

    /// <summary>
    /// Generic CRUD endpoint for creating entities
    /// Eliminates repetitive Create controller methods
    /// </summary>
    protected async Task<ActionResult<TKey>> CreateAsync<TRequest, TKey>(
        TRequest request,
        Func<TRequest, string, Task<TKey>> createFunc,
        string entityName = "Entity")
    {
        if (request == null)
            return BadRequest($"{entityName} request cannot be null");

        var userId = GetUserIdOrThrow();

        return await ValidateAndExecuteAsync(
            () => createFunc(request, userId)
        );
    }

    /// <summary>
    /// Generic CRUD endpoint for deleting entities
    /// Eliminates repetitive Delete controller methods
    /// </summary>
    protected async Task<ActionResult> DeleteAsync<TKey>(
        TKey id,
        Func<TKey, string, Task<bool>> deleteFunc,
        string entityName = "Entity")
    {
        var userId = GetUserIdOrThrow();

        return await ValidateAndExecuteAsync(
            async () =>
            {
                var deleted = await deleteFunc(id, userId);
                if (!deleted)
                    throw new UnauthorizedAccessException($"{entityName} not found or access denied");
            },
            ValidationService.ValidateGuidNotEmpty((Guid)(object)id!, $"{entityName} ID")
        );
    }

    /// <summary>
    /// Standardized response for successful operations
    /// </summary>
    protected ActionResult SuccessResponse(string message = "Operation completed successfully")
    {
        return Ok(new { Success = true, Message = message });
    }

    /// <summary>
    /// Standardized response for created resources
    /// </summary>
    protected ActionResult<T> CreatedResponse<T>(T resource, string? location = null)
    {
        if (location != null)
            return Created(location, resource);

        return CreatedAtAction(null, null, resource);
    }
}