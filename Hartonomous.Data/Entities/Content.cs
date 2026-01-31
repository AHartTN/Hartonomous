using System;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Entities;

public class Content
{
    public HartonomousId Id { get; set; }
    public HartonomousId TenantId { get; set; }
    public HartonomousId UserId { get; set; }
    public ushort ContentType { get; set; }
    public byte[] ContentHash { get; set; } = Array.Empty<byte>();
    public ulong ContentSize { get; set; }
    public string? ContentMimeType { get; set; }
    public string? ContentLanguage { get; set; }
    public string? ContentSource { get; set; }
    public string? ContentEncoding { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ValidatedAt { get; set; } = DateTimeOffset.UtcNow;
}
