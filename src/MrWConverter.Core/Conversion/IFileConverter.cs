namespace MrWConverter.Core.Conversion;

public interface IFileConverter
{
    bool CanConvert(string sourceFormat, string targetFormat);

    /// <param name="progress">Receives the finished fraction of this file, from 0 to 1, if the converter can measure it.</param>
    Task ConvertAsync(
        string inputPath,
        string outputPath,
        string sourceFormat,
        string targetFormat,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
