using System.Diagnostics;

namespace ClanHub.E2E.Infrastructure;

/// <summary>Igual ao AppFixture, mas roda o app *publicado* em Production (service worker real, arquivos pré-comprimidos).</summary>
public sealed class PublishedAppFixture : AppFixture
{
    private string? _dir;

    protected override (string Dll, string WorkingDirectory, string Environment) Launch(string repoRoot)
    {
        _dir = Path.Combine(Path.GetTempPath(), "clanhub-e2e-publish-" + Guid.NewGuid().ToString("N"));
        var psi = new ProcessStartInfo("dotnet", $"publish src/ClanHub.Server -c Release -o \"{_dir}\" -nologo -v q")
        {
            WorkingDirectory = repoRoot, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("dotnet publish falhou:\n" + output);
        return (Path.Combine(_dir, "ClanHub.Server.dll"), _dir, "Production");
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        try { if (_dir is not null && Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}

[CollectionDefinition("e2e-published")]
public class PublishedCollection : ICollectionFixture<PublishedAppFixture>;