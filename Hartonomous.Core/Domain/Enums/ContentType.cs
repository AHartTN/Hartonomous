namespace Hartonomous.Core.Domain.Enums;

/// <summary>
/// Type of content for universal atomic decomposition
/// </summary>
public enum ContentType
{
    /// <summary>Raw binary data</summary>
    Binary = 0,
    
    /// <summary>Text content (UTF-8, ASCII, etc.)</summary>
    Text = 1,
    
    /// <summary>Image data (JPEG, PNG, etc.)</summary>
    Image = 2,
    
    /// <summary>Audio data (MP3, WAV, FLAC, etc.)</summary>
    Audio = 3,
    
    /// <summary>Video data (MP4, AVI, MKV, etc.)</summary>
    Video = 4,
    
    /// <summary>AI model weights and parameters</summary>
    ModelWeights = 5,
    
    /// <summary>Structured data (JSON, XML, etc.)</summary>
    Structured = 6,
    
    /// <summary>Code/source files</summary>
    Code = 7,
    
    /// <summary>Document files (PDF, DOCX, etc.)</summary>
    Document = 8
}
