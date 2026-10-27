using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClanHub.E2E.Infrastructure;

/// <summary>Servidor SMTP mínimo (sem TLS) que só guarda as mensagens: dá para ler o link de confirmação de e-mail.</summary>
public sealed class SmtpSink : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    public List<string> Messages { get; } = [];
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public SmtpSink()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => Handle(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task Handle(TcpClient client)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        async Task Send(string line) => await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\r\n"));

        await Send("220 sink ready");
        var inData = false;
        var body = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            if (inData)
            {
                if (line == ".")
                {
                    inData = false;
                    lock (Messages) Messages.Add(body.ToString());
                    body.Clear();
                    await Send("250 queued");
                }
                else body.AppendLine(line.StartsWith("..") ? line[1..] : line);
                continue;
            }
            var cmd = line.ToUpperInvariant();
            if (cmd.StartsWith("EHLO") || cmd.StartsWith("HELO")) await Send("250 sink");
            else if (cmd.StartsWith("DATA")) { inData = true; await Send("354 go ahead"); }
            else if (cmd.StartsWith("QUIT")) { await Send("221 bye"); break; }
            else await Send("250 ok");
        }
    }

    /// <summary>Decodifica quoted-printable e HTML para localizar links no corpo.</summary>
    public static string Decode(string raw)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '=' && i + 2 < raw.Length && Uri.IsHexDigit(raw[i + 1]) && Uri.IsHexDigit(raw[i + 2]))
            { sb.Append((char)Convert.ToInt32(raw.Substring(i + 1, 2), 16)); i += 2; }
            else if (raw[i] == '=' && i + 1 < raw.Length && (raw[i + 1] == '\r' || raw[i + 1] == '\n'))
            { i += raw[i + 1] == '\r' ? 2 : 1; }
            else sb.Append(raw[i]);
        }
        return System.Net.WebUtility.HtmlDecode(sb.ToString());
    }

    public async Task<string> WaitForLinkAsync(string pathContains, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            string[] snapshot;
            lock (Messages) snapshot = [.. Messages];
            foreach (var m in snapshot.Reverse())
            {
                var match = System.Text.RegularExpressions.Regex.Match(Decode(m), @"https?://[^\s""'<>]*" + System.Text.RegularExpressions.Regex.Escape(pathContains) + @"[^\s""'<>]*");
                if (match.Success) return match.Value;
            }
            await Task.Delay(200);
        }
        throw new TimeoutException($"Nenhum e-mail com link '{pathContains}' chegou.");
    }

    public void Dispose() { _cts.Cancel(); _listener.Stop(); }
}