namespace MrWConverter.Core.Conversion;

/// <summary>A job stopped because of this file. The UI words the message for the user.</summary>
public sealed class ConversionException(string filePath, Exception innerException)
    : Exception($"Could not convert {filePath}.", innerException)
{
    public string FilePath { get; } = filePath;
}
