using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class LiveLocomotionService : IAsyncDisposable
{
    private readonly object _fusionLock = new();
    private readonly List<IAsyncDisposable> _sources = [];
    private CancellationTokenSource? _lifetime;
    private Task[] _workers = [];
    private VrLocomotionSession? _vrSession;
    private SensorFusionEngine? _fusion;
    private SensorFusionEngine? _auxFusion;
    private PsMoveGaitEngine? _psMoveGait;
    private StreamWriter? _diagnosticWriter;

    public bool IsRunning => _lifetime is { IsCancellationRequested: false };
    public string ModeDescription { get; private set; } = "OFF";
    public event EventHandler<string>? CriticalSensorLost;
    public event EventHandler<LocomotionTelemetrySample>? TelemetryUpdated;
    private long _lastTelemetryEventTicks;

    public async Task StartPsMoveOnlyAsync(bool includePhone = false, bool includeBoard = false, CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        var onboarding = await new PsMoveOnboardingService().GetStatusAsync(cancellationToken);
        if (!onboarding.IsReady) throw new InvalidOperationException(onboarding.Instruction);
        var profile = JsonSerializer.Deserialize<PsMoveTrainingProfile>(await File.ReadAllTextAsync(NiiMotionPaths.PsMoveTrainingProfile, cancellationToken))
            ?? throw new InvalidDataException("PS Move kişisel profili okunamadı.");
        var phoneProfile = File.Exists(Path.Combine(NiiMotionPaths.Config, "personal-phone-motion.json")) ? await PersonalPhoneMotion.LoadAsync(Path.Combine(NiiMotionPaths.Config, "personal-phone-motion.json"), cancellationToken) : null;
        var boardProfile = File.Exists(Path.Combine(NiiMotionPaths.Config, "personal-board-motion.json")) ? await PersonalBoardMotion.LoadAsync(Path.Combine(NiiMotionPaths.Config, "personal-board-motion.json"), cancellationToken) : null;
        _psMoveGait = new(profile);
        _auxFusion = new SensorFusionEngine(phoneProfile: phoneProfile, boardProfile: boardProfile, allowBoardTurn: includeBoard);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); var token = _lifetime.Token;
        try
        {
            var source = new PsMoveSensorSource(NiiMotionPaths.PsMoveAssignments, NiiMotionPaths.PsMoveFactoryCalibration);
            _sources.Add(source); await source.StartAsync(token);
            var gameProfile = new GameMotionProfileStore().LoadActive();
            var selectedGame = new GameSelectionStore().Load();
            var optimization = new GameSensorOptimizationStore().Load(selectedGame, "psmove-only");
            gameProfile = gameProfile with { SpeedMultiplier = gameProfile.SpeedMultiplier * optimization.DistanceScale };
            _vrSession = new VrLocomotionSession(VrOutputSinkFactory.CreateActive(), gameProfile); await _vrSession.StartAsync(token);
            var logFolder = Path.Combine(NiiMotionPaths.Logs, "live"); Directory.CreateDirectory(logFolder); StorageRetention.EnforceDirectoryBudget(logFolder);
            _diagnosticWriter = new StreamWriter(Path.Combine(logFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-psmove.csv"));
            _diagnosticWriter.WriteLine("elapsed_ticks;state;target_speed;confidence;cadence_hz;steps;phone_fresh;board_fresh;board_contact;turn_target");
            var pump = PumpPsMoveAsync(source, token); var output = RunPsMoveOutputLoopAsync(token);
            var workers = new List<Task> { pump, output };
            var critical = new List<Task> { pump, output };
            if (includePhone)
            {
                var phone = new OwoTrackSensorSource(); await phone.StartAsync(token); _sources.Add(phone); workers.Add(PumpPhoneAsync(phone, token));
            }
            if (includeBoard)
            {
                var board = new BalanceBoardSensorSource(); await board.StartAsync(token); _sources.Add(board); var boardPump = PumpBoardAsync(board, token); workers.Add(boardPump); critical.Add(boardPump);
            }
            workers.Add(MonitorCriticalSensorsAsync(critical, token)); _workers = workers.ToArray();
            ModeDescription = includeBoard && includePhone ? "PS MOVE + TELEFON + BOARD" : includeBoard ? "PS MOVE + BOARD" : includePhone ? "PS MOVE + TELEFON" : "SADECE PS MOVE — KİŞİSEL BALDIR PROFİLİ";
        }
        catch { await StopAsync(); throw; }
    }

    private async Task PumpPsMoveAsync(PsMoveSensorSource source, CancellationToken token)
    {
        await foreach (var sample in source.Samples.ReadAllAsync(token)) lock (_fusionLock) _psMoveGait!.Observe(sample);
        token.ThrowIfCancellationRequested(); throw new IOException("PS Move veri bağlantısı kesildi.");
    }

    private async Task RunPsMoveOutputLoopAsync(CancellationToken token)
    {
        var previous = Stopwatch.GetTimestamp(); using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (await timer.WaitForNextTickAsync(token))
        {
            var now = Stopwatch.GetTimestamp(); GaitSnapshot gait; lock (_fusionLock) gait = _psMoveGait!.Update(now);
            var auxiliary = _auxFusion!.Update(now);
            var target = gait.TargetSpeed;
            if (auxiliary.BoardFresh && !auxiliary.BoardContact) target = 0;
            if (auxiliary.TurnTarget != 0) target = 0;
            var snapshot = new FusionSnapshot(gait, gait.Confidence, target, auxiliary.PhoneFresh, auxiliary.BoardFresh, auxiliary.BoardContact, auxiliary.BoardTransferVelocity, auxiliary.TurnTarget, auxiliary.BoardCopX, auxiliary.BoardTotalKg);
            _diagnosticWriter?.WriteLine(string.Join(';', now, gait.State, target.ToString("0.000", CultureInfo.InvariantCulture), gait.Confidence.ToString("0.000", CultureInfo.InvariantCulture), gait.CadenceHz.ToString("0.000", CultureInfo.InvariantCulture), gait.StepCount, auxiliary.PhoneFresh, auxiliary.BoardFresh, auxiliary.BoardContact, auxiliary.TurnTarget.ToString("0.000", CultureInfo.InvariantCulture)));
            PublishTelemetry(now, gait, target, auxiliary.TurnTarget);
            var delta = TimeSpan.FromSeconds((now - previous) / (double)Stopwatch.Frequency); previous = now;
            await _vrSession!.UpdateAsync(snapshot, delta, token);
        }
    }

    public async Task StartAsync(string? calibrationPath = null, bool includePhone = true, bool phoneOnly = false, bool includeBoard = false, bool boardOnly = false, bool includePsMove = false, CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        var devices = HidDeviceEnumerator.FindJoyCons().GroupBy(x => x.Side).Select(x => x.First()).ToArray();
        var leftDescriptor = devices.FirstOrDefault(x => x.Side == JoyConSide.Left);
        var rightDescriptor = devices.FirstOrDefault(x => x.Side == JoyConSide.Right);
        if (!phoneOnly && !boardOnly && (leftDescriptor is null || rightDescriptor is null)) throw new InvalidOperationException("Sol ve sağ Joy-Con bağlı olmalı.");

        var threshold = await LoadThresholdAsync(calibrationPath, cancellationToken);
        var paceModelPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models", "deepgait-pace-v1.json");
        paceModelPath = Path.GetFullPath(paceModelPath);
        var pacePrior = File.Exists(paceModelPath) ? await GaitPacePrior.LoadAsync(paceModelPath, cancellationToken) : null;
        var personalPath = @"C:\NiirMotion\config\personal-gait-pace.json";
        var personalPace = File.Exists(personalPath) ? await PersonalGaitPace.LoadAsync(personalPath, cancellationToken) : null;
        var phoneProfilePath = @"C:\NiirMotion\config\personal-phone-motion.json";
        var phoneProfile = File.Exists(phoneProfilePath) ? await PersonalPhoneMotion.LoadAsync(phoneProfilePath, cancellationToken) : null;
        var boardProfilePath = @"C:\NiirMotion\config\personal-board-motion.json";
        var boardProfile = File.Exists(boardProfilePath) ? await PersonalBoardMotion.LoadAsync(boardProfilePath, cancellationToken) : null;
        _fusion = new SensorFusionEngine(threshold, pacePrior: pacePrior, personalPace: personalPace, phoneProfile: phoneProfile, boardProfile: boardProfile, allowPhoneOnly: phoneOnly, allowBoardOnly: boardOnly);
        if (includePsMove)
        {
            var onboarding = await new PsMoveOnboardingService().GetStatusAsync(cancellationToken);
            if (!onboarding.IsReady) throw new InvalidOperationException(onboarding.Instruction);
            var moveProfile = JsonSerializer.Deserialize<PsMoveTrainingProfile>(await File.ReadAllTextAsync(NiiMotionPaths.PsMoveTrainingProfile, cancellationToken)) ?? throw new InvalidDataException("PS Move kişisel profili okunamadı.");
            _psMoveGait = new PsMoveGaitEngine(moveProfile);
        }
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _lifetime.Token;

        try
        {
            JoyConSensorSource? left = null, right = null;
            if (!phoneOnly && !boardOnly)
            {
                left = new JoyConSensorSource(leftDescriptor!); right = new JoyConSensorSource(rightDescriptor!);
                _sources.Add(left); _sources.Add(right);
                await left.StartAsync(token); await right.StartAsync(token);
            }

            OwoTrackSensorSource? phone = null;
            if (includePhone)
            {
                try { phone = new OwoTrackSensorSource(); await phone.StartAsync(token); _sources.Add(phone); }
                catch { if (phone is not null) await phone.DisposeAsync(); phone = null; }
            }

            BalanceBoardSensorSource? board = null;
            if (includeBoard)
            {
                try { board = new BalanceBoardSensorSource(); await board.StartAsync(token); _sources.Add(board); }
                catch { if (board is not null) await board.DisposeAsync(); board = null; }
            }

            var gameProfile = new GameMotionProfileStore().LoadActive();
            var selectedGame = new GameSelectionStore().Load();
            var selectedMotionProfile = new ActiveMotionProfileStore().Load() ?? "joycon-only";
            var optimization = new GameSensorOptimizationStore().Load(selectedGame, selectedMotionProfile);
            gameProfile = gameProfile with { SpeedMultiplier = gameProfile.SpeedMultiplier * optimization.DistanceScale };
            _vrSession = new VrLocomotionSession(VrOutputSinkFactory.CreateActive(), gameProfile);
            await _vrSession.StartAsync(token);
            var logFolder = @"C:\NiirMotion\logs\live"; Directory.CreateDirectory(logFolder);
            StorageRetention.EnforceDirectoryBudget(logFolder);
            _diagnosticWriter = new StreamWriter(Path.Combine(logFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv"));
            _diagnosticWriter.WriteLine("elapsed_ticks;state;target_speed;turn_target;confidence;cadence_hz;steps;phone_fresh;board_contact;board_cop_x;board_total_kg;board_transfer_velocity");

            var outputLoop = RunOutputLoopAsync(token);
            var jobs = new List<Task> { outputLoop };
            var criticalSensors = new List<Task> { outputLoop };
            if (left is not null && right is not null)
            {
                var leftPump = PumpLegAsync(left, LegSide.Left, token); var rightPump = PumpLegAsync(right, LegSide.Right, token);
                jobs.Add(leftPump); jobs.Add(rightPump); criticalSensors.Add(leftPump); criticalSensors.Add(rightPump);
            }
            if (includePsMove)
            {
                var moves = new PsMoveSensorSource(NiiMotionPaths.PsMoveAssignments, NiiMotionPaths.PsMoveFactoryCalibration); _sources.Add(moves); await moves.StartAsync(token);
                var movePump = PumpPsMoveAsync(moves, token); jobs.Add(movePump); criticalSensors.Add(movePump);
            }
            if (phone is not null) jobs.Add(PumpPhoneAsync(phone, token));
            if (board is not null) jobs.Add(PumpBoardAsync(board, token));
            if (criticalSensors.Count > 0) jobs.Add(MonitorCriticalSensorsAsync(criticalSensors, token));
            _workers = jobs.ToArray();
            var sensors = boardOnly ? "SADECE BALANCE BOARD — DENEYSEL" : phoneOnly && board is not null ? "BALANCE BOARD + TELEFON — DENEYSEL" : phoneOnly ? "SADECE TELEFON — DENEYSEL" : includePsMove && board is not null && phone is not null ? "JOY-CON + PS MOVE + TELEFON + BOARD" : includePsMove && board is not null ? "JOY-CON + PS MOVE + BOARD" : includePsMove && phone is not null ? "JOY-CON + PS MOVE + TELEFON" : includePsMove ? "JOY-CON + PS MOVE" : board is not null && phone is null ? "BALANCE BOARD + JOY-CON" : board is not null ? "JOY-CON + TELEFON + BOARD" : phone is null ? "SADECE JOY-CON" : "JOY-CON + TELEFON";
            ModeDescription = personalPace is not null ? $"{sensors} — KİŞİSEL HIZ" : pacePrior is null ? $"{sensors} — SAFE HEURISTIC" : $"{sensors} — DEEPGAIT PACE";
        }
        catch { await StopAsync(); throw; }
    }

    private async Task PumpLegAsync(JoyConSensorSource source, LegSide side, CancellationToken token)
    {
        await foreach (var sample in source.Samples.ReadAllAsync(token)) lock (_fusionLock) _fusion!.ObserveLeg(side, sample.AngularVelocityDps.Length(), sample.Timestamp.MonotonicTicks);
        token.ThrowIfCancellationRequested();
        throw new IOException($"{(side == LegSide.Left ? "Sol" : "Sağ")} Joy-Con veri bağlantısı kesildi.");
    }

    private async Task MonitorCriticalSensorsAsync(IReadOnlyCollection<Task> sensors, CancellationToken token)
    {
        try
        {
            var ended = await Task.WhenAny(sensors);
            await ended;
            if (!token.IsCancellationRequested) throw new IOException("Joy-Con veri akışı beklenmedik şekilde durdu.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            _lifetime?.Cancel();
            CriticalSensorLost?.Invoke(this, ex.GetBaseException().Message);
        }
    }

    private async Task PumpPhoneAsync(OwoTrackSensorSource source, CancellationToken token)
    {
        await foreach (var sample in source.Samples.ReadAllAsync(token))
        {
            var body = PhoneMounting.ToBodyFrame(sample);
            lock (_fusionLock) (_fusion ?? _auxFusion)!.ObservePhoneMotion(body.AngularVelocityRadps.Length(), body.AccelerationMps2.Length(), sample.Timestamp.MonotonicTicks, body.VerticalTurnRadps);
        }
    }

    private async Task PumpBoardAsync(BalanceBoardSensorSource source, CancellationToken token)
    {
        await foreach (var sample in source.Samples.ReadAllAsync(token))
            lock (_fusionLock) (_fusion ?? _auxFusion)!.ObserveBoard(sample);
    }

    private async Task RunOutputLoopAsync(CancellationToken token)
    {
        var previous = Stopwatch.GetTimestamp();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (await timer.WaitForNextTickAsync(token))
        {
            var now = Stopwatch.GetTimestamp(); FusionSnapshot snapshot;
            lock (_fusionLock)
            {
                snapshot = _fusion!.Update(now);
                if (_psMoveGait is not null)
                {
                    var move = _psMoveGait.Update(now);
                    snapshot = HybridGaitFusion.Combine(snapshot, move);
                }
            }
            _diagnosticWriter?.WriteLine(string.Join(';', now, snapshot.Gait.State, snapshot.TargetSpeed.ToString("0.000", CultureInfo.InvariantCulture), snapshot.TurnTarget.ToString("0.000", CultureInfo.InvariantCulture), snapshot.GlobalConfidence.ToString("0.000", CultureInfo.InvariantCulture), snapshot.Gait.CadenceHz.ToString("0.000", CultureInfo.InvariantCulture), snapshot.Gait.StepCount, snapshot.PhoneFresh, snapshot.BoardContact, snapshot.BoardCopX.ToString("0.000", CultureInfo.InvariantCulture), snapshot.BoardTotalKg.ToString("0.0", CultureInfo.InvariantCulture), snapshot.BoardTransferVelocity.ToString("0.000", CultureInfo.InvariantCulture)));
            PublishTelemetry(now, snapshot.Gait, snapshot.TargetSpeed, snapshot.TurnTarget);
            if (now % Stopwatch.Frequency < Stopwatch.Frequency / 100) _diagnosticWriter?.Flush();
            var delta = TimeSpan.FromSeconds((now - previous) / (double)Stopwatch.Frequency); previous = now;
            await _vrSession!.UpdateAsync(snapshot, delta, token);
        }
    }

    private void PublishTelemetry(long now, GaitSnapshot gait, double targetSpeed, double turnTarget)
    {
        if (now - Interlocked.Read(ref _lastTelemetryEventTicks) < Stopwatch.Frequency / 10) return;
        Interlocked.Exchange(ref _lastTelemetryEventTicks, now);
        TelemetryUpdated?.Invoke(this, new(now, gait.StepCount, targetSpeed, gait.CadenceHz, turnTarget, gait.State != GaitState.Idle));
    }

    public async Task StopAsync()
    {
        var lifetime = _lifetime; _lifetime = null;
        if (lifetime is not null) { lifetime.Cancel(); try { await Task.WhenAll(_workers); } catch { /* Sensor fault is reported separately; shutdown must remain deterministic. */ } lifetime.Dispose(); }
        _workers = [];
        if (_vrSession is not null) { await _vrSession.DisposeAsync(); _vrSession = null; }
        foreach (var source in _sources.AsEnumerable().Reverse()) await source.DisposeAsync();
        _diagnosticWriter?.Flush(); _diagnosticWriter?.Dispose(); _diagnosticWriter = null;
        _sources.Clear(); _fusion = null; _auxFusion = null; _psMoveGait = null; ModeDescription = "OFF";
    }

    private static async Task<double> LoadThresholdAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 56;
        await using var stream = File.OpenRead(path);
        var calibrated = (await new CalibrationStore().LoadAsync(stream, cancellationToken)).RecommendedLegThresholdDps;
        // The calibration threshold was intentionally conservative. The isolated
        // leg capture shows real swings spend much of their time just below it;
        // retain its noise adaptation while moving the trigger into that range.
        return Math.Clamp(calibrated * 0.70, 50, 70);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

public sealed record LocomotionTelemetrySample(long MonotonicTicks, long StepCount, double TargetSpeed, double CadenceHz, double TurnTarget, bool IsMoving);
