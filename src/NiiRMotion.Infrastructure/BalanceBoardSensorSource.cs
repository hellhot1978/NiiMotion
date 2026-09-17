using System.Diagnostics;
using System.Threading.Channels;
using NiiRMotion.Core;
using WiimoteLib.NetCore;

namespace NiiRMotion.Infrastructure;

public sealed class BalanceBoardSensorSource : ISensorSource<BalanceBoardSample>
{
    private const int TareSamples = 200;
    private const int TareTimeoutSeconds = 15;
    private const float TareWeightThreshold = 15f;
    private static readonly object ConnectionGate = new();
    private static Wiimote? SharedBoard;
    private static int _subscriberCount;
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

        Wiimote? boardToConnect = null;
        lock (ConnectionGate)
        {
            if (SharedBoard is null)
            {
                SharedBoard = new Wiimote();
                boardToConnect = SharedBoard;
            }
            _board = SharedBoard;
            Interlocked.Increment(ref _subscriberCount);
            _board.WiimoteChanged += OnChanged;
        }

        if (boardToConnect is not null)
        {
            try
            {
                await Task.Run(() => boardToConnect.Connect(), cancellationToken);
                boardToConnect.SetLEDs(1);
            }
            catch
            {
                lock (ConnectionGate)
                {
                    _board.WiimoteChanged -= OnChanged;
                    _board = null;
                    Interlocked.Decrement(ref _subscriberCount);
                    SharedBoard = null;
                }
                throw;
            }
        }

        var readyBy = DateTime.UtcNow + TimeSpan.FromSeconds(TareTimeoutSeconds);
        while (!IsTared && DateTime.UtcNow < readyBy)
            await Task.Delay(25, cancellationToken);
        if (!IsTared) throw new InvalidOperationException("Balance Board could not be tared. Make sure the board is completely empty and restart Game Mode.");
    }

    private void OnChanged(object? sender, WiimoteChangedEventArgs args)
    {
        if (args.WiimoteState.ExtensionType != ExtensionType.BalanceBoard) return;
        var values = args.WiimoteState.BalanceBoardState.SensorValuesKg;
        var frontLeft = values.TopLeft / 4f;
        var frontRight = values.TopRight / 4f;
        var backLeft = values.BottomLeft / 4f;
        var backRight = values.BottomRight / 4f;
        if (_tareCount < TareSamples)
        {
            if (frontLeft + frontRight + backLeft + backRight > TareWeightThreshold) return;
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
        var sorted = values.OrderBy(x => x).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }

    public ValueTask DisposeAsync()
    {
        if (_board is not null)
        {
            _board.WiimoteChanged -= OnChanged;
            _board = null;
            if (Interlocked.Decrement(ref _subscriberCount) <= 0)
            {
                lock (ConnectionGate)
                {
                    if (SharedBoard is not null)
                    {
                        try { SharedBoard.Disconnect(); } catch { }
                        SharedBoard = null;
                    }
                    _subscriberCount = 0;
                }
            }
        }
        _buffer.Complete();
        return ValueTask.CompletedTask;
    }
}
