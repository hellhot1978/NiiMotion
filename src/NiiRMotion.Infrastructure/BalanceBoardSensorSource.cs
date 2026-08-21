using System.Diagnostics;
using System.Threading.Channels;
using NiiRMotion.Core;
using WiimoteLib.NetCore;

namespace NiiRMotion.Infrastructure;

public sealed class BalanceBoardSensorSource : ISensorSource<BalanceBoardSample>
{
    private const int TareSamples = 200;
    private static readonly object ConnectionGate = new();
    private static Wiimote? SharedBoard;
    private readonly BoundedSensorBuffer<BalanceBoardSample> _buffer = new(256);
    private Wiimote? _board;
    private long _sequence;
    private int _tareCount;
    private float _tareFrontLeft, _tareFrontRight, _tareBackLeft, _tareBackRight;
    private readonly List<float> _tareFrontLeftValues = [], _tareFrontRightValues = [], _tareBackLeftValues = [], _tareBackRightValues = [];

    public string SourceId => "balance-board";
    public SensorMode Mode => SensorMode.Live;
    public ChannelReader<BalanceBoardSample> Samples => _buffer.Reader;
    public bool IsTared => _tareCount >= TareSamples;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_board is not null) throw new InvalidOperationException("Balance Board source already started.");
        cancellationToken.ThrowIfCancellationRequested();
        lock (ConnectionGate)
        {
            if (SharedBoard is null)
            {
                SharedBoard = new Wiimote();
                SharedBoard.Connect();
                SharedBoard.SetLEDs(1);
            }
            _board = SharedBoard;
            _board.WiimoteChanged += OnChanged;
        }
        var readyBy = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (!IsTared && DateTime.UtcNow < readyBy)
            await Task.Delay(25, cancellationToken);
        if (!IsTared) throw new InvalidOperationException("Balance Board sıfırlanamadı. Kart tamamen boşken Oyun Modunu yeniden başlat.");
    }

    private void OnChanged(object? sender, WiimoteChangedEventArgs args)
    {
        if (args.WiimoteState.ExtensionType != ExtensionType.BalanceBoard) return;
        // WiimoteLib exposes load-cell values at four times the physical kg
        // scale and divides their sum by four for WeightKg.
        var values = args.WiimoteState.BalanceBoardState.SensorValuesKg;
        var frontLeft = values.TopLeft / 4f;
        var frontRight = values.TopRight / 4f;
        var backLeft = values.BottomLeft / 4f;
        var backRight = values.BottomRight / 4f;
        if (_tareCount < TareSamples)
        {
            if (Math.Abs(frontLeft) + Math.Abs(frontRight) + Math.Abs(backLeft) + Math.Abs(backRight) < .01f) return;
            // Never learn the user's body weight as the empty-board baseline.
            // A loaded board must wait until the user steps off before taring.
            if (frontLeft + frontRight + backLeft + backRight > 15f) return;
            _tareFrontLeftValues.Add(frontLeft); _tareFrontRightValues.Add(frontRight); _tareBackLeftValues.Add(backLeft); _tareBackRightValues.Add(backRight);
            if (++_tareCount == TareSamples)
            {
                _tareFrontLeft = Median(_tareFrontLeftValues); _tareFrontRight = Median(_tareFrontRightValues); _tareBackLeft = Median(_tareBackLeftValues); _tareBackRight = Median(_tareBackRightValues);
            }
            return;
        }
        var timestamp = new SensorTimestamp(Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow);
        _buffer.TryWrite(new(SourceId, Interlocked.Increment(ref _sequence), timestamp,
            Math.Max(0, frontLeft - _tareFrontLeft), Math.Max(0, frontRight - _tareFrontRight),
            Math.Max(0, backLeft - _tareBackLeft), Math.Max(0, backRight - _tareBackRight)));
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }

    public ValueTask DisposeAsync()
    {
        if (_board is not null)
        {
            _board.WiimoteChanged -= OnChanged;
            _board = null;
        }
        _buffer.Complete();
        return ValueTask.CompletedTask;
    }
}
