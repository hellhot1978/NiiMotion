using WiimoteLib.NetCore;

namespace NiiRMotion.Infrastructure;

public sealed record BalanceBoardLiveDiagnostics(
    int SampleCount,
    float MinimumWeightKg,
    float MaximumWeightKg,
    float LastWeightKg,
    string ExtensionType);

public sealed class BalanceBoardDiagnosticsService
{
    public async Task<BalanceBoardLiveDiagnostics> RunAsync(int sampleCount = 100, CancellationToken cancellationToken = default)
    {
        var board = new Wiimote();
        var completion = new TaskCompletionSource<BalanceBoardLiveDiagnostics>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var minimum = float.MaxValue;
        var maximum = float.MinValue;
        var last = 0f;

        board.WiimoteChanged += (_, args) =>
        {
            if (args.WiimoteState.ExtensionType != ExtensionType.BalanceBoard) return;
            last = args.WiimoteState.BalanceBoardState.WeightKg;
            minimum = Math.Min(minimum, last);
            maximum = Math.Max(maximum, last);
            if (Interlocked.Increment(ref count) >= sampleCount)
                completion.TrySetResult(new(count, minimum, maximum, last, args.WiimoteState.ExtensionType.ToString()));
        };

        try
        {
            board.Connect();
            board.SetLEDs(1);
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            board.Disconnect();
            board.Dispose();
        }
    }
}
