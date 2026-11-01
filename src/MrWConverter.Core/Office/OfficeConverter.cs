using System.Globalization;
using System.Text;
using MrWConverter.Core.Conversion;
using MrWConverter.Core.Media;

namespace MrWConverter.Core.Office;

/// <summary>Word, Excel and PowerPoint files, and the formats like them, to PDF through LibreOffice.</summary>
public sealed class OfficeConverter(OfficeRuntime runtime) : IFileConverter
{
    private const string Writer = "writer_pdf_Export";
    private const string Calc = "calc_pdf_Export";
    private const string Impress = "impress_pdf_Export";

    private static readonly TimeSpan TimeLimit = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlyDictionary<string, string> Exports = new Dictionary<string, string>
    {
        ["doc"] = Writer,
        ["docx"] = Writer,
        ["odt"] = Writer,
        ["rtf"] = Writer,
        ["txt"] = Writer,
        ["xls"] = Calc,
        ["xlsx"] = Calc,
        ["ods"] = Calc,
        ["csv"] = Calc,
        ["ppt"] = Impress,
        ["pptx"] = Impress,
        ["odp"] = Impress,
    };

    // Macros never run, links are never followed, and files never reach out for remote pictures,
    // so that opening a document can't do anything but show it. Rewritten before every job.
    private const string LockedDownSettings = """
        <?xml version="1.0" encoding="UTF-8"?>
        <oor:items xmlns:oor="http://openoffice.org/2001/registry" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="DisableMacrosExecution" oor:op="fuse"><value>true</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="BlockUntrustedRefererLinks" oor:op="fuse"><value>true</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Load"><prop oor:name="UpdateLinkMode" oor:op="fuse"><value>0</value></prop></item>
        </oor:items>
        """;

    public static bool Handles(string format) => Exports.ContainsKey(format);

    public bool CanConvert(string sourceFormat, string targetFormat) => targetFormat == "pdf" && Handles(sourceFormat);

    public async Task ConvertAsync(string inputPath, string outputPath, string sourceFormat, string targetFormat, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var office = runtime.Executable ?? throw new OfficeDownloadException("LibreOffice is not set up.");

        // A plain name, because a file's own name could confuse LibreOffice's options.
        var job = Path.Combine(runtime.WorkFolder, Guid.NewGuid().ToString("N"));
        var settings = Path.Combine(runtime.ProfileFolder, "user");
        var rendered = Path.Combine(job, "out");
        Directory.CreateDirectory(settings);
        Directory.CreateDirectory(rendered);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(settings, "registrymodifications.xcu"), LockedDownSettings, cancellationToken);

            var source = Path.Combine(job, $"source.{sourceFormat}");
            await CopyAsSourceAsync(inputPath, source, sourceFormat, cancellationToken);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeLimit);

            try
            {
                await ToolProcess.RunAsync(office, Arguments(runtime.ProfileFolder, source, sourceFormat, rendered), null, limit.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"LibreOffice did not finish within {TimeLimit.TotalMinutes} minutes.");
            }

            var result = Path.Combine(rendered, "source.pdf");
            if (!File.Exists(result) || new FileInfo(result).Length == 0)
            {
                throw new InvalidDataException("LibreOffice could not convert this file.");
            }

            File.Move(result, outputPath, overwrite: true);
        }
        finally
        {
            try
            {
                Directory.Delete(job, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A copy of the document stays in the app's own folder until the app is removed.
            }
        }
    }

    /// <summary>The separator a spreadsheet saved as text uses: Excel writes semicolons where a comma is the decimal mark.</summary>
    internal static int CsvSeparator(string firstLine) =>
        firstLine.Count(character => character == ';') > firstLine.Count(character => character == ',') ? ';' : ',';

    /// <summary>Text comes as UTF-8 for LibreOffice. Anything else is taken to be in the Windows language's old encoding.</summary>
    internal static string TextInUtf8(byte[] bytes, int oldCodePage)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(oldCodePage).GetString(bytes);
        }
    }

    private static async Task CopyAsSourceAsync(string inputPath, string source, string format, CancellationToken cancellationToken)
    {
        if (format is "txt" or "csv")
        {
            var text = TextInUtf8(await File.ReadAllBytesAsync(inputPath, cancellationToken), CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            await File.WriteAllTextAsync(source, text, new UTF8Encoding(false), cancellationToken);
            return;
        }

        File.Copy(inputPath, source);
    }

    private static List<string> Arguments(string profile, string source, string format, string rendered)
    {
        List<string> arguments =
        [
            $"-env:UserInstallation={new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri}",
            "--headless",
            "--nologo",
            "--nodefault",
            "--nolockcheck",
            "--nofirststartwizard",
            "--norestore",
        ];

        if (format == "txt")
        {
            arguments.Add("--infilter=Text (encoded):UTF8,LF");
        }
        else if (format == "csv")
        {
            var firstLine = File.ReadLines(source).FirstOrDefault() ?? "";
            arguments.Add($"--infilter=Text - txt - csv (StarCalc):{CsvSeparator(firstLine)},34,76,1");
        }

        arguments.AddRange(["--convert-to", $"pdf:{Exports[format]}", "--outdir", rendered, source]);
        return arguments;
    }
}
