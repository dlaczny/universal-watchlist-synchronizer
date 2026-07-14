using System.Security;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class DataProtectionKeyRingHostedService(
    IOptions<DataProtectionKeyRingOptions> options,
    IHostEnvironment environment,
    ITraktTokenProtector tokenProtector) : IHostedService
{
    private const string InvalidProductionPathMessage =
        "Data Protection key ring path must be absolute in Production.";
    private const string UnusablePathMessage =
        "The Data Protection key ring path is unavailable or not writable.";
    private const string FailedProbeMessage =
        "The Data Protection startup probe failed.";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string keyRingPath = options.Value.KeyRingPath;
        if (environment.IsProduction() && !Path.IsPathFullyQualified(keyRingPath))
        {
            throw new InvalidOperationException(InvalidProductionPathMessage);
        }

        EnsureWritableDirectory(keyRingPath);
        ValidateProtectionRoundTrip();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static void EnsureWritableDirectory(string keyRingPath)
    {
        try
        {
            DirectoryInfo directory = Directory.CreateDirectory(keyRingPath);
            directory.Refresh();
            if (!directory.Exists)
            {
                throw new IOException("The configured directory was not created.");
            }

            string writeProbePath = Path.Combine(
                directory.FullName,
                $".write-probe-{Guid.NewGuid():N}");
            using (FileStream stream = new(
                writeProbePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose))
            {
                stream.WriteByte(0);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(writeProbePath))
            {
                File.Delete(writeProbePath);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException)
        {
            throw new InvalidOperationException(UnusablePathMessage, exception);
        }
    }

    private void ValidateProtectionRoundTrip()
    {
        byte[] probe = RandomNumberGenerator.GetBytes(32);
        byte[]? recoveredProbe = null;
        try
        {
            string plaintext = Convert.ToBase64String(probe);
            string ciphertext = tokenProtector.Protect(plaintext);
            string recoveredPlaintext = tokenProtector.Unprotect(ciphertext);
            recoveredProbe = Convert.FromBase64String(recoveredPlaintext);
            if (!CryptographicOperations.FixedTimeEquals(probe, recoveredProbe))
            {
                throw new InvalidOperationException(FailedProbeMessage);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(FailedProbeMessage);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(probe);
            if (recoveredProbe is not null)
            {
                CryptographicOperations.ZeroMemory(recoveredProbe);
            }
        }
    }
}
