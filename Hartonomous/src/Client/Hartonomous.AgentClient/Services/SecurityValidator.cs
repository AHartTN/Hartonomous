/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the SecurityValidator for agent assembly code signing and security validation.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Models;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient.Services;

/// <summary>
/// Provides comprehensive security validation for agent assemblies including code signing,
/// strong name validation, certificate chain verification, and integrity checks.
/// </summary>
public class SecurityValidator
{
    private readonly ILogger<SecurityValidator> _logger;
    private readonly SecurityValidationPolicy _policy;

    public SecurityValidator(ILogger<SecurityValidator> logger, SecurityValidationPolicy policy)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>
    /// Performs comprehensive security validation on an agent assembly
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly to validate</param>
    /// <param name="definition">Agent definition for context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Security validation result</returns>
    public async Task<SecurityValidationResult> ValidateAgentSecurityAsync(
        string assemblyPath,
        AgentDefinition definition,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentNullException(nameof(assemblyPath));
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        var result = new SecurityValidationResult
        {
            AssemblyPath = assemblyPath,
            AgentId = definition.Id,
            ValidationTimestamp = DateTimeOffset.UtcNow
        };

        var validationTasks = new List<Task>();

        try
        {
            // Validate assembly exists and is accessible
            if (!File.Exists(assemblyPath))
            {
                result.SecurityIssues.Add($"Assembly file not found: {assemblyPath}");
                result.IsSecure = false;
                return result;
            }

            // Parallel validation for better performance
            var authenticodeTask = ValidateAuthenticodeSignatureAsync(assemblyPath, result, cancellationToken);
            var strongNameTask = ValidateStrongNameAsync(assemblyPath, result, cancellationToken);
            var integrityTask = ValidateIntegrityAsync(assemblyPath, definition, result, cancellationToken);
            var trustedPublisherTask = ValidateTrustedPublisherAsync(assemblyPath, result, cancellationToken);

            validationTasks.AddRange(new[] { authenticodeTask, strongNameTask, integrityTask, trustedPublisherTask });

            await Task.WhenAll(validationTasks);

            // Additional security checks based on policy
            await ValidateSecurityPolicyComplianceAsync(definition, result, cancellationToken);

            // Determine overall security status
            result.IsSecure = result.SecurityIssues.Count == 0 &&
                             (!_policy.RequireCodeSigning || result.AuthenticodeValid) &&
                             (!_policy.RequireStrongName || result.StrongNameValid) &&
                             (!_policy.RequireTrustedPublisher || result.PublisherTrusted);

            _logger.LogInformation("Security validation completed for agent {AgentId}. Secure: {IsSecure}, Issues: {IssueCount}",
                definition.Id, result.IsSecure, result.SecurityIssues.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Security validation failed for agent {AgentId} at {AssemblyPath}", definition.Id, assemblyPath);
            result.SecurityIssues.Add($"Security validation error: {ex.Message}");
            result.IsSecure = false;
            return result;
        }
    }

    /// <summary>
    /// Validates Authenticode signature on the assembly
    /// </summary>
    private async Task ValidateAuthenticodeSignatureAsync(
        string assemblyPath,
        SecurityValidationResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                var certificate = GetAuthenticodeCertificate(assemblyPath);
                if (certificate == null)
                {
                    if (_policy.RequireCodeSigning)
                    {
                        result.SecurityIssues.Add("Assembly is not code-signed with Authenticode");
                    }
                    else
                    {
                        result.Warnings.Add("Assembly is not code-signed with Authenticode");
                    }
                    result.AuthenticodeValid = false;
                    return;
                }

                result.SigningCertificate = certificate;
                result.AuthenticodeValid = true;

                // Validate certificate
                ValidateCertificate(certificate, result);

                _logger.LogDebug("Authenticode signature validated for {AssemblyPath}. Subject: {Subject}",
                    assemblyPath, certificate.Subject);

            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate Authenticode signature for {AssemblyPath}", assemblyPath);
            result.SecurityIssues.Add($"Authenticode validation failed: {ex.Message}");
            result.AuthenticodeValid = false;
        }
    }

