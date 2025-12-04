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
    private static readonly char[] _wordSeparators = new[] { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '-', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\' };
    private static readonly char[] _sentenceSeparators = new[] { '.', '!', '?' };
    
    public ContentType SupportedContentType => ContentType.Text;

    public TextDecomposer(ILogger<TextDecomposer> logger)
    {
        _logger = logger;
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
        var byteConstants = DecomposeBytes(data, cancellationToken);
        constants.AddRange(byteConstants);

        // 2. Character-level constants (UTF-8 multi-byte handling)
        var charConstants = DecomposeCharacters(text, cancellationToken);
        constants.AddRange(charConstants);

        // 3. Word-level constants
        var wordConstants = DecomposeWords(text, cancellationToken);
        constants.AddRange(wordConstants);

        // 4. Sentence-level constants (optional, for larger context)
        if (text.Length > 100) // Only for sufficiently large texts
        {
            var sentenceConstants = DecomposeSentences(text, cancellationToken);
            constants.AddRange(sentenceConstants);
        }

        _logger.LogInformation(
            "Text decomposition complete: {ConstantCount} constants ({ByteCount} bytes, {CharCount} chars, {WordCount} words)",
            constants.Count, byteConstants.Count, charConstants.Count, wordConstants.Count);
        
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
            constant.Project();
            constants.Add(constant);
        }
        
        return constants;
    }

    private List<Constant> DecomposeWords(string text, CancellationToken cancellationToken)
    {
        var words = text.Split(_wordSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var constants = new List<Constant>(words.Length);
        
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (string.IsNullOrWhiteSpace(word)) continue;
            
            var wordBytes = Encoding.UTF8.GetBytes(word);
            var constant = Constant.Create(wordBytes, ContentType.Text);
            constant.Project();
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
        _logger.LogInformation("Using binary fallback for text decomposition");
        var constants = new List<Constant>(data.Length);
        
        for (int i = 0; i < data.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var byteData = new byte[] { data[i] };
            var constant = Constant.Create(byteData, ContentType.Binary);
            constant.Project();
            constants.Add(constant);
        }
        
        return await Task.FromResult(constants);
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
