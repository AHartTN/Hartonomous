using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Hartonomous.Infrastructure.Services.Decomposers;

/// <summary>
/// Text content decomposer - multi-granularity decomposition
/// Creates constants at byte, character, word, and sentence levels
/// Uses UTF-8 encoding for text processing
/// </summary>
public sealed class TextDecomposer : IContentDecomposer
{
    private readonly ILogger<TextDecomposer> _logger;
    private readonly IQuantizationService _quantizationService;
    private static readonly char[] _wordSeparators = new[] { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '-', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\' };
    private static readonly char[] _sentenceSeparators = new[] { '.', '!', '?' };
    
    public ContentType SupportedContentType => ContentType.Text;

    public TextDecomposer(
        ILogger<TextDecomposer> logger,
        IQuantizationService quantizationService)
    {
        _logger = logger;
        _quantizationService = quantizationService;
    }

    public async Task<List<Constant>> DecomposeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Data cannot be null or empty", nameof(data));
        }

        _logger.LogDebug("Decomposing text content: {Size} bytes", data.Length);

        var constants = new List<Constant>();
        string text;
        
        try
        {
            text = Encoding.UTF8.GetString(data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode UTF-8 text, falling back to binary decomposition");
            // Fallback to byte-level if not valid UTF-8
            return await DecomposeBinaryFallbackAsync(data, cancellationToken);
        }

        // 1. Byte-level constants (for raw deduplication)
        foreach (var b in data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var byteData = new byte[] { b };
            var constant = Constant.Create(byteData, ContentType.Text);
            var (y, z, m) = _quantizationService.Quantize(byteData);
            constant.ProjectWithQuantization((int)y, (int)z, (int)m);
            constants.Add(constant);
        }

        // 2. Character-level constants (for text similarity)
        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var charBytes = Encoding.UTF8.GetBytes(new[] { ch });
            var constant = Constant.Create(charBytes, ContentType.Text);
            var (y, z, m) = _quantizationService.Quantize(charBytes);
            constant.ProjectWithQuantization((int)y, (int)z, (int)m);
            constants.Add(constant);
        }

        // 3. Word-level constants (for semantic similarity)
        var words = text.Split(_wordSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var wordBytes = Encoding.UTF8.GetBytes(word);
            var constant = Constant.Create(wordBytes, ContentType.Text);
            var (y, z, m) = _quantizationService.Quantize(wordBytes);
            constant.ProjectWithQuantization((int)y, (int)z, (int)m);
            constants.Add(constant);
        }

        // 4. Sentence-level constants (for document structure)
        var sentences = text.Split(_sentenceSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var sentence in sentences.Where(s => !string.IsNullOrWhiteSpace(s) && s.Length >= 10))
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var sentenceBytes = Encoding.UTF8.GetBytes(sentence);
            var constant = Constant.Create(sentenceBytes, ContentType.Text);
            var (y, z, m) = _quantizationService.Quantize(sentenceBytes);
            constant.ProjectWithQuantization((int)y, (int)z, (int)m);
            constants.Add(constant);
        }

        _logger.LogInformation(
            "Text decomposition complete: {ConstantCount} constants (multi-granularity decomposition)",
            constants.Count);
        
        return constants;
    }

    private List<Constant> DecomposeBytes(byte[] data, CancellationToken cancellationToken)
    {
        var constants = new List<Constant>(data.Length);
        
        for (int i = 0; i < data.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var byteData = new byte[] { data[i] };
            var constant = Constant.Create(byteData, ContentType.Text);
            constant.Project();
            constants.Add(constant);
        }
        
        return constants;
    }

    private List<Constant> DecomposeCharacters(string text, CancellationToken cancellationToken)
    {
        var constants = new List<Constant>(text.Length);
        
        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var charBytes = Encoding.UTF8.GetBytes(new[] { ch });
            var constant = Constant.Create(charBytes, ContentType.Text);
            var (y, z, m) = _quantizationService.Quantize(charBytes);
            constant.ProjectWithQuantization((int)y, (int)z, (int)m);
            constants.Add(constant);
        }
        
        return constants;
    }

    private List<Constant> DecomposeWords(string text, CancellationToken cancellationToken)
    {
        var words = text.Split(_wordSeparators, StringSplitOptions.RemoveEmptyEntries);
        var constants = new List<Constant>(words.Length);
        
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var wordBytes = Encoding.UTF8.GetBytes(word);
            var constant = Constant.Create(wordBytes, ContentType.Text);
            var (y, z, m) = _quantizationService.Quantize(wordBytes);
            constant.ProjectWithQuantization((int)y, (int)z, (int)m);
            constants.Add(constant);
        }
        
        return constants;
    }

    private List<Constant> DecomposeSentences(string text, CancellationToken cancellationToken)
    {
        var sentences = text.Split(_sentenceSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var constants = new List<Constant>(sentences.Length);
        
        foreach (var sentence in sentences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (string.IsNullOrWhiteSpace(sentence) || sentence.Length < 10) continue;
            
            var sentenceBytes = Encoding.UTF8.GetBytes(sentence);
            var constant = Constant.Create(sentenceBytes, ContentType.Text);
            constant.Project();
            constants.Add(constant);
        }
        
        return constants;
    }

    private async Task<List<Constant>> DecomposeBinaryFallbackAsync(byte[] data, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Falling back to binary decomposition for {Size} bytes", data.Length);
        
        var constants = new List<Constant>(data.Length);
        
        for (int i = 0; i < data.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var byteData = new byte[] { data[i] };
            var constant = Constant.Create(byteData, ContentType.Binary);
            var (y, z, m) = _quantizationService.Quantize(byteData);
            constant.ProjectWithQuantization((int)y, (int)z, (int)m);
            constants.Add(constant);
        }
        
        return constants;
    }

    public bool CanDecompose(byte[] data, ContentType declaredType)
    {
        if (declaredType == ContentType.Text)
            return true;

        // Auto-detect UTF-8 text
        try
        {
            var text = Encoding.UTF8.GetString(data);
            // Check if contains mostly printable ASCII/UTF-8 characters
            var printableRatio = text.Count(c => !char.IsControl(c) || char.IsWhiteSpace(c)) / (double)text.Length;
            return printableRatio > 0.7; // 70% printable threshold
        }
        catch
        {
            return false;
        }
    }
}