    /// <summary>
    /// Validates strong name signature on the assembly
    /// </summary>
    private async Task ValidateStrongNameAsync(
        string assemblyPath,
        SecurityValidationResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                var assembly = Assembly.LoadFile(assemblyPath);
                var assemblyName = assembly.GetName();

                var publicKey = assemblyName.GetPublicKey();
                var publicKeyToken = assemblyName.GetPublicKeyToken();

                if (publicKey == null || publicKey.Length == 0)
                {
                    if (_policy.RequireStrongName)
                    {
                        result.SecurityIssues.Add("Assembly is not strong-name signed");
                    }
                    else
                    {
                        result.Warnings.Add("Assembly is not strong-name signed");
                    }
                    result.StrongNameValid = false;
                    return;
                }

                result.StrongNameValid = true;
                result.PublicKeyToken = Convert.ToHexString(publicKeyToken ?? Array.Empty<byte>());

                // Validate against trusted strong name keys if configured
                if (_policy.TrustedStrongNameKeys.Any())
                {
                    var publicKeyHex = Convert.ToHexString(publicKey);
                    if (!_policy.TrustedStrongNameKeys.Contains(publicKeyHex, StringComparer.OrdinalIgnoreCase))
                    {
                        result.SecurityIssues.Add("Assembly strong name key is not in trusted keys list");
                        result.StrongNameValid = false;
                    }
                }

                _logger.LogDebug("Strong name validation completed for {AssemblyPath}. Token: {Token}",
                    assemblyPath, result.PublicKeyToken);

            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate strong name for {AssemblyPath}", assemblyPath);
            result.SecurityIssues.Add($"Strong name validation failed: {ex.Message}");
            result.StrongNameValid = false;
        }
    }

    /// <summary>
    /// Validates file integrity using hash verification
    /// </summary>
    private async Task ValidateIntegrityAsync(
        string assemblyPath,
        AgentDefinition definition,
        SecurityValidationResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(async () =>
            {
                // Calculate SHA256 hash of the assembly
                using var sha256 = SHA256.Create();
                using var fileStream = File.OpenRead(assemblyPath);
                var hash = await sha256.ComputeHashAsync(fileStream, cancellationToken);
                var hashHex = Convert.ToHexString(hash);

                result.FileHash = hashHex;

                // Compare with expected checksum if provided
                if (!string.IsNullOrWhiteSpace(definition.Checksum))
                {
                    if (!string.Equals(hashHex, definition.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        result.SecurityIssues.Add($"File integrity check failed. Expected: {definition.Checksum}, Actual: {hashHex}");
                        result.IntegrityValid = false;
                        return;
                    }
                }

                result.IntegrityValid = true;
                _logger.LogDebug("Integrity validation completed for {AssemblyPath}. Hash: {Hash}", assemblyPath, hashHex);

            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate integrity for {AssemblyPath}", assemblyPath);
            result.SecurityIssues.Add($"Integrity validation failed: {ex.Message}");
            result.IntegrityValid = false;
        }
    }

    /// <summary>
    /// Validates that the publisher is trusted
    /// </summary>
    private async Task ValidateTrustedPublisherAsync(
        string assemblyPath,
        SecurityValidationResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                if (result.SigningCertificate == null)
                {
                    result.PublisherTrusted = false;
                    return;
                }

                var certificate = result.SigningCertificate;

                // Check against trusted publishers list
                if (_policy.TrustedPublishers.Any())
                {
                    var subject = certificate.Subject;
                    var issuer = certificate.Issuer;
                    var thumbprint = certificate.Thumbprint;

                    var isTrusted = _policy.TrustedPublishers.Any(tp =>
                        string.Equals(tp, subject, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tp, issuer, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tp, thumbprint, StringComparison.OrdinalIgnoreCase));

                    if (!isTrusted)
                    {
                        if (_policy.RequireTrustedPublisher)
                        {
                            result.SecurityIssues.Add($"Publisher is not trusted: {subject}");
                        }
                        else
                        {
                            result.Warnings.Add($"Publisher is not in trusted list: {subject}");
                        }
                        result.PublisherTrusted = false;
                        return;
                    }
                }

                result.PublisherTrusted = true;
                _logger.LogDebug("Publisher trust validation completed for {AssemblyPath}. Trusted: {Trusted}",
                    assemblyPath, result.PublisherTrusted);

            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate trusted publisher for {AssemblyPath}", assemblyPath);
            result.SecurityIssues.Add($"Publisher trust validation failed: {ex.Message}");
            result.PublisherTrusted = false;
        }
    }

    /// <summary>
    /// Validates compliance with security policies
    /// </summary>
    private async Task ValidateSecurityPolicyComplianceAsync(
        AgentDefinition definition,
        SecurityValidationResult result,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            // Check trust level requirements
            if (_policy.MaxAllowedTrustLevel < definition.Security.TrustLevel)
            {
                result.SecurityIssues.Add($"Agent trust level {definition.Security.TrustLevel} exceeds policy maximum {_policy.MaxAllowedTrustLevel}");
            }

            // Check capability restrictions
            var restrictedCapabilities = definition.Capabilities
                .Intersect(_policy.RestrictedCapabilities, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (restrictedCapabilities.Any())
            {
                result.SecurityIssues.Add($"Agent requests restricted capabilities: {string.Join(", ", restrictedCapabilities)}");
            }

            // Check network access policy
            if (definition.Resources.NetworkAccess > _policy.MaxAllowedNetworkAccess)
            {
                result.SecurityIssues.Add($"Agent network access {definition.Resources.NetworkAccess} exceeds policy maximum {_policy.MaxAllowedNetworkAccess}");
            }

            // Check file system access policy
            if (definition.Resources.FileSystemAccess > _policy.MaxAllowedFileSystemAccess)
            {
                result.SecurityIssues.Add($"Agent file system access {definition.Resources.FileSystemAccess} exceeds policy maximum {_policy.MaxAllowedFileSystemAccess}");
            }

        }, cancellationToken);
    }

    /// <summary>
    /// Gets the Authenticode certificate from a signed assembly
    /// </summary>
    private static X509Certificate2? GetAuthenticodeCertificate(string assemblyPath)
    {
        try
        {
            var certificate = X509Certificate.CreateFromSignedFile(assemblyPath);
            return new X509Certificate2(certificate);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Validates certificate properties and chain
    /// </summary>
    private void ValidateCertificate(X509Certificate2 certificate, SecurityValidationResult result)
    {
        try
        {
            // Check certificate expiration
            if (certificate.NotAfter < DateTime.Now)
            {
                result.SecurityIssues.Add($"Code signing certificate has expired: {certificate.NotAfter}");
            }

            if (certificate.NotBefore > DateTime.Now)
            {
                result.SecurityIssues.Add($"Code signing certificate is not yet valid: {certificate.NotBefore}");
            }

            // Validate certificate chain
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = _policy.CheckCertificateRevocation ?
                X509RevocationMode.Online : X509RevocationMode.NoCheck;

            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            var chainValid = chain.Build(certificate);
            if (!chainValid)
            {
                var errors = chain.ChainStatus.Select(status => status.StatusInformation).ToList();
                result.SecurityIssues.Add($"Certificate chain validation failed: {string.Join(", ", errors)}");
            }

            result.CertificateChainValid = chainValid;

            _logger.LogDebug("Certificate validation completed. Subject: {Subject}, Valid: {Valid}",
                certificate.Subject, chainValid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Certificate validation failed");
            result.SecurityIssues.Add($"Certificate validation error: {ex.Message}");
            result.CertificateChainValid = false;
        }
    }

    /// <summary>
    /// Throws SecurityException if validation fails based on policy
    /// </summary>
    public static void ThrowIfValidationFailed(SecurityValidationResult result, SecurityValidationPolicy policy)
    {
        if (!result.IsSecure)
        {
            var message = $"Security validation failed for agent {result.AgentId}";
            if (result.SecurityIssues.Any())
            {
                message += $": {string.Join("; ", result.SecurityIssues)}";
            }
            throw new SecurityException(message);
        }

        if (policy.FailOnWarnings && result.Warnings.Any())
        {
            var message = $"Security validation failed due to warnings for agent {result.AgentId}: {string.Join("; ", result.Warnings)}";
            throw new SecurityException(message);
        }
    }
}

/// <summary>
/// Security validation policy configuration
/// </summary>
public class SecurityValidationPolicy
{
    /// <summary>
    /// Whether code signing is required
    /// </summary>
    public bool RequireCodeSigning { get; set; } = true;

    /// <summary>
    /// Whether strong name signing is required
    /// </summary>
    public bool RequireStrongName { get; set; } = false;

    /// <summary>
    /// Whether publisher must be trusted
    /// </summary>
    public bool RequireTrustedPublisher { get; set; } = false;

    /// <summary>
    /// Whether to check certificate revocation status
    /// </summary>
    public bool CheckCertificateRevocation { get; set; } = true;

    /// <summary>
    /// Whether to fail validation on warnings
    /// </summary>
    public bool FailOnWarnings { get; set; } = false;

    /// <summary>
    /// Maximum allowed trust level
    /// </summary>
    public TrustLevel MaxAllowedTrustLevel { get; set; } = TrustLevel.Medium;

    /// <summary>
    /// Maximum allowed network access level
    /// </summary>
    public NetworkAccessLevel MaxAllowedNetworkAccess { get; set; } = NetworkAccessLevel.Internet;

    /// <summary>
    /// Maximum allowed file system access level
    /// </summary>
    public FileSystemAccessLevel MaxAllowedFileSystemAccess { get; set; } = FileSystemAccessLevel.Restricted;

    /// <summary>
    /// List of trusted publisher identifiers (subject, issuer, or thumbprint)
    /// </summary>
    public List<string> TrustedPublishers { get; set; } = new();

    /// <summary>
    /// List of trusted strong name public keys (hex encoded)
    /// </summary>
    public List<string> TrustedStrongNameKeys { get; set; } = new();

    /// <summary>
    /// List of restricted capabilities that agents cannot request
    /// </summary>
    public List<string> RestrictedCapabilities { get; set; } = new();

    /// <summary>
    /// Validation mode: Strict or Permissive
    /// </summary>
    public SecurityValidationMode ValidationMode { get; set; } = SecurityValidationMode.Strict;
}

/// <summary>
/// Security validation mode
/// </summary>
public enum SecurityValidationMode
{
    /// <summary>
    /// Strict validation - all security checks must pass
    /// </summary>
    Strict,

    /// <summary>
    /// Permissive validation - allows some security checks to fail with warnings
    /// </summary>
    Permissive,

    /// <summary>
    /// Development mode - minimal security checks for development/testing
    /// </summary>
    Development
}

/// <summary>
/// Security validation result
/// </summary>
public class SecurityValidationResult
{
    /// <summary>
    /// Path to the validated assembly
    /// </summary>
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>
    /// Agent ID being validated
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Overall security status
    /// </summary>
    public bool IsSecure { get; set; } = false;

    /// <summary>
    /// Whether Authenticode signature is valid
    /// </summary>
    public bool AuthenticodeValid { get; set; } = false;

    /// <summary>
    /// Whether strong name signature is valid
    /// </summary>
    public bool StrongNameValid { get; set; } = false;

    /// <summary>
    /// Whether file integrity check passed
    /// </summary>
    public bool IntegrityValid { get; set; } = false;

    /// <summary>
    /// Whether publisher is trusted
    /// </summary>
    public bool PublisherTrusted { get; set; } = false;

    /// <summary>
    /// Whether certificate chain is valid
    /// </summary>
    public bool CertificateChainValid { get; set; } = false;

    /// <summary>
    /// Signing certificate if available
    /// </summary>
    public X509Certificate2? SigningCertificate { get; set; }

    /// <summary>
    /// Public key token from strong name
    /// </summary>
    public string? PublicKeyToken { get; set; }

    /// <summary>
    /// SHA256 hash of the assembly file
    /// </summary>
    public string? FileHash { get; set; }

    /// <summary>
    /// Security issues found during validation
    /// </summary>
    public List<string> SecurityIssues { get; set; } = new();

    /// <summary>
    /// Security warnings
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Validation timestamp
    /// </summary>
    public DateTimeOffset ValidationTimestamp { get; set; } = DateTimeOffset.UtcNow;
}