using Microsoft.JSInterop;

namespace ClanHub.Client.Components;

/// <summary>Executa uma ação a cada N segundos, pausando enquanto a aba do navegador não está visível.</summary>
public sealed class PollingLoop(IJSRuntime js, TimeSpan interval, Func<Task> tick) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public void Start() => _ = RunAsync();

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                bool visible;
                try { visible = await js.InvokeAsync<bool>("clanhub.isVisible"); } catch { visible = true; }
                if (visible) await tick();
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose() => _cts.Cancel();
}