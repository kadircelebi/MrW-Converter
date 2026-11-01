namespace MrWConverter.Core.Conversion;

/// <summary>A converter that can turn one file into several, such as a PDF into one picture per page.</summary>
public interface IPagedConverter : IFileConverter
{
    /// <summary>True if this conversion makes one output per page rather than a single file.</summary>
    bool HasPages(string sourceFormat, string targetFormat);

    Task<int> CountPagesAsync(string inputPath, CancellationToken cancellationToken);

    Task ConvertPageAsync(string inputPath, string outputPath, int pageIndex, string targetFormat, CancellationToken cancellationToken);
}
