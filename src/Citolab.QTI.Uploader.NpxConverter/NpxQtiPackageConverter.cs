using System.Diagnostics;
using System.Text;

namespace Citolab.QTI.Uploader;

public sealed class NpxQtiPackageConverter : IQtiPackageConverter
{
    private readonly string _npxCommand;

    public NpxQtiPackageConverter(string npxCommand = "npx")
    {
        _npxCommand = string.IsNullOrWhiteSpace(npxCommand) ? "npx" : npxCommand;
    }

    public async Task<string> ConvertQti2PackageToQti3Async(string inputZipPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputZipPath)) throw new ArgumentNullException(nameof(inputZipPath));
        if (!File.Exists(inputZipPath)) throw new FileNotFoundException("Input zip not found.", inputZipPath);

        var workingDir = Path.GetDirectoryName(inputZipPath) ?? Directory.GetCurrentDirectory();
        var inputFileName = Path.GetFileName(inputZipPath);
        var expectedOutputPath = Path.Combine(
            workingDir,
            $"{Path.GetFileNameWithoutExtension(inputFileName)}-qti3.zip");

        if (File.Exists(expectedOutputPath))
        {
            try { File.Delete(expectedOutputPath); } catch { }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _npxCommand,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"-p=@citolab/qti-convert qti-convert-pkg \"{inputFileName}\""
        };

        using var process = new Process { StartInfo = startInfo };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start npx process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"npx qti-convert-pkg failed (exit code {process.ExitCode}). {stderr}");
        }

        if (!File.Exists(expectedOutputPath))
        {
            throw new FileNotFoundException($"Converter did not produce expected output: {expectedOutputPath}. Stdout: {stdout} Stderr: {stderr}");
        }

        return expectedOutputPath;
    }
}

