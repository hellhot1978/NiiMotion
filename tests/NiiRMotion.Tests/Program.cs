using System.Numerics;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;
var releaseManifestArg = args.FirstOrDefault(x => x.StartsWith("--release-manifest=", StringComparison.OrdinalIgnoreCase));
if (releaseManifestArg is not null)
{
    var root = Path.GetFullPath(releaseManifestArg[(releaseManifestArg.IndexOf('=') + 1)..]);
    var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var project = File.ReadAllText(Path.Combine(repository, "src", "NiiRMotion.App", "NiiRMotion.App.csproj"));
    var version = Regex.Match(project, "<Version>([^<]+)</Version>", RegexOptions.CultureInvariant).Groups[1].Value;
    if (!Version.TryParse(version, out _)) throw new InvalidDataException("Application version is missing or invalid.");
    var manifest = ReleaseIntegrityService.Create(root, version); ReleaseIntegrityService.Save(manifest, Path.Combine(root, "release-integrity.json"));
    Console.WriteLine($"Release integrity manifest: {manifest.Files.Count} files"); return manifest.Files.Count > 0 ? 0 : 2;
}
if (args.Contains("--hardware-smoke", StringComparer.OrdinalIgnoreCase)) return await HardwareSmokeAsync();
if (args.Contains("--hid-paths", StringComparer.OrdinalIgnoreCase))
{
    foreach (var path in HidDeviceEnumerator.FindAllHidPaths()) Console.WriteLine(path);
    return 0;
}
if (args.Contains("--hardware-status", StringComparer.OrdinalIgnoreCase))
{
    foreach (var status in await new HardwareDiscoveryService().ScanAsync())
        Console.WriteLine($"{status.Kind}: {status.State} | {status.Detail}");
    return 0;
}
if (args.Contains("--psmove-discovery", StringComparer.OrdinalIgnoreCase))
{
    var probes = new PsMoveDiagnosticsService().Discover();
    Console.WriteLine($"Detected PS Move CECH-ZCM1 controllers: {probes.Count}");
    foreach (var probe in probes)
    {
        Console.WriteLine($"{probe.Device.Model} | {probe.Device.Transport} | ID: {probe.Device.StableId ?? "unknown"} | HID open: {probe.Opened} | reports in/out/feature: {probe.InputReportBytes}/{probe.OutputReportBytes}/{probe.FeatureReportBytes}");
        Console.WriteLine(probe.Device.DevicePath);
        if (!probe.Opened) Console.WriteLine(probe.Detail);
    }
    return 0;
}
if (args.Contains("--psmove-dual", StringComparer.OrdinalIgnoreCase))
{
    var captures = await new PsMoveDiagnosticsService().CaptureAllInputReportsAsync(TimeSpan.FromSeconds(3));
    Console.WriteLine($"PS Move live streams: {captures.Count}");
    foreach (var capture in captures)
        Console.WriteLine($"{capture.Device.StableId ?? "unknown"} | {capture.Device.Transport} | {capture.ReportCount} reports | {capture.DistinctReportCount} distinct | ID 0x{capture.ReportId:X2}");
    return captures.Count >= 2 && captures.All(x => x.ReportCount > 0) ? 0 : 2;
}
if (args.Contains("--psmove-identify", StringComparer.OrdinalIgnoreCase))
{
    const uint moveButton = 1u << 19;
    Console.WriteLine("Waiting 20 seconds for the large Move button...");
    var device = await new PsMoveDiagnosticsService().WaitForButtonAsync(moveButton, TimeSpan.FromSeconds(20));
    if (device is null)
    {
        Console.WriteLine("No Move button press detected.");
        return 2;
    }
    Console.WriteLine($"Move button controller: {device.StableId ?? device.DevicePath}");
    return 0;
}
var moveAssignmentArg = args.FirstOrDefault(x => x.StartsWith("--psmove-assign=", StringComparison.OrdinalIgnoreCase));
if (moveAssignmentArg is not null)
{
    var ids = moveAssignmentArg[(moveAssignmentArg.IndexOf('=') + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (ids.Length != 2) return 2;
    var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "psmove-assignments.json"));
    var store = new PsMoveAssignmentStore(path);
    await store.SaveAsync(ids[0], ids[1]);
    Console.WriteLine($"PS Move assignment saved: LEFT {ids[0]} | RIGHT {ids[1]}");
    return 0;
}
if (args.Contains("--psmove-colors", StringComparer.OrdinalIgnoreCase))
{
    var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "psmove-assignments.json"));
    var assignments = await new PsMoveAssignmentStore(path).LoadAsync();
    if (assignments is not { IsComplete: true }) return 2;
    Console.WriteLine("LEFT red / RIGHT blue for 8 seconds; rumble is off.");
    await new PsMoveDiagnosticsService().ShowAssignmentColorsAsync(assignments, TimeSpan.FromSeconds(8));
    return 0;
}
if (args.Contains("--psmove-calibration-usb", StringComparer.OrdinalIgnoreCase))
{
    var capture = new PsMoveDiagnosticsService().ReadUsbFactoryCalibration();
    if (capture is null)
    {
        Console.WriteLine("No PS Move CECH-ZCM1 USB device is connected.");
        return 2;
    }
    Console.WriteLine($"PS Move factory calibration: {capture.Blob.Length} bytes");
    Console.WriteLine(Convert.ToHexString(capture.Blob));
    return capture.Blob.Length == 143 ? 0 : 2;
}
var saveMoveCalibrationArg = args.FirstOrDefault(x => x.StartsWith("--psmove-save-calibration=", StringComparison.OrdinalIgnoreCase));
if (saveMoveCalibrationArg is not null)
{
    var side = saveMoveCalibrationArg[(saveMoveCalibrationArg.IndexOf('=') + 1)..];
    var assignmentPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "psmove-assignments.json"));
    var calibrationPath = Path.Combine(Path.GetDirectoryName(assignmentPath)!, "psmove-calibrations.json");
    var assignments = await new PsMoveAssignmentStore(assignmentPath).LoadAsync();
    if (assignments is not { IsComplete: true }) return 2;
    var stableId = side.Equals("left", StringComparison.OrdinalIgnoreCase) ? assignments.LeftStableId : side.Equals("right", StringComparison.OrdinalIgnoreCase) ? assignments.RightStableId : "";
    if (string.IsNullOrEmpty(stableId)) return 2;
    var capture = new PsMoveDiagnosticsService().ReadUsbFactoryCalibration();
    if (capture is null) return 2;
    await new PsMoveCalibrationStore(calibrationPath).SaveAsync(stableId, side, capture.Blob);
    var parsed = PsMoveZcm1FactoryCalibration.Parse(capture.Blob);
    Console.WriteLine($"Saved {side} PS Move calibration for {stableId}: accel low {parsed.AccelerationLow}, high {parsed.AccelerationHigh}, gyro scale {parsed.GyroscopeRadiansPerSecondPerUnit}");
    return 0;
}
if (args.Contains("--psmove-health", StringComparer.OrdinalIgnoreCase))
{
    var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "psmove-calibrations.json"));
    var stored = await new PsMoveCalibrationStore(path).LoadAsync();
    var health = await new PsMoveDiagnosticsService().CaptureCalibratedHealthAsync(stored, TimeSpan.FromSeconds(5));
    Console.WriteLine($"Calibrated PS Move streams: {health.Count}");
    foreach (var item in health)
        Console.WriteLine($"{item.StableId} | {item.ReportRateHz:F1} Hz | jitter {item.JitterMs:F2} ms | loss {item.MissingReports} | battery 0x{item.Battery:X2} | accel {item.MinimumAccelerationG:F2}-{item.MaximumAccelerationG:F2} g | gyro max {item.MaximumAngularVelocityRadPerSecond:F2} rad/s");
    return health.Count == 2 && health.All(x => x.ReportCount > 0) ? 0 : 2;
}
if (args.Contains("--psmove-source-smoke", StringComparer.OrdinalIgnoreCase))
{
    var configRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config"));
    await using var source = new PsMoveSensorSource(Path.Combine(configRoot, "psmove-assignments.json"), Path.Combine(configRoot, "psmove-calibrations.json"));
    await source.StartAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var counts = new Dictionary<LegSide, int>();
    while (counts.Values.Sum() < 500 && await source.Samples.WaitToReadAsync(timeout.Token))
        while (source.Samples.TryRead(out var sample)) counts[sample.Side] = counts.GetValueOrDefault(sample.Side) + 1;
    Console.WriteLine(string.Join(" | ", counts.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value} calibrated calf samples")));
    return counts.GetValueOrDefault(LegSide.Left) > 0 && counts.GetValueOrDefault(LegSide.Right) > 0 ? 0 : 2;
}
if (args.Contains("--psmove-analyze", StringComparer.OrdinalIgnoreCase))
{
    var root = @"C:\NiirMotion\data\psmove";
    var profile = await new PsMoveTrainingAnalyzer().AnalyzeAsync(root);
    await PsMoveTrainingAnalyzer.SaveAsync(profile, @"C:\NiirMotion\config\personal-psmove-training.json");
    Console.WriteLine(JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
if (args.Contains("--psmove-replay", StringComparer.OrdinalIgnoreCase))
{
    var profile = JsonSerializer.Deserialize<PsMoveTrainingProfile>(await File.ReadAllTextAsync(@"C:\NiirMotion\config\personal-psmove-training.json"))!;
    var results = await PsMoveReplayValidator.ValidateAsync(@"C:\NiirMotion\data\psmove", profile);
    foreach (var result in results) Console.WriteLine($"{result.Label}: {result.ActiveRatio:P1} active ({result.ActiveSamples}/{result.Samples})");
    return 0;
}
if (args.Contains("--psmove-raw", StringComparer.OrdinalIgnoreCase))
{
    var capture = await new PsMoveDiagnosticsService().CaptureInputReportsAsync(TimeSpan.FromSeconds(3));
    if (capture is null)
    {
        Console.WriteLine("No PS Move input-report collection is available.");
        return 2;
    }
    Console.WriteLine($"PS Move raw capture: {capture.Device.Transport}, {capture.ReportCount} reports, {capture.DistinctReportCount} distinct, ID 0x{capture.ReportId:X2}, {capture.ReportBytes} bytes");
    Console.WriteLine($"First report: {capture.FirstReportHex}");
    if (capture.ReportBytes == PsMoveZcm1ReportParser.InputReportBytes)
    {
        var parsed = PsMoveZcm1ReportParser.Parse(Convert.FromHexString(capture.FirstReportHex));
        Console.WriteLine($"Parsed: seq {parsed.Sequence}, battery 0x{parsed.Battery:X2}, trigger {parsed.Trigger}, accel2 {parsed.LatestSample.Acceleration}, gyro2 {parsed.LatestSample.AngularVelocity}, magnet {parsed.Magnetometer}");
    }
    return capture.ReportCount > 0 ? 0 : 2;
}
if (args.Contains("--board-discovery", StringComparer.OrdinalIgnoreCase))
{
    var boards = HidDeviceEnumerator.FindBalanceBoards();
    Console.WriteLine($"Detected Balance Boards: {boards.Count}");
    foreach (var board in boards) Console.WriteLine(board);
    return boards.Count > 0 ? 0 : 2;
}
if (args.Contains("--board-live", StringComparer.OrdinalIgnoreCase))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var result = await new BalanceBoardDiagnosticsService().RunAsync(100, timeout.Token);
    Console.WriteLine($"Balance Board live: {result.SampleCount} samples, {result.MinimumWeightKg:F2}-{result.MaximumWeightKg:F2} kg, last {result.LastWeightKg:F2} kg, {result.ExtensionType}");
    return 0;
}
var boardMeasureArg = args.FirstOrDefault(x => x.StartsWith("--board-measure=", StringComparison.OrdinalIgnoreCase));
if (boardMeasureArg is not null)
{
    var label = boardMeasureArg[(boardMeasureArg.IndexOf('=') + 1)..];
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var result = await new BalanceBoardMeasurementService().CaptureAsync(label, TimeSpan.FromSeconds(8), timeout.Token);
    Console.WriteLine(JsonSerializer.Serialize(result));
    return 0;
}
if (args.Contains("--capture-phone", StringComparer.OrdinalIgnoreCase)) return await CapturePhoneAsync();
if (args.Contains("--owotrack-smoke", StringComparer.OrdinalIgnoreCase)) return await OwoTrackSmokeAsync();
if (args.Contains("--gait-calibration", StringComparer.OrdinalIgnoreCase)) return await GaitCalibrationAsync();
if (args.Contains("--motion-validation", StringComparer.OrdinalIgnoreCase)) return await MotionValidationAsync();
if (args.Contains("--replay-motion-validation", StringComparer.OrdinalIgnoreCase)) return await ReplayMotionValidationAsync();
if (args.Contains("--walk-tuning-capture", StringComparer.OrdinalIgnoreCase)) return await WalkTuningCaptureAsync();
if (args.Contains("--leg-balance-capture", StringComparer.OrdinalIgnoreCase)) return await LegBalanceCaptureAsync();
if (args.Contains("--vr-output-smoke", StringComparer.OrdinalIgnoreCase)) return await VrOutputSmokeAsync();
if (args.Contains("--vr-output-forward-test", StringComparer.OrdinalIgnoreCase)) return await VrOutputForwardTestAsync();
if (args.Contains("--vr-pace-simulation", StringComparer.OrdinalIgnoreCase)) return await VrPaceSimulationAsync();
if (args.Contains("--vr-pace-simulation-short", StringComparer.OrdinalIgnoreCase)) return await VrPaceSimulationAsync(200);
if (args.Contains("--vr-straight-drift-test", StringComparer.OrdinalIgnoreCase)) return await VrStraightDriftTestAsync();
var tests = new (string Name, Func<Task> Run)[] { Sync("Hardware inventory creates every available profile combination", HardwareInventoryProfiles), Sync("Required device missing blocks session", RequiredMissingBlocks), Sync("Optional device missing degrades session", OptionalMissingDegrades), Sync("All devices ready", AllReady), Sync("Classic VR disables locomotion", ClassicVrIsNonInvasive), Sync("Joy-Con identity rejects clones", JoyConIdentity), Sync("PS Move identity accepts only ZCM1", PsMoveIdentity), Sync("PS Move ZCM1 input report parses", PsMoveInputReport), Sync("PS Move factory calibration maps sensor units", PsMoveFactoryCalibration), Sync("PS Move training creates distinct pace anchors", PsMoveTrainingProfileTest), ("PS Move assignments persist by stable identity", PsMoveAssignmentsRoundTrip), Sync("Joy-Con report parses three IMU samples", ParseImu), Sync("Invalid report is rejected", InvalidReport), Sync("Factory calibration parses", ParseCalibration), Sync("Factory calibration scales IMU", ScaleCalibration), Sync("Phone sequence loss is measured", PhoneLoss), Sync("owoTrack big-endian rotation parses", OwoRotation), Sync("Balance Board derives load and CoP", BalanceBoardDerivesCop), Sync("Balance Board protocol parses and calibrates", BalanceBoardProtocol), Sync("Board hold gesture turns without walking", BoardHoldGestureTurns), Sync("Board walking does not become a turn", BoardWalkingDoesNotTurn), Sync("Torso motion alone never starts locomotion", TorsoCannotStart), Sync("Experimental phone-only requires sustained motion", ExperimentalPhoneOnlyIsExplicitlyGated), Sync("Bilateral crouch motion resets gait confidence", BilateralMotionRejected), Sync("Single leg movement does not become walking", SingleLegRejected), Sync("Alternating leg evidence starts gait", AlternatingLegsStart), Sync("Natural cadence stays continuous and stops promptly", NaturalCadenceContinuity), Sync("Threshold hysteresis rejects sensor chatter", ThresholdHysteresisRejectsChatter), Sync("Stronger thigh swings produce a faster natural pace", SwingAmplitudeControlsPace), ("Learned DeepGait pace prior loads and scales", LearnedPacePrior), Sync("Gait stops after stale leg data", GaitStops), Sync("Optional fusion evidence cannot create gait", OptionalFusionCannotStart), Sync("Stale optional sensors degrade without blocking gait", StaleOptionalSensorsDoNotBlock), Sync("Analog output is smoothed", SpeedIsSmoothed), ("VR session starts promptly, stays straight and stops promptly", VrSessionResponseContract), Sync("Calibration rejects incomplete capture", CalibrationRejectsIncomplete), ("Calibration is versioned and round-trips", CalibrationRoundTrip), ("Personal gait records analyze and apply", PersonalGaitAnalysisRoundTrip), ("Recording round-trips through replay", RecordingRoundTrip), ("Balance Board recording round-trips", BalanceBoardRecordingRoundTrip), ("Phone UDP listener validates token", PhoneUdpRoundTrip), ("Live log retention enforces its disk budget", LogRetentionEnforcesBudget), Sync("Alyx physical forward override preserves controller buttons", AlyxPhysicalForwardOverride), Sync("Arizona 2 movement override preserves controller buttons", Arizona2PhysicalMovementOverride), ("VR output starts at zero and clamps analog values", VrOutputLifecycle), ("VR output refuses movement while off", VrOutputOffRejects), ("VR output failure detaches safely", VrOutputFailureDetaches), ("Fused gait drives analog output and stops safely", FusedGaitDrivesOutput), ("Board turn drives horizontal output only", BoardTurnDrivesHorizontalOutput), ("Named-pipe output packet matches native protocol", NamedPipeOutputProtocol), Sync("Native OpenVR DLL exports driver factory", NativeDriverExportsFactory), Sync("Native treadmill publishes an active stationary pose", NativeDriverPoseContract), Sync("Alyx binding includes treadmill vector and walk activation", AlyxBindingContract), Sync("Arizona Sunshine 2 binding includes movement vector", Arizona2BindingContract), ("HMD pose recording round-trips", HmdPoseRoundTrip), Sync("Four-hour accelerated endurance stays safe", EnduranceSimulationTest) };
tests = [Sync("Validated HMD suppresses only weak false forward turns", HmdFusionPolicyTest), Sync("HMD validation quality rejects weak captures", HmdValidationQualityTest), ("Live HMD shared pose source reads tracked pose", LiveHmdSharedPoseSource), Sync("Fresh HMD tracking state is authoritative", FreshHmdTrackingState), Sync("Game telemetry provider selects direct and universal modes", GameTelemetryProviderSelection), Sync("Universal game feedback is bounded and reversible", GuidedGameOptimization), Sync("Alyx console position telemetry parses exactly", AlyxPositionTelemetryParser), Sync("Game sensor optimization is isolated and reversible", GameSensorOptimizationTest), ("Calibration repair replaces only broken segment", CalibrationSegmentRepair), Sync("Calibration quality isolates broken time segments", CalibrationQualitySegments), Sync("Game motion profile is versioned and bounded", GameMotionProfileTest), Sync("Game adapter validates safe SteamVR actions", GameAdapterValidation), Sync("Game adapter restore returns original profile", GameAdapterRestoreTest), Sync("Steam action discovery finds movement inputs", SteamActionDiscoveryTest), Sync("Steam game catalog detects only real manifests", SteamGameCatalogDetection), ("Unified sensor replay preserves timestamp order", UnifiedSensorReplayOrder), Sync("Calibration progress schema separates devices and profiles", CalibrationProgressSchema), Sync("Hybrid leg sensors reward agreement and degrade disagreement", HybridLegAgreement), .. tests];
tests = [Sync("Learned data reset backs up motion data and preserves device identity", LearnedDataResetTest), .. tests];
tests = [Sync("Session health report is bounded and privacy-safe", SessionHealthReportTest), Sync("Diagnostic package redacts private network and device identities", DiagnosticRedactionTest), .. tests];
tests = [("OpenXR shared output packet is bounded and process-scoped", OpenXrSharedOutputTest), Sync("OpenXR layer manifest and native export are valid", OpenXrLayerContract), .. tests];
tests = [Sync("Configured hand control is not reported missing", ConfiguredHandControl), .. tests];
tests = [Sync("Non-HMD profile regression matrix passes", NonHmdMatrix), .. tests];
tests = [Sync("Workspace maintenance enforces recursive cache budget", WorkspaceMaintenanceBudget), .. tests];
tests = [Sync("Game launch journal is atomic and recoverable", GameLaunchJournal), Sync("SteamVR dashboard overlay binary and contract are valid", VrDashboardOverlayContract), Sync("Application safety detects an unclean session", ApplicationSafetyMarker), Sync("Configuration migration backs up and preserves JSON", ConfigurationMigration), Sync("VR panel state packet round-trips", VrPanelPacket), .. tests];
tests = [Sync("Generic OpenXR adapter is validated and persisted", GenericOpenXrAdapter), .. tests];
tests = [Sync("First-use preferences and guidance are deterministic", FirstUsePreferences), Sync("Successful game validation is remembered locally", GameValidationReceipt), .. tests];
tests = [("Update download is hash verified before staging", UpdateDownloadVerification), Sync("Release integrity detects tampering", ReleaseIntegrityVerification), .. tests];
tests = [Sync("Installer preserves personal data and unregisters VR components", InstallerSafetyContract), .. tests];
tests = [Sync("Release candidate pipeline is complete and remains manual", ReleaseCandidatePipelineContract), .. tests];
tests = [Sync("Hardware validation uses the current combined locomotion engine", CombinedHardwareValidationContract), .. tests];
tests = [Sync("VR panel commands are delivered once", VrPanelCommands), Sync("OpenXR wizard prioritizes common engine executables", OpenXrEngineDiscovery), .. tests];
tests = [Sync("Static UI text has English localization coverage", StaticUiLocalizationCoverage), .. tests];
tests = [Sync("Dynamic UI status messages have English localization coverage", DynamicUiLocalizationCoverage), .. tests];
tests = [Sync("Every app message box uses the English localization gate", MessageBoxLocalizationContract), .. tests];
tests = [Sync("AI agent handoff is model-independent and safety-complete", AiAgentHandoffContract), Sync("Hardware acceptance matrix covers every release scenario", HardwareAcceptanceMatrixContract), Sync("Repository CI runs the canonical verification gate", ContinuousIntegrationContract), Sync("Standalone package contains local models and has no AI runtime dependency", StandaloneRuntimeContract), .. tests];
tests = [Sync("Every motion device declares its software requirement", DeviceSoftwareGuidanceContract), .. tests];
tests = [Sync("PS Move pairing has a verified offline bundle", PsMoveOfflineBundleContract), .. tests];
tests = [Sync("Game launch compatibility blocks broken adapters locally", GameLaunchCompatibilityContract), .. tests];
tests = [Sync("Sensor loss offers only an explicit safe fallback profile", SafeProfileFallback), .. tests];
tests = [Sync("Standalone readiness detects and repairs only local prerequisites", StandaloneReadiness), .. tests];
tests = [("Completed calibration requires a valid local runtime model", CalibrationModelReadiness), .. tests];
var failures = new List<string>();
foreach (var test in tests) { try { await test.Run(); Console.WriteLine($"PASS  {test.Name}"); } catch (Exception ex) { failures.Add($"FAIL  {test.Name}: {ex.Message}"); } }
foreach (var failure in failures) Console.Error.WriteLine(failure); Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed."); return failures.Count == 0 ? 0 : 1;

static void StaticUiLocalizationCoverage()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var localization = File.ReadAllText(Path.Combine(root, "src", "NiiRMotion.App", "UiLocalization.cs"));
    var keys = Regex.Matches(localization, "\\[\\\"((?:[^\\\"\\\\]|\\\\.)*)\\\"\\]\\s*=", RegexOptions.CultureInvariant)
        .Select(match => Regex.Unescape(match.Groups[1].Value)).ToArray();
    var neutral = new HashSet<string>(StringComparer.Ordinal)
    {
        "—", "…", "↻", "+", "●", "✓", "0", "0.0 kg", "0.00", "00:00", "00:00 / 05:00", "05:00",
        "2× Joy-Con", "2× PS Move", "Balance Board", "BOARD + JOY-CON", "HIZ 0.00", "JOY-CON + TELEFON",
        "Joy-Con + telefon + Balance Board", "m/sn²", "Meta Quest 3", "NATURAL VR LOCOMOTION", "NiiMotion",
        "Normal VR", "rad/sn", "TÜM CİHAZLAR", "Wii Balance Board", "Android telefon"
    };
    var attributePattern = new Regex("(?:Text|Content|ToolTip|Title)=\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant);
    var missing = new List<string>();
    foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src", "NiiRMotion.App"), "*.xaml", SearchOption.AllDirectories))
    {
        foreach (Match match in attributePattern.Matches(File.ReadAllText(path)))
        {
            var value = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith('{') || neutral.Contains(value)) continue;
            if (keys.Any(key => value.Equals(key, StringComparison.Ordinal) || value.EndsWith(key, StringComparison.Ordinal))) continue;
            missing.Add($"{Path.GetFileName(path)}: {value}");
        }
    }
    Assert(missing.Count == 0, "Missing English UI translations: " + string.Join(" | ", missing.Distinct()));
}

static void DynamicUiLocalizationCoverage()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var localization = File.ReadAllText(Path.Combine(root, "src", "NiiRMotion.App", "UiLocalization.cs"));
    var required = new[]
    {
        "Quest ve Virtual Desktop oturumu bekleniyor", "SteamVR ve NiiMotion hareket köprüsü doğrulanıyor",
        "Kişisel hareket modeli başlatılıyor", "Faz tamamlanmadı", "Kayıt tamamlanmadı",
        "Başlatma durduruldu", "Locomotion başlatılamadı", "sensör örneği alındı",
        "Sadece PS Move", "BAĞLANTI GEREKİYOR", "EKSİK CİHAZLARI BAĞLA", "Sağ PS Move",
        "En güçlü bacak doğrulaması", "Doğal yerinde yürüyüş", "SANA UYGUN PROFİLLER"
    };
    foreach (var phrase in required)
        Assert(localization.Contains(phrase, StringComparison.Ordinal), $"Dynamic English translation rule is missing: {phrase}");
    Assert(localization.Contains("Regex.Replace(result", StringComparison.Ordinal), "Parameterized runtime messages are not localized by templates.");
    Assert(localization.Contains("value.EndsWith(pair.Key", StringComparison.Ordinal), "Icon-decorated runtime status text is not localized.");
    Assert(localization.Contains("EnsureEnglish(Translate(value))", StringComparison.Ordinal), "Runtime UI text does not pass through the English localization gate.");
    Assert(localization.Contains("GeneratedTemplates", StringComparison.Ordinal), "Parameterized runtime messages are not covered by the generated English catalog.");
    Assert(!localization.Contains("Information unavailable in English.", StringComparison.Ordinal), "The UI still contains the temporary unavailable-English placeholder.");
}

static void MessageBoxLocalizationContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var app = Path.Combine(root, "src", "NiiRMotion.App");
    var violations = Directory.EnumerateFiles(app, "*.cs", SearchOption.TopDirectoryOnly)
        .Where(path => !Path.GetFileName(path).Equals("UiLocalization.cs", StringComparison.OrdinalIgnoreCase))
        .Where(path => File.ReadAllText(path).Contains("MessageBox.Show(", StringComparison.Ordinal))
        .Select(Path.GetFileName)
        .ToArray();
    Assert(violations.Length == 0, "Message boxes bypass English localization: " + string.Join(", ", violations));
}

static void SafeProfileFallback()
{
    var inventory = new UserHardwareInventory(1, true, true, true, false, false, DateTimeOffset.UtcNow);
    var profiles = MotionProfileCatalog.For(inventory);
    var selected = profiles.Single(x => x.Profile.Name == "Joy-Con + PS Move + Telefon").Profile;
    var connected = selected.Required.Select(kind => new DeviceStatus(kind, kind.ToString(), kind is DeviceKind.PsMoveLeft or DeviceKind.PsMoveRight or DeviceKind.Phone ? DeviceState.Missing : DeviceState.Connected, "", "")).ToArray();
    var fallback = ProfileFallbackAdvisor.Find(selected, profiles, connected);
    Assert(fallback?.Profile.Name == "Sadece Joy-Con", "The strongest connected safe subset was not offered.");
    Assert(selected.Name == "Joy-Con + PS Move + Telefon", "Fallback selection changed the active profile silently.");
    var allReady = selected.Required.Select(kind => new DeviceStatus(kind, kind.ToString(), DeviceState.Connected, "", "")).ToArray();
    Assert(ProfileFallbackAdvisor.Find(selected, profiles, allReady) is null, "A fallback was offered although the selected profile was ready.");
}

static void StandaloneReadiness()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-standalone-" + Guid.NewGuid().ToString("N"));
    var app = Path.Combine(root, "app"); var state = Path.Combine(root, "state"); var models = Path.Combine(app, "Models");
    try
    {
        Directory.CreateDirectory(app); Directory.CreateDirectory(models);
        var service = new StandaloneReadinessService(app, state, models);
        var missing = service.Inspect(); Assert(!missing.IsReady, "An incomplete standalone package was reported ready.");
        foreach (var file in new[] { "coreclr.dll", "hostfxr.dll", Path.Combine("OpenVRDriver", "driver.vrdrivermanifest"), Path.Combine("OpenVRDriver", "bin", "win64", "driver_niirmotion.dll"), Path.Combine("OpenXRLayer", "niirmotion_openxr.json"), Path.Combine("OpenXRLayer", "bin", "win64", "niirmotion_openxr.dll"), Path.Combine("VrOverlay", "NiiMotion.VrOverlay.exe"), Path.Combine("VrOverlay", "openvr_api.dll"), Path.Combine("VrOverlay", "niirmotion.vrmanifest") })
        { var path = Path.Combine(app, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "test"); }
        File.WriteAllText(Path.Combine(models, "model.json"), "{}");
        var ready = service.RepairLocalState(); Assert(ready.IsReady, "Complete local standalone prerequisites were not accepted.");
        Assert(Directory.Exists(Path.Combine(state, "config")) && Directory.Exists(Path.Combine(state, "data")) && Directory.Exists(Path.Combine(state, "logs")), "Local state repair did not create safe user directories.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static async Task CalibrationModelReadiness()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-calibration-model-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root); var service = new CalibrationModelReadinessService(root);
        var progress = new CalibrationProgressDocument(1, [new(SensorFamily.JoyCon, CalibrationStage.Ready, 3, DateTimeOffset.UtcNow)]);
        var missing = await service.FindUnavailableAsync([SensorFamily.JoyCon], progress, repairFromLocalCaptures: false);
        Assert(missing.SequenceEqual([SensorFamily.JoyCon]), "A missing personal model was accepted from progress metadata alone.");
        File.WriteAllText(Path.Combine(root, "personal-gait-pace.json"), "{\"slowP95Dps\":80,\"naturalP95Dps\":180,\"fastP95Dps\":320}");
        Assert((await service.FindUnavailableAsync([SensorFamily.JoyCon], progress, repairFromLocalCaptures: false)).Count == 0, "A valid local personal model was rejected.");
        File.WriteAllText(Path.Combine(root, "personal-gait-pace.json"), "{broken");
        Assert((await service.FindUnavailableAsync([SensorFamily.JoyCon], progress, repairFromLocalCaptures: false)).Count == 1, "A corrupt personal model was accepted.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static void StandaloneRuntimeContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var appProject = File.ReadAllText(Path.Combine(root, "src", "NiiRMotion.App", "NiiRMotion.App.csproj"));
    Assert(appProject.Contains("<SelfContained>true</SelfContained>", StringComparison.Ordinal), "Windows application is not configured as self-contained.");
    Assert(appProject.Contains("models\\*.json", StringComparison.OrdinalIgnoreCase), "Local motion models are missing from the package contract.");
    Assert(!appProject.Contains("calibration\\*.json", StringComparison.OrdinalIgnoreCase), "A user-specific calibration file must never be bundled into a public package.");
    var liveSource = File.ReadAllText(Path.Combine(root, "src", "NiiRMotion.Infrastructure", "LiveLocomotionService.cs"));
    Assert(liveSource.Contains("NiiMotionPaths.Models", StringComparison.Ordinal) && !liveSource.Contains(@"C:\NiirMotion", StringComparison.OrdinalIgnoreCase), "Live locomotion still depends on the development checkout.");
    var runtimeSources = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText);
    var forbidden = new[] { "api.openai.com", "generativelanguage.googleapis.com", "api.anthropic.com", "Azure.AI.OpenAI" };
    Assert(!runtimeSources.Any(source => forbidden.Any(term => source.Contains(term, StringComparison.OrdinalIgnoreCase))), "An external AI service dependency exists in runtime source.");
}

static void ContinuousIntegrationContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "verify.yml"));
    Assert(workflow.Contains("windows-latest", StringComparison.Ordinal) && workflow.Contains("verify-development.ps1", StringComparison.Ordinal), "Windows CI does not run the canonical verification script.");
    var verification = File.ReadAllText(Path.Combine(root, "scripts", "verify-development.ps1"));
    Assert(verification.Contains("verify-release-readiness.ps1", StringComparison.Ordinal), "Release readiness contracts are not part of development verification.");
}

static void AiAgentHandoffContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    foreach (var relative in new[] { "AGENTS.md", "CLAUDE.md", "GEMINI.md", "NiiMotion_AI_Continuation_Prompt.txt", "docs/AI_AGENT_HANDOFF.md" })
        Assert(File.Exists(Path.Combine(root, relative)), $"AI handoff entry point is missing: {relative}");
    var handoff = File.ReadAllText(Path.Combine(root, "docs", "AI_AGENT_HANDOFF.md"));
    foreach (var required in new[] { "verify-development.ps1", "hardware-acceptance-matrix.json", "niirmotion_profile.json", "Normal VR", "fail", "cloud", "owner-hardware-verified" })
        Assert(handoff.Contains(required, StringComparison.OrdinalIgnoreCase), $"AI handoff omits a safety or completion rule: {required}");
    var prompt = File.ReadAllText(Path.Combine(root, "NiiMotion_AI_Continuation_Prompt.txt"));
    Assert(prompt.Contains("Do not mark", StringComparison.OrdinalIgnoreCase) && prompt.Contains("Architecture boundaries", StringComparison.OrdinalIgnoreCase), "Portable continuation prompt can overclaim validation or ignore architecture boundaries.");
}

static void HardwareAcceptanceMatrixContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "hardware-acceptance-matrix.json")));
    var matrix = document.RootElement;
    var profiles = matrix.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
    foreach (var profile in new[] { "normal-vr", "joycon-only", "psmove-only", "phone-only-experimental", "board-only-experimental", "joycon-ps-move", "all-devices" })
        Assert(profiles.Contains(profile), $"Hardware acceptance profile is missing: {profile}");
    var scenarios = matrix.GetProperty("scenarios").EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
    foreach (var scenario in new[] { "cold-start-and-first-step", "abrupt-stop", "turn-in-place", "sensor-sleep-and-disconnect", "safe-zero-on-failure" })
        Assert(scenarios.Contains(scenario), $"Hardware acceptance scenario is missing: {scenario}");
    Assert(matrix.GetProperty("games").GetArrayLength() >= 5, "Hardware acceptance game coverage is incomplete.");
}

static void DeviceSoftwareGuidanceContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var source = File.ReadAllText(Path.Combine(root, "src", "NiiRMotion.App", "DeviceCalibrationWindow.xaml.cs"));
    foreach (var requirement in new[] { "NiiMotion Joy-Con HID", "PSMoveAPI", "owoTrack", "NiiMotion Balance Board" })
        Assert(source.Contains(requirement, StringComparison.Ordinal), $"Device software guidance is missing: {requirement}");
}

static void PsMoveOfflineBundleContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var bundle = Path.Combine(root, "third_party", "psmoveapi", "4.0.12");
    var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["psmove.exe"] = "FEDE4BBE0675CE9A78FDAF6E5D99985D1ED18674F539DA2F1F4428458390D42D",
        ["psmoveapi.dll"] = "B0CF9D566D35D7ADF4CDD08829D1BA89B2A8462CF695E3A7456A56D90B2427B7",
        ["COPYING"] = "1A5007D3E29F1E89DFCB6471BB6EE1353D82DBD7071A5789EA28A64F5A27EB5F"
    };
    foreach (var item in expected)
    {
        var path = Path.Combine(bundle, item.Key); Assert(File.Exists(path), $"Offline PS Move component is missing: {item.Key}");
        Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == item.Value, $"Offline PS Move component hash mismatch: {item.Key}");
    }
}

static void GameLaunchCompatibilityContract()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-game-preflight-" + Guid.NewGuid().ToString("N"));
    try
    {
        var install = Path.Combine(root, "game"); var app = Path.Combine(root, "app"); Directory.CreateDirectory(install); Directory.CreateDirectory(Path.Combine(app, "OpenXRLayer", "bin", "win64"));
        var steam = Path.Combine(root, "steam.exe"); File.WriteAllText(steam, "test"); File.WriteAllText(Path.Combine(install, "Game-Win64-Shipping.exe"), "test"); File.WriteAllText(Path.Combine(app, "OpenXRLayer", "bin", "win64", "niirmotion_openxr.dll"), "test");
        var definition = new GameDefinition("user-openxr-test", "Test VR", "123", "OpenXR API Layer", true, "test"); var game = new InstalledGame(definition, true, install);
        var adapter = new OpenXrGameAdapter(definition.Id, definition.Name, "123", ["Game-Win64-Shipping.exe"], 1, DateTimeOffset.UtcNow);
        var service = new GameLaunchCompatibilityService(steam, app, [], [adapter]); Assert(service.Validate(game, true).IsReady, "Valid local OpenXR game was blocked.");
        File.Delete(Path.Combine(install, "Game-Win64-Shipping.exe")); var broken = service.Validate(game, true); Assert(!broken.IsReady && broken.Issues.Any(x => x.Code == "game-executable"), "Missing game executable was not blocked.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static void ApplicationSafetyMarker()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-safety-" + Guid.NewGuid().ToString("N"));
    try
    {
        using (var first = new ApplicationSafetyService(root)) Assert(!first.Begin().WasUnclean, "Fresh session was reported as unclean.");
        File.WriteAllText(Path.Combine(root, "running-session.json"), "{\"startedAtUtc\":\"2026-01-01T00:00:00Z\"}");
        using var second = new ApplicationSafetyService(root);
        Assert(second.Begin().WasUnclean, "Stale session marker was not detected.");
        second.Complete();
        Assert(!File.Exists(Path.Combine(root, "running-session.json")), "Clean shutdown did not remove its marker.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static void ConfigurationMigration()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-migration-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "user-hardware.json"), "{\"schemaVersion\":1,\"devices\":[]}");
        var report = new DataMigrationService(root).Run();
        Assert(report.SchemaVersion == DataMigrationService.CurrentSchema, "Migration did not reach the current schema.");
        Assert(File.Exists(Path.Combine(root, "user-hardware.json.pre-schema-3.backup")), "Migration did not create a rollback copy.");
        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data-schema.json")));
        Assert(state.RootElement.GetProperty("schemaVersion").GetInt32() == DataMigrationService.CurrentSchema, "Migration state is invalid.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static void VrPanelPacket()
{
    if (!OperatingSystem.IsWindows()) return;
    using var publisher = new VrPanelStatePublisher();
    publisher.Publish(new(1, "Joy-Con", "Alyx", "Hazır", .42f, "2/2", "Güvenli", DateTimeOffset.UtcNow));
    using var mapping = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(VrPanelStatePublisher.MappingName);
    using var view = mapping.CreateViewAccessor();
    Assert(view.ReadUInt32(0) == 0x3150564E, "VR panel packet magic is invalid.");
    var length = view.ReadInt32(4); var bytes = new byte[length]; view.ReadArray(8, bytes, 0, length);
    var state = JsonSerializer.Deserialize<VrPanelState>(bytes);
    Assert(state?.Profile == "Joy-Con" && Math.Abs(state.Speed - .42f) < .001f, "VR panel state did not round-trip.");
}

static void GameLaunchJournal()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-launch-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); var path = Path.Combine(root, "launch.json"); var store = new GameLaunchJournalStore(path);
    store.Save(new(1, "half-life-alyx", "Half-Life: Alyx", "psmove-only", true, GameLaunchStage.WaitingForMotionBridge, "Köprü bekleniyor", DateTimeOffset.UtcNow));
    var loaded = store.Load(); Assert(loaded?.Stage == GameLaunchStage.WaitingForMotionBridge && loaded.GameId == "half-life-alyx", "Launch journal did not round-trip."); store.Complete(); Assert(store.Load()?.Stage == GameLaunchStage.Idle, "Launch journal did not close safely."); Directory.Delete(root, true);
}

static void VrDashboardOverlayContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var sourcePath = Path.Combine(root, "native", "vr-overlay", "overlay.cpp");
    var binaryPath = Path.Combine(root, "native", "vr-overlay", "dist", "NiiMotion.VrOverlay.exe");
    var apiPath = Path.Combine(root, "native", "vr-overlay", "dist", "openvr_api.dll");
    Assert(File.Exists(sourcePath) && File.Exists(binaryPath) && File.Exists(apiPath), "SteamVR overlay package is incomplete.");
    var source = File.ReadAllText(sourcePath);
    Assert(source.Contains("CreateDashboardOverlay") && source.Contains("SetOverlayTexture") && source.Contains("PollNextOverlayEvent"), "Overlay does not implement the SteamVR dashboard contract.");
    Assert(source.Contains("NiiMotion.VrPanel.v1") && source.Contains("NiiMotion.VrPanel.Commands.v1"), "Overlay is not connected to the existing state and command channels.");
    Assert(source.Contains("NiiMotion.VrOverlay.Show") && source.Contains("ShowDashboard(kOverlayKey)"), "Overlay cannot be opened explicitly from the desktop application.");
    Assert(source.Contains("bgra[pixel * 4 + 3] = 255"), "GDI dashboard pixels are not made opaque and can render invisibly in SteamVR.");
    Assert(source.Contains("SetOverlayFromFile") && source.Contains("com.niirmotion.dashboard"), "Overlay has no persistent dashboard icon/application identity.");
    Assert(source.Contains("ShowSteamVrDesktop") && source.Contains("valve.steam.desktop") && source.Contains("system.desktop.1"), "Desktop button is not connected to compatible SteamVR desktop overlays.");
    Assert(source.Contains("WriteRuntimeManifest") && source.Contains("MouseButtonUp"), "Dashboard icon or resilient click handling is missing.");
}

static void GenericOpenXrAdapter()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-openxr-adapter-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, "Example-Win64-Shipping.exe"), []);
        var config = Path.Combine(root, "config"); var adapter = new OpenXrGameAdapter("user-openxr-42", "Example VR", "42", ["Example-Win64-Shipping.exe"], 1.2, DateTimeOffset.UtcNow);
        var store = new OpenXrGameAdapterStore(config); store.Save(adapter, root);
        Assert(store.Find(adapter.Id)?.Executables.Single() == "Example-Win64-Shipping.exe", "OpenXR adapter did not round-trip.");
        if (OperatingSystem.IsWindows()) Assert(SharedMemoryOpenXrOutputSink.Fnv1a("EXAMPLE-WIN64-SHIPPING.EXE") == SharedMemoryOpenXrOutputSink.Fnv1a("example-win64-shipping.exe"), "Process matching must ignore case.");
        Assert(store.Remove(adapter.Id) && store.Find(adapter.Id) is null, "OpenXR adapter was not removed.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static void FirstUsePreferences()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-ux-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new UserExperienceStore(root); store.Save(UserExperiencePreferences.Default with { TextScale = 9, Language = "xx", OnboardingComplete = true });
        var loaded = store.Load(); Assert(loaded.TextScale == 1.3 && loaded.Language == "tr" && loaded.OnboardingComplete, "User experience preferences were not normalized.");
        store.Save(loaded with { Language = "en" });
        var restartedStore = new UserExperienceStore(root);
        Assert(restartedStore.Load().Language == "en", "The selected UI language did not persist across application restarts.");
        var inventory = new UserHardwareInventory(1, true, false, false, false, false, DateTimeOffset.UtcNow);
        var progress = new CalibrationProgressDocument(1, [new(SensorFamily.JoyCon, CalibrationStage.Ready, 3, DateTimeOffset.UtcNow)]);
        var steps = FirstUseGuidance.Build(inventory, progress); Assert(steps.Count == 4 && steps[0].Complete && steps[1].Complete, "First-use guidance does not reflect calibration state.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static void GameValidationReceipt()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-game-receipt-" + Guid.NewGuid().ToString("N"));
    try
    {
        var path = Path.Combine(root, "receipt.json"); var store = new GameValidationReceiptStore(path);
        Assert(store.Load() is null, "A validation receipt existed before a game was verified.");
        store.Save(new(1, "example-vr", "Example VR", "joycon", true, DateTimeOffset.UtcNow));
        var receipt = store.Load(); Assert(receipt?.GameId == "example-vr" && receipt.NiiMotionEnabled, "The local validation receipt did not round-trip.");
        var inventory = new UserHardwareInventory(1, true, false, false, false, false, DateTimeOffset.UtcNow);
        var progress = new CalibrationProgressDocument(1, [new(SensorFamily.JoyCon, CalibrationStage.Ready, 3, DateTimeOffset.UtcNow)]);
        Assert(FirstUseGuidance.Build(inventory, progress, receipt is not null)[3].Complete, "The game-validation guidance step was not completed from local state.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static async Task UpdateDownloadVerification()
{
    var bytes = "verified NiiMotion package"u8.ToArray(); var sha = Convert.ToHexString(SHA256.HashData(bytes));
    using var client = new HttpClient(new StaticHttpHandler(bytes)); var service = new UpdateService(client);
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-update-" + Guid.NewGuid().ToString("N"));
    try
    {
        var manifest = new NiiMotionUpdateManifest("9.9.9", "https://updates.example/NiiMotion.exe", sha, null) { SizeBytes = bytes.Length };
        var staged = await service.DownloadVerifiedAsync(manifest, root); Assert(File.ReadAllBytes(staged).SequenceEqual(bytes), "Verified package was not staged.");
        try { await service.DownloadVerifiedAsync(manifest with { Sha256 = new string('0', 64) }, root); throw new InvalidOperationException("A bad update hash was accepted."); } catch (InvalidDataException) { }
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static void ReleaseIntegrityVerification()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-integrity-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "NiiRMotion.App.exe"), "safe"); var manifest = ReleaseIntegrityService.Create(root, "1.0.0");
        Assert(ReleaseIntegrityService.Verify(root, manifest), "Untouched release failed integrity verification."); File.AppendAllText(Path.Combine(root, "NiiRMotion.App.exe"), "changed");
        Assert(!ReleaseIntegrityService.Verify(root, manifest), "Modified release passed integrity verification.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

static void InstallerSafetyContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var installerPath = Path.Combine(root, "installer", "NiiMotion.iss");
    Assert(File.Exists(installerPath), "The Windows installer definition is missing.");

    var source = File.ReadAllText(installerPath);
    Assert(source.Contains("DefaultDirName={localappdata}\\Programs\\NiiMotion", StringComparison.OrdinalIgnoreCase),
        "The installer must remain a per-user installation and must not require administrator privileges.");
    Assert(source.Contains("PrivilegesRequired=lowest", StringComparison.OrdinalIgnoreCase),
        "The installer must remain usable from a standard Windows account.");
    Assert(source.Contains("Source: \"..\\artifacts\\app\\*\"", StringComparison.OrdinalIgnoreCase),
        "The installer must package the verified self-contained application output.");
    Assert(source.Contains("#ifndef MyAppVersion", StringComparison.OrdinalIgnoreCase),
        "The release build must be able to inject the application version into the installer.");
    Assert(source.Contains("LicenseFile=..\\LICENSE.md", StringComparison.OrdinalIgnoreCase) &&
           source.Contains("Source: \"..\\LICENSE.md\"", StringComparison.OrdinalIgnoreCase),
        "The authoritative source license must be shown by the installer and included in the installed documentation.");
    Assert(source.Contains("{autodesktop}\\NiiMotion", StringComparison.OrdinalIgnoreCase),
        "The optional desktop shortcut contract is missing.");
    Assert(source.Contains("RegDeleteValue(HKCU, 'Software\\Khronos\\OpenXR\\1\\ApiLayers\\Implicit'", StringComparison.OrdinalIgnoreCase),
        "Uninstall must remove the NiiMotion OpenXR implicit-layer registration.");
    Assert(source.Contains("removedriver \"' + DriverPath + '\"", StringComparison.OrdinalIgnoreCase),
        "Uninstall must unregister the NiiMotion OpenVR driver.");

    var destructiveUserDataTokens = new[]
    {
        "[UninstallDelete]", "{userappdata}", "{commonappdata}", "\\data\\*", "\\logs\\*", "\\config\\*"
    };
    foreach (var token in destructiveUserDataTokens)
        Assert(!source.Contains(token, StringComparison.OrdinalIgnoreCase),
            $"Installer removal must not target user-owned data: {token}");

    var buildScript = File.ReadAllText(Path.Combine(root, "scripts", "build-installer.ps1"));
    Assert(buildScript.Contains("/DMyAppVersion=$version", StringComparison.Ordinal),
        "The installer build must take its version from the application project.");
    Assert(buildScript.Contains("verify-development.ps1", StringComparison.Ordinal) && buildScript.Contains("-Publish", StringComparison.Ordinal) && buildScript.Contains("Get-FileHash", StringComparison.Ordinal),
        "The installer build must consume a verified publish and create a SHA-256 checksum.");
    Assert(source.Contains("PRIVACY.md", StringComparison.OrdinalIgnoreCase) && source.Contains("SECURITY.md", StringComparison.OrdinalIgnoreCase) && source.Contains("WiimoteLib.NetCore-MIT.txt", StringComparison.OrdinalIgnoreCase),
        "Installed documentation is missing privacy, security, or third-party license material.");

    var license = File.ReadAllText(Path.Combine(root, "LICENSE.md"));
    Assert(license.StartsWith("# PolyForm Noncommercial License 1.0.0", StringComparison.Ordinal) &&
           license.Contains("Any noncommercial purpose is a permitted purpose.", StringComparison.Ordinal),
        "The selected PolyForm Noncommercial license is missing or incomplete.");
}

static void ReleaseCandidatePipelineContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var scriptPath = Path.Combine(root, "scripts", "build-release-candidate.ps1");
    Assert(File.Exists(scriptPath), "The release-candidate entry point is missing.");
    var script = File.ReadAllText(scriptPath);
    foreach (var required in new[]
    {
        "verify-release-readiness.ps1", "-Strict", "build-installer.ps1",
        "verify-installer-smoke.ps1", "-SkipUiRender", "export-component-inventory.ps1",
        "release-candidate.json", "hardwareAcceptance", "codeSigning", "not-run-headless-environment"
    })
        Assert(script.Contains(required, StringComparison.Ordinal), $"Release-candidate pipeline is missing: {required}");

    var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "installer-acceptance.yml"));
    Assert(workflow.Contains("workflow_dispatch", StringComparison.Ordinal) &&
           !workflow.Contains("push:", StringComparison.Ordinal) &&
           workflow.Contains("build-release-candidate.ps1", StringComparison.Ordinal) &&
           workflow.Contains("-SkipUiSmoke", StringComparison.Ordinal) &&
           workflow.Contains("release-candidate.sha256", StringComparison.Ordinal),
        "Installer candidates must be deliberate, verified, and accompanied by inventory and integrity metadata.");

    var smoke = File.ReadAllText(Path.Combine(root, "scripts", "verify-installer-smoke.ps1"));
    Assert(smoke.Contains("WaitForExit($TimeoutSeconds * 1000)", StringComparison.Ordinal) &&
           smoke.Contains("[switch]$SkipUiRender", StringComparison.Ordinal) &&
           smoke.Contains("/MERGETASKS=`\"!desktopicon`\"", StringComparison.Ordinal),
        "Installer processes must be bounded, headless UI omission explicit, and the owner's desktop shortcut isolated.");
}

static void CombinedHardwareValidationContract()
{
    var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "NiiRMotion.Tests", "Program.cs"));
    var start = source.LastIndexOf("static async Task<int> MotionValidationAsync()", StringComparison.Ordinal);
    var end = source.IndexOf("static async Task<int> VrOutputSmokeAsync()", start, StringComparison.Ordinal);
    Assert(start >= 0 && end > start, "Combined hardware validation entry point is missing.");
    var validation = source[start..end];
    foreach (var required in new[] { "SensorFusionEngine", "PsMoveGaitEngine", "HybridGaitAgreementGate", "hybridGate.Combine", "OwoTrackSensorSource", "IncludeFields = true", "personal-gait-pace.json", "personal-psmove-training.json" })
        Assert(validation.Contains(required, StringComparison.Ordinal), $"Hardware validation does not use the current combined path: {required}");
    Assert(!validation.Contains("gait-v1.json", StringComparison.Ordinal), "Hardware validation still depends on the legacy developer calibration.");
}

static void VrPanelCommands()
{
    if (!OperatingSystem.IsWindows()) return;
    using var sender = new VrPanelCommandChannel(); using var receiver = new VrPanelCommandChannel();
    sender.Send(VrPanelCommand.EmergencyStop); Assert(receiver.Receive() == VrPanelCommand.EmergencyStop, "Emergency stop command was not delivered."); Assert(receiver.Receive() == VrPanelCommand.None, "A VR command was delivered twice.");
    sender.Send(VrPanelCommand.Rescan); Assert(receiver.Receive() == VrPanelCommand.Rescan, "Rescan command was not delivered.");
    sender.Send(VrPanelCommand.StartLocomotion); Assert(receiver.Receive() == VrPanelCommand.StartLocomotion, "Start locomotion command was not delivered.");
    sender.Send(VrPanelCommand.ShowDesktop); Assert(receiver.Receive() == VrPanelCommand.ShowDesktop, "Show desktop command was not delivered.");
}

static void OpenXrEngineDiscovery()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-engine-" + Guid.NewGuid().ToString("N"));
    try
    {
        var binaries = Path.Combine(root, "Example", "Binaries", "Win64"); Directory.CreateDirectory(binaries);
        File.WriteAllBytes(Path.Combine(binaries, "Example-Win64-Shipping.exe"), []); File.WriteAllBytes(Path.Combine(root, "launcher.exe"), []);
        var candidates = OpenXrGameAdapterStore.FindCandidateExecutables(root); Assert(candidates.First() == "Example-Win64-Shipping.exe" && !candidates.Contains("launcher.exe"), "OpenXR executable ranking is unsafe.");
        Assert(OpenXrGameAdapterStore.DetectEngine(root) == "Unreal Engine", "Unreal layout was not detected.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}


static async Task OpenXrSharedOutputTest()
{
    if (!OperatingSystem.IsWindows()) return;
    await using var sink = new SharedMemoryOpenXrOutputSink(); await sink.AttachAsync(); await sink.WriteAsync(new LocomotionVector(2, .75f));
    using var mapping = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(SharedMemoryOpenXrOutputSink.MappingName);
    using var view = mapping.CreateViewAccessor();
    Assert(view.ReadUInt32(0) == 0x3158524E && view.ReadUInt32(4) == 1, "OpenXR packet header changed.");
    Assert(view.ReadSingle(16) == 1 && Math.Abs(view.ReadSingle(20) - .75f) < .001f && view.ReadUInt32(24) == 1, "OpenXR packet was not clamped or enabled.");
    Assert(view.ReadUInt32(28) == SharedMemoryOpenXrOutputSink.Fnv1a("Impact-Win64-Shipping.exe") && view.ReadUInt32(32) == SharedMemoryOpenXrOutputSink.Fnv1a("Impact.exe"), "Metro process scope changed.");
    await sink.DetachAsync();
}

static void OpenXrLayerContract()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var manifest = Path.Combine(root, "native", "openxr-layer", "dist", "niirmotion_openxr.json"); var dll = Path.Combine(root, "native", "openxr-layer", "dist", "bin", "win64", "niirmotion_openxr.dll");
    using var json = JsonDocument.Parse(File.ReadAllText(manifest)); Assert(json.RootElement.GetProperty("api_layer").GetProperty("name").GetString() == "XR_APILAYER_NIIRMOTION_locomotion", "OpenXR layer name changed.");
    var library = System.Runtime.InteropServices.NativeLibrary.Load(dll); try { Assert(System.Runtime.InteropServices.NativeLibrary.TryGetExport(library, "xrNegotiateLoaderApiLayerInterface", out _), "OpenXR negotiation export missing."); } finally { System.Runtime.InteropServices.NativeLibrary.Free(library); }
    Assert(new GameMotionProfileStore().Load("metro-awakening").MappingVersion == "metro-openxr-layer-v1", "Metro OpenXR profile missing.");
}

static void GameMotionProfileTest()
{
    var config = Path.Combine(Path.GetTempPath(), "niirmotion-game-profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(config);
    try
    {
        new GameSelectionStore(config).Save("half-life-alyx"); var store = new GameMotionProfileStore(config); var builtIn = store.LoadActive();
        Assert(builtIn.MappingVersion == "alyx-openvr-v2" && builtIn.AccelerationPerSecond == 3 && builtIn.DecelerationPerSecond == 12, "Established Alyx response contract changed.");
        store.Save(builtIn with { SpeedMultiplier = 9, MaximumOutput = .1, Deadzone = .5 }); var safe = store.LoadActive();
        Assert(safe.SpeedMultiplier == 3 && safe.MaximumOutput == .2 && safe.Deadzone == .2, "Unsafe game tuning was not bounded.");
        var reset = store.Reset("half-life-alyx"); Assert(reset.SpeedMultiplier == 1 && reset.MaximumOutput == 1 && store.LoadAll().Count == 0, "Game tuning did not restore its built-in profile.");
        var openXr = new OpenXrGameAdapter("user-openxr-123", "User OpenXR", "123", ["Game.exe"], 1.6, DateTimeOffset.UtcNow);
        new OpenXrGameAdapterStore(config).Save(openXr); new GameSelectionStore(config).Save(openXr.Id);
        Assert(Math.Abs(store.LoadActive().SpeedMultiplier - 1.6) < .001, "User OpenXR adapter speed was not applied to its game profile.");
    }
    finally { Directory.Delete(config, true); }
}

static void GameSensorOptimizationTest()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-game-sensor-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var store = new GameSensorOptimizationStore(root);
        Assert(Math.Abs(store.Load("half-life-alyx", "psmove-only").DistanceScale - .41) < .001, "Alyx PS Move verified default changed.");
        Assert(store.Load("half-life-alyx", "joycon-only").DistanceScale == 1, "PS Move scale leaked into Joy-Con.");
        var tuned = store.ApplyTelemetry("half-life-alyx", "psmove-only", new(10, 12, .75, 8, false, false));
        Assert(tuned.DistanceScale < .41 && tuned.Source == "Oyun telemetrisi", "Clean game telemetry did not reduce excessive stride distance.");
        var restored = store.RestorePrevious("half-life-alyx", "psmove-only");
        Assert(Math.Abs(restored.DistanceScale - .41) < .001, "Previous game-sensor scale was not restored.");
        var rejected = store.ApplyTelemetry("half-life-alyx", "psmove-only", new(10, 9, .75, 8, true, false));
        Assert(Math.Abs(rejected.DistanceScale - .41) < .001 && rejected.Source.StartsWith("Reddedildi:"), "Teleport-contaminated telemetry changed the model.");
    }
    finally { Directory.Delete(root, true); }
}

static void AlyxPositionTelemetryParser()
{
    Assert(AlyxPositionParser.TryParse("] getpos_exact\r\nsetpos_exact 125.500000 -42.250000 72.000000;setang_exact 0.000000 91.500000 0.000000", out var pose), "Alyx getpos_exact output was not parsed.");
    Assert(Math.Abs(pose.X - 125.5) < .001 && Math.Abs(pose.Y + 42.25) < .001 && Math.Abs(pose.Yaw - 91.5) < .001, "Parsed Alyx pose changed numeric values.");
    var oneMeterAway = pose with { X = pose.X + 39.37007874 };
    Assert(Math.Abs(pose.HorizontalDistanceTo(oneMeterAway) - 1) < .001, "Source units were not converted to meters.");
    Assert(!AlyxPositionParser.TryParse("loading map...", out _), "Unrelated console output became a player pose.");
}

static void GameTelemetryProviderSelection()
{
    var alyx = GameTelemetryProviderFactory.Create("half-life-alyx", "546560");
    var custom = GameTelemetryProviderFactory.Create("user-steam-123", "123");
    Assert(alyx.Capability.Mode == GameTelemetryMode.Direct && alyx.LaunchArguments.Contains("netconport"), "Alyx direct telemetry provider was not selected.");
    Assert(custom.Capability.Mode == GameTelemetryMode.Guided && string.IsNullOrEmpty(custom.LaunchArguments), "Unknown VR game did not receive the universal safe provider.");
}

static void GuidedGameOptimization()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-guided-game-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
    try
    {
        var store = new GameSensorOptimizationStore(root);
        var faster = store.ApplyFeedback("user-steam-123", "joycon-only", GamePaceFeedback.TooSlow);
        Assert(Math.Abs(faster.DistanceScale - 1.1) < .001 && faster.Source == "Oyun içi kısa doğrulama", "Slow feedback did not raise the game-only scale.");
        var restored = store.RestorePrevious("user-steam-123", "joycon-only");
        Assert(Math.Abs(restored.DistanceScale - 1) < .001, "Guided game optimization was not reversible.");
        var confirmed = store.ApplyFeedback("user-steam-123", "joycon-only", GamePaceFeedback.Correct);
        Assert(confirmed.Confidence == 1 && confirmed.Source == "Oyun içi hız doğrulandı", "Correct feedback was not persisted as verified.");
    }
    finally { Directory.Delete(root, true); }
}

static async Task LiveHmdSharedPoseSource()
{
    if (!OperatingSystem.IsWindows()) return;
    var mappingName = "NiiMotion.HmdPose.Test." + Guid.NewGuid().ToString("N");
    using var map = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(mappingName, 64);
    using (var view = map.CreateViewAccessor())
    {
        view.Write(0, 0x31444D48u); view.Write(4, 1u); view.Write(8, 42L); view.Write(16, System.Diagnostics.Stopwatch.GetTimestamp()); view.Write(24, 1u);
        view.Write(28, 1.25f); view.Write(32, 1.70f); view.Write(36, -0.5f); view.Write(40, 0f); view.Write(44, 0f); view.Write(48, 0f); view.Write(52, 1f);
    }
    await using var source = new SharedMemoryHmdPoseSource(mappingName); await source.StartAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); var sample = await source.Samples.ReadAsync(timeout.Token);
    Assert(sample.Sequence == 42 && sample.IsTracked && Math.Abs(sample.PositionMeters.Y - 1.7f) < .001 && sample.Orientation.W == 1, "Live HMD shared pose changed during transport.");
}

static void FreshHmdTrackingState()
{
    if (!OperatingSystem.IsWindows()) return;
    var mappingName = "NiiMotion.HmdPresence.Test." + Guid.NewGuid().ToString("N");
    using var map = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateOrOpen(mappingName, 64);
    using var view = map.CreateViewAccessor();
    void Write(bool tracked, long qpc)
    {
        view.Write(0, 0x31444D48u); view.Write(4, 1u); view.Write(8, 1L); view.Write(16, qpc); view.Write(24, tracked ? 1u : 0u);
        view.Write(28, 0f); view.Write(32, 1.7f); view.Write(36, 0f); view.Write(40, 0f); view.Write(44, 0f); view.Write(48, 0f); view.Write(52, 1f);
    }
    Write(true, Stopwatch.GetTimestamp());
    Assert(SharedMemoryHmdPoseSource.TryGetFreshTracking(out var tracked, mappingName) && tracked, "Fresh tracked HMD state was not accepted.");
    Write(false, Stopwatch.GetTimestamp());
    Assert(SharedMemoryHmdPoseSource.TryGetFreshTracking(out tracked, mappingName) && !tracked, "Fresh untracked HMD state was not authoritative.");
    Write(true, Stopwatch.GetTimestamp() - Stopwatch.Frequency * 2);
    Assert(!SharedMemoryHmdPoseSource.TryGetFreshTracking(out _, mappingName), "Stale HMD state was treated as live.");
}

static void LearnedDataResetTest()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-reset-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "config")); Directory.CreateDirectory(Path.Combine(root, "data", "user-gait"));
    try
    {
        File.WriteAllText(Path.Combine(root, "config", "personal-gait.json"), "{}");
        File.WriteAllText(Path.Combine(root, "config", "psmove-assignments.json"), "identity");
        File.WriteAllText(Path.Combine(root, "config", "game-motion-profiles.json"), "games");
        File.WriteAllText(Path.Combine(root, "data", "user-gait", "samples.jsonl"), "sample");
        var result = new LearnedMotionDataService(root).Reset();
        Assert(File.Exists(result.BackupPath) && result.RemovedFiles == 2, "Reset backup was not created before learned files were removed.");
        Assert(!File.Exists(Path.Combine(root, "config", "personal-gait.json")) && !File.Exists(Path.Combine(root, "data", "user-gait", "samples.jsonl")), "Learned files survived reset.");
        Assert(File.Exists(Path.Combine(root, "config", "psmove-assignments.json")) && File.Exists(Path.Combine(root, "config", "game-motion-profiles.json")), "Device identity or game settings were removed.");
    }
    finally { Directory.Delete(root, true); }
}
static void DiagnosticRedactionTest()
{
    var text = DiagnosticPackageService.Redact("phone 192.168.1.24:9185 move 0007041EFC1E");
    Assert(!text.Contains("192.168.1.24") && !text.Contains("0007041EFC1E") && text.Contains("[IP]") && text.Contains("[DEVICE-ID]"), "Diagnostic redaction leaked a private endpoint or stable device identity.");
}

static void SessionHealthReportTest()
{
    var path = Path.Combine(Path.GetTempPath(), "niirmotion-health-" + Guid.NewGuid().ToString("N") + ".jsonl");
    var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    try
    {
        File.WriteAllLines(path,
        [
            JsonSerializer.Serialize(new { timestampUtc = now.AddMinutes(-5), category = "psmove", eventName = "disconnected", message = "private detail" }),
            "not-json",
            JsonSerializer.Serialize(new { timestampUtc = now.AddMinutes(-4), category = "psmove", eventName = "connected", message = "private detail" }),
            JsonSerializer.Serialize(new { timestampUtc = now.AddMinutes(-3), category = "game-launch", eventName = "Failed", message = "private detail" }),
            JsonSerializer.Serialize(new { timestampUtc = now.AddDays(-2), category = "application", eventName = "crash", message = "outside window" })
        ]);
        var report = new SessionHealthReportService().Analyze(path, now);
        Assert(report.EventsRead == 3 && report.SensorDisconnects == 1 && report.SensorReconnects == 1 && report.LaunchFailures == 1, "Session health counters are incorrect.");
        Assert(report.ApplicationCrashes == 0 && report.OverallState == "attention", "Session health time window or severity is incorrect.");
        Assert(!JsonSerializer.Serialize(report).Contains("private detail", StringComparison.Ordinal), "Session health report copied event payloads.");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void GameAdapterValidation()
{
    var valid = new UserGameAdapter("user-1", "Test VR", "123", "/actions/main", "/actions/main/in/move", "/actions/main/in/sprint", 1.2, DateTimeOffset.UtcNow);
    Assert(GameAdapterValidator.Validate(valid).Count == 0, "Valid SteamVR actions were rejected.");
    Assert(GameAdapterValidator.Validate(valid with { MovementAction = "keyboard/W" }).Count > 0, "Unsafe non-action path was accepted.");
    Assert(GameAdapterValidator.Validate(valid with { SpeedMultiplier = 5 }).Count > 0, "Unsafe speed multiplier was accepted.");
}

static void GameAdapterRestoreTest()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-adapter-" + Guid.NewGuid().ToString("N")); var input = Path.Combine(root, "native", "openvr-driver", "dist", "resources", "input"); Directory.CreateDirectory(input);
    var original = """{"default_bindings":[{"app_key":"steam.app.546560","binding_url":"default_bindings/alyx.json"}]}"""; File.WriteAllText(Path.Combine(input, "niirmotion_profile.json"), original);
    try
    {
        var store = new GameAdapterStore(root); var adapter = new UserGameAdapter("user-steam-123", "Test VR", "123", "/actions/main", "/actions/main/in/move", null, 1, DateTimeOffset.UtcNow);
        store.SaveAndInstall(adapter); Assert(store.Load().Count == 1 && store.HasOriginalProfileBackup, "Adapter or original backup was not created.");
        var result = store.RestoreOriginalProfile(); Assert(result.RemovedAdapterCount == 1 && File.Exists(result.SafetyCopyPath), "Safe restore copy was not created.");
        Assert(store.Load().Count == 0, "Restored adapter store was not cleared.");
        Assert(JsonNode.Parse(File.ReadAllText(Path.Combine(input, "niirmotion_profile.json")))!["default_bindings"]!.AsArray().Count == 1, "Original driver profile was not restored.");
        Assert(!File.Exists(Path.Combine(input, "default_bindings", "steam.app.123_niirmotion.json")), "Generated binding survived restore.");
    }
    finally { Directory.Delete(root, true); }
}

static void SteamActionDiscoveryTest()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-actions-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
    try
    {
        File.WriteAllText(Path.Combine(root, "actions.json"), """{"actions":[{"name":"/actions/main/in/jump"},{"name":"/actions/main/in/move"}]}""");
        var actions = new SteamActionDiscovery().Discover(root);
        Assert(actions.Count == 2, "Steam action paths were not discovered.");
        Assert(actions[0].Path.EndsWith("/move", StringComparison.OrdinalIgnoreCase), "Movement candidate was not prioritized.");
        Assert(actions[0].ActionSet == "/actions/main", "Action set was not derived correctly.");
        File.Delete(Path.Combine(root, "actions.json")); var runtime = Path.Combine(root, "Engine", "Binaries"); Directory.CreateDirectory(runtime); File.WriteAllBytes(Path.Combine(runtime, "openxr_loader.dll"), [1]);
        var inspection = new SteamActionDiscovery().Inspect(root); Assert(inspection.Runtime == VrInputRuntime.OpenXr && inspection.Actions.Count == 0, "OpenXR-only game was misreported as a missing SteamVR action game.");
    }
    finally { Directory.Delete(root, true); }
}

static void HardwareInventoryProfiles()
{
    var all = new UserHardwareInventory(1, true, true, true, true, true, DateTimeOffset.UtcNow);
    var profiles = MotionProfileCatalog.For(all);
    Assert(profiles.Count == 16, "Four sensor families must create 15 combinations plus Normal VR.");
    Assert(profiles.Select(x => x.Profile.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == profiles.Count, "Generated profile ids must be unique.");
    Assert(profiles.Any(x => x.Profile.Required.Contains(DeviceKind.JoyConLeft) && x.Profile.Required.Contains(DeviceKind.PsMoveLeft) && x.Profile.Required.Contains(DeviceKind.Phone) && x.Profile.Required.Contains(DeviceKind.BalanceBoard)), "All-device profile is missing.");
    Assert(profiles.Where(x => x.Profile.LocomotionAllowed).All(x => !x.Profile.Required.Contains(DeviceKind.HandTracking) && x.Profile.Optional.Contains(DeviceKind.HandTracking)), "Hand tracking must enhance profiles but never block locomotion.");
    Assert(profiles[0].Profile == MotionProfile.ClassicVr && profiles[1].Profile.Name == "Sadece PS Move", "Profiles must begin with Normal VR then the easiest high-performance sensor option.");
    var moveOnly = MotionProfileCatalog.For(all with { HasJoyCons = false, HasPhone = false, HasBalanceBoard = false });
    Assert(moveOnly.Count == 2 && moveOnly.Any(x => x.Profile.Required.Contains(DeviceKind.PsMoveLeft)), "Inventory must hide unavailable-device profiles.");
}

static void CalibrationProgressSchema()
{
    var document = new CalibrationProgressDocument(1, [new(SensorFamily.PsMove, CalibrationStage.Ready, 3, DateTimeOffset.UtcNow)], [new("ps-move-phone", 2, DateTimeOffset.UtcNow)]);
    var json = JsonSerializer.Serialize(document);
    var loaded = JsonSerializer.Deserialize<CalibrationProgressDocument>(json)!;
    Assert(loaded.Devices.Single().IsReady, "Completed device calibration must remain ready after serialization.");
    var profile = loaded.Profiles!.Single();
    Assert(profile.CompletedPhases == 2 && !profile.IsReady, "Profile calibration must remain separate from device readiness.");
}

static void HybridLegAgreement()
{
    var gait = new GaitSnapshot(GaitState.Walking, 1.8, .8, .8, LegSide.Left, 10);
    var primary = new FusionSnapshot(gait, .8, .8, false, false, false, 0);
    var agreeing = HybridGaitFusion.Combine(primary, gait with { TargetSpeed = .9, Confidence = .85 });
    var disagreeing = HybridGaitFusion.Combine(primary, gait with { State = GaitState.Idle, TargetSpeed = 0, Confidence = 0 });
    Assert(agreeing.TargetSpeed > disagreeing.TargetSpeed, "Two agreeing leg systems must outrank a single active system.");
    Assert(agreeing.GlobalConfidence > disagreeing.GlobalConfidence, "Agreement must increase hybrid confidence.");
    Assert(disagreeing.TargetSpeed > 0 && disagreeing.TargetSpeed < primary.TargetSpeed, "A single active system must degrade instead of causing an abrupt dropout.");
    var gate = new HybridGaitAgreementGate(TimeSpan.FromMilliseconds(360));
    Assert(gate.Combine(primary, gait with { State = GaitState.Idle, TargetSpeed = 0, Confidence = 0 }, 100).TargetSpeed == 0, "A combined profile must not start from only one leg-sensor family.");
    Assert(gate.Combine(primary, gait with { TargetSpeed = .9, Confidence = .85 }, 200).TargetSpeed > 0, "Agreement must start combined locomotion.");
    Assert(gate.Combine(primary, gait with { State = GaitState.Idle, TargetSpeed = 0, Confidence = 0 }, 200 + Stopwatch.Frequency / 10).TargetSpeed > 0, "A brief sensor disagreement must not create a visible cut-out.");
    Assert(gate.Combine(primary, gait with { State = GaitState.Idle, TargetSpeed = 0, Confidence = 0 }, 200 + Stopwatch.Frequency).TargetSpeed == 0, "Sustained disagreement must stop combined locomotion.");
}
static async Task UnifiedSensorReplayOrder()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-unified-replay-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
    try
    {
        await File.WriteAllLinesAsync(Path.Combine(root, "left.jsonl"), ["{\"sequence\":1,\"timestamp\":{\"monotonicTicks\":10}}", "{\"sequence\":2,\"timestamp\":{\"monotonicTicks\":30}}"]);
        await File.WriteAllLinesAsync(Path.Combine(root, "phone.jsonl"), ["{\"sequence\":8,\"timestamp\":{\"monotonicTicks\":20}}"]);
        var manifest = new UnifiedSensorSessionManifest(1, "test", "replay-test", null, 0, DateTimeOffset.UtcNow, [new(SensorFamily.JoyCon, "left", "left.jsonl", 2), new(SensorFamily.Phone, "phone", "phone.jsonl", 1)]);
        var path = Path.Combine(root, "unified-session.json"); await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest));
        var replayed = new List<UnifiedReplaySample>(); await foreach (var sample in new UnifiedSensorSessionReplay().ReadAsync(path)) replayed.Add(sample);
        Assert(replayed.Select(x => x.MonotonicTicks).SequenceEqual([10L, 20L, 30L]), "Unified replay must merge streams by monotonic timestamp.");
        Assert(replayed[1].Sensor == SensorFamily.Phone, "Replay must preserve stream identity.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}
static void SteamGameCatalogDetection()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-steam-catalog-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
    try
    {
        File.WriteAllText(Path.Combine(root, "appmanifest_546560.acf"), "\"AppState\" { \"appid\" \"546560\" \"name\" \"Half-Life: Alyx\" \"installdir\" \"Half-Life Alyx\" }");
        var games = new SteamGameCatalog([root]).Detect();
        Assert(games.Single(x => x.Definition.Id == "half-life-alyx").State == GameIntegrationState.Ready, "A real Alyx manifest must unlock its adapter.");
        Assert(games.Single(x => x.Definition.Id == "metro-awakening").State == GameIntegrationState.NotInstalled, "Missing manifests must never be reported as installed.");
        Assert(games.Single(x => x.Definition.Id == "zelda-botw").State == GameIntegrationState.VerificationRequired, "Zelda must remain unverified without a real integration path.");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}
static DeviceStatus Connected(DeviceKind kind) => new(kind, kind.ToString(), DeviceState.Connected, "", "");
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void RequiredMissingBlocks() { var result = SessionReadinessEvaluator.Evaluate(MotionProfile.AlyxFullFusion, [Connected(DeviceKind.SteamVr)]); Assert(result.State == ReadinessState.NotReady, "Expected NotReady."); }
static void OptionalMissingDegrades() { var result = SessionReadinessEvaluator.Evaluate(MotionProfile.AlyxFullFusion, MotionProfile.AlyxFullFusion.Required.Select(Connected).ToArray()); Assert(result.State == ReadinessState.Degraded, "Expected Degraded."); }
static void ConfiguredHandControl()
{
    var devices = MotionProfile.AlyxFullFusion.Required.Select(Connected).Append(Connected(DeviceKind.VirtualDesktop))
        .Append(new DeviceStatus(DeviceKind.HandTracking, "VR El Kontrolü", DeviceState.Configured, "", "")).ToArray();
    var status = devices[^1];
    Assert(status.Symbol == "●" && status.StateText == "Kullanıma açık", "Configured hand control must have a distinct visual state.");
    Assert(SessionReadinessEvaluator.Evaluate(MotionProfile.AlyxFullFusion, devices).State == ReadinessState.Ready, "Configured optional hand control must not degrade readiness.");
}
static void NonHmdMatrix()
{
    var report = NonHmdRegressionMatrix.Run();
    Assert(report.Cases.Count > 100 && report.Passed, "At least one non-HMD profile/readiness regression scenario failed.");
}
static void WorkspaceMaintenanceBudget()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-maintenance-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "nested"));
    try
    {
        for (var i = 0; i < 5; i++) { var path = Path.Combine(root, "nested", i + ".bin"); File.WriteAllBytes(path, new byte[100]); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i)); }
        var remaining = WorkspaceMaintenanceService.EnforceTreeBudget(root, 250);
        Assert(remaining <= 250 && Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 2, "Recursive cache budget was not enforced.");
    }
    finally { Directory.Delete(root, true); }
}
static void AllReady() { var devices = Enum.GetValues<DeviceKind>().Select(Connected).ToArray(); Assert(SessionReadinessEvaluator.Evaluate(MotionProfile.AlyxFullFusion, devices).State == ReadinessState.Ready, "Expected Ready."); }
static void ClassicVrIsNonInvasive() => Assert(!MotionProfile.ClassicVr.LocomotionAllowed, "Classic VR must keep locomotion off.");
static async Task PersonalGaitAnalysisRoundTrip()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-personal-analysis-" + Guid.NewGuid().ToString("N"));
    try
    {
        var learning = Path.Combine(root, "joycon-learning");
        var session = Path.Combine(learning, "part-1-test");
        Directory.CreateDirectory(session);
        await File.WriteAllTextAsync(Path.Combine(learning, "progress-v2.json"), "[1]");
        await File.WriteAllTextAsync(Path.Combine(session, "session.json"), "{\"part\":1}");
        await using (var writer = new StreamWriter(Path.Combine(session, "joycons.jsonl")))
            foreach (var (activity, magnitude) in new[] { ("slow_walk", 80d), ("natural_walk", 190d), ("fast_walk", 380d) })
                for (var i = 0; i < 150; i++) await writer.WriteLineAsync(JsonSerializer.Serialize(new { activity, sample = new { AngularVelocityDps = new { X = magnitude, Y = 0, Z = 0 } } }));
        var analyzer = new PersonalGaitAnalyzer();
        var analysis = analyzer.Analyze(root);
        var output = Path.Combine(root, "personal.json");
        await analyzer.ApplyAsync(analysis, output);
        var loaded = await PersonalGaitPace.LoadAsync(output);
        Assert(loaded.SlowP95Dps == 80 && loaded.NaturalP95Dps == 190 && loaded.FastP95Dps == 380, "Personal gait anchors mismatch.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
static void JoyConIdentity() { Assert(JoyConDeviceDescriptor.TryCreate("left", 0x057e, 0x2006, out var left) && left!.Side == JoyConSide.Left, "Original left must match."); Assert(!JoyConDeviceDescriptor.TryCreate("clone", 0x1234, 0x2006, out _), "Non-Nintendo VID must not match."); }
static void PsMoveIdentity()
{
    Assert(PsMoveDeviceDescriptor.TryCreate("hid#vid_054c&pid_03d5", 0x054c, 0x03d5, out var move) && move!.Model == "PS Move CECH-ZCM1", "Original ZCM1 must match.");
    Assert(move!.Transport == PsMoveTransport.Usb, "USB HID path must be classified as USB.");
    Assert(PsMoveDeviceDescriptor.TryCreate("hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&03d5", 0x054c, 0x03d5, out var wireless) && wireless!.Transport == PsMoveTransport.Bluetooth, "Bluetooth HID path must be classified as Bluetooth.");
    Assert(!PsMoveDeviceDescriptor.TryCreate("wrong-product", 0x054c, 0x0c5e, out _), "ZCM2 must not be mislabeled as ZCM1.");
    Assert(!PsMoveDeviceDescriptor.TryCreate("clone", 0x1234, 0x03d5, out _), "Non-Sony VID must not match.");
}
static void PsMoveInputReport()
{
    var report = new byte[49];
    report[0] = 0x01; report[4] = 0x4B; report[5] = 10; report[6] = 14; report[11] = 0x12; report[12] = 0x05; report[43] = 0x34;
    report[13] = 0x00; report[14] = 0x90; // +4096 after ZCM1 center offset
    report[19] = 0x00; report[20] = 0x70; // -4096 after ZCM1 center offset
    report[38] = 0x08; report[39] = 0x00; // signed 12-bit -2048
    var parsed = PsMoveZcm1ReportParser.Parse(report);
    Assert(parsed.Sequence == 11 && parsed.Trigger == 12 && parsed.Battery == 5 && parsed.Timestamp == 0x1234, "ZCM1 status fields mismatch.");
    Assert((parsed.Buttons & (1u << 19)) != 0, "ZCM1 Move button bit mismatch.");
    Assert(parsed.OlderSample.Acceleration.X == 4096 && parsed.LatestSample.Acceleration.X == -4096, "ZCM1 dual accelerometer frames mismatch.");
    Assert(parsed.Magnetometer.X == -2048, "ZCM1 signed magnetometer value mismatch.");
}
static void PsMoveLedReport()
{
    var report = PsMoveZcm1OutputReport.CreateLed(255, 20, 30);
    Assert(report.Length == 49 && report[0] == 0x06 && report[2] == 255 && report[3] == 20 && report[4] == 30 && report[6] == 0, "PS Move LED report mismatch.");
}
static void PsMoveFactoryCalibration()
{
    var blob = new byte[143];
    static void Put(byte[] b, int offset, int centered) { var raw = centered + 0x8000; b[offset] = (byte)raw; b[offset + 1] = (byte)(raw >> 8); }
    Put(blob, 10, -1000); Put(blob, 36, -1000); Put(blob, 20, -1000);
    Put(blob, 22, 1000); Put(blob, 30, 1000); Put(blob, 8, 1000);
    Put(blob, 42, 0); Put(blob, 44, 0); Put(blob, 46, 0);
    Put(blob, 70, 800); Put(blob, 80, 800); Put(blob, 90, 800);
    var calibration = PsMoveZcm1FactoryCalibration.Parse(blob);
    Assert(calibration.CalibrateAcceleration(Vector3.Zero) == Vector3.Zero, "PS Move accelerometer center mismatch.");
    Assert(Math.Abs(calibration.CalibrateAcceleration(new Vector3(1000)).X - 1) < .001, "PS Move accelerometer +1g mismatch.");
    Assert(Math.Abs(calibration.CalibrateGyroscope(new Vector3(800)).X - 80 * 2 * Math.PI / 60) < .001, "PS Move gyroscope scale mismatch.");
}
static void PsMoveTrainingProfileTest()
{
    // Keep the safety contract covered alongside the learned profile contract.
    PsMoveLedReport();
    var observations = new List<PsMoveTrainingObservation>(12_000);
    Add("stand", .08); Add("slow_walk", .45); Add("natural_walk", .75); Add("fast_walk", 1.35);
    var profile = new PsMoveTrainingAnalyzer().Analyze(observations);
    Assert(profile.RestReleaseThresholdRadps < profile.GaitActivationThresholdRadps, "PS Move activation must stay above rest noise.");
    Assert(profile.SlowAnchorRadps < profile.NaturalAnchorRadps && profile.NaturalAnchorRadps < profile.FastAnchorRadps, "PS Move pace anchors must remain ordered.");
    PsMoveGaitContractTest();
    void Add(string label, double center)
    {
        for (var i = 0; i < 3_000; i++) observations.Add(new(label, i % 2 == 0 ? LegSide.Left : LegSide.Right, i * 6, center + (i % 17 - 8) * .002));
    }
}
static void PsMoveGaitContractTest()
{
    var anchors = new Dictionary<string, PsMoveMotionAnchor>();
    var profile = new PsMoveTrainingProfile(1, DateTimeOffset.UtcNow, SensorPlacement.CalfLowerLeg, 10000, 60, .10, .24, .43, .69, 1.17, 1, anchors);
    var gait = new PsMoveGaitEngine(profile); var ticks = System.Diagnostics.Stopwatch.GetTimestamp();
    for (var i = 0; i < 16; i++) { ticks += System.Diagnostics.Stopwatch.Frequency / 2; var side = i % 2 == 0 ? LegSide.Left : LegSide.Right; gait.Observe(side, new Vector3(.6f,.6f,.1f), ticks); gait.Observe(side, Vector3.Zero, ticks + 2); }
    Assert(gait.Update(ticks).TargetSpeed > 0, "Alternating PS Move calf motion must activate locomotion.");
    Assert(gait.Update(ticks + (long)(System.Diagnostics.Stopwatch.Frequency * .36)).TargetSpeed > 0, "Established PS Move gait must not drop between natural steps.");
    Assert(gait.Update(ticks + (long)(System.Diagnostics.Stopwatch.Frequency * .5)).TargetSpeed == 0, "PS Move gait must stop promptly after motion ends.");
    var brake = new PsMoveGaitEngine(profile); ticks = System.Diagnostics.Stopwatch.GetTimestamp();
    for (var i = 0; i < 10; i++) { ticks += System.Diagnostics.Stopwatch.Frequency / 3; var side = i % 2 == 0 ? LegSide.Left : LegSide.Right; brake.Observe(side, new Vector3(.6f,.6f,.1f), Vector3.UnitY, ticks); brake.Observe(side, Vector3.Zero, new Vector3(0, 1.2f, 0), ticks + 2); }
    Assert(brake.Update(ticks).TargetSpeed > 0, "PS Move active-brake fixture did not establish gait.");
    for (var i = 0; i < 30; i++) { ticks += System.Diagnostics.Stopwatch.Frequency / 100; brake.Observe(LegSide.Left, Vector3.Zero, Vector3.UnitY, ticks); brake.Observe(LegSide.Right, Vector3.Zero, Vector3.UnitY, ticks + 1); }
    Assert(brake.Update(ticks).TargetSpeed == 0, "Bilateral zero-velocity lock must actively brake PS Move gait.");
    var reject = new PsMoveGaitEngine(profile); ticks = System.Diagnostics.Stopwatch.GetTimestamp();
    for (var i = 0; i < 8; i++) { ticks += System.Diagnostics.Stopwatch.Frequency / 2; reject.Observe(LegSide.Left, new Vector3(1,.1f,.05f), ticks); reject.Observe(LegSide.Right, new Vector3(1,.1f,.05f), ticks + 1); reject.Observe(LegSide.Left, Vector3.Zero, ticks + 2); reject.Observe(LegSide.Right, Vector3.Zero, ticks + 3); }
    Assert(reject.Update(ticks).TargetSpeed == 0, "Bilateral PS Move bend motion must not activate locomotion.");
}
static async Task PsMoveAssignmentsRoundTrip()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-move-assign-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new PsMoveAssignmentStore(Path.Combine(root, "assignments.json"));
        await store.SaveAsync("00:07:04:1e:fc:1e", "00-06-f7-17-3e-9c");
        var loaded = await store.LoadAsync();
        Assert(loaded is { IsComplete: true, LeftStableId: "0007041EFC1E", RightStableId: "0006F7173E9C" }, "Stable PS Move assignments did not round-trip.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
static void ParseImu() { var report = new byte[49]; report[0] = 0x30; report[13] = 0x00; report[14] = 0x10; var samples = JoyConReportParser.ParseStandardFullReport(report, "joycon-left", 7, System.Diagnostics.Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow); Assert(samples.Count == 3, "Expected 3 sub-samples."); Assert(Math.Abs(samples[0].AccelerationG.X - 1f) < 0.001f, "Expected 1g X."); Assert(samples[2].Sequence == 9, "Sequence must increment."); }
static void InvalidReport() { try { JoyConReportParser.ParseStandardFullReport(new byte[49], "x", 0, 0, DateTimeOffset.UtcNow); throw new InvalidOperationException("Expected rejection."); } catch (ArgumentException) { } }
static void ParseCalibration() { var data = new byte[24]; data[0] = 1; data[6] = 2; data[12] = 3; data[18] = 4; var cal = JoyConImuCalibration.ParseFactory(data); Assert(cal.AccelOrigin.X == 1 && cal.AccelSensitivity.X == 2 && cal.GyroOrigin.X == 3 && cal.GyroSensitivity.X == 4, "Calibration mapping mismatch."); }
static void ScaleCalibration() { var cal = new JoyConImuCalibration(Vector3.Zero, new(16384), Vector3.Zero, new(13371)); Assert(Math.Abs(cal.ConvertAcceleration(new(4096)).X - 1f) < 0.001f, "Acceleration scale mismatch."); Assert(Math.Abs(cal.ConvertAngularVelocity(new(13371)).X - 936f) < 0.001f, "Gyro scale mismatch."); }
static void PhoneLoss() { var d = new SequenceDiagnostics(); d.Observe(4); d.Observe(7); d.Observe(6); Assert(d.Received == 3 && d.Missing == 2 && d.OutOfOrder == 1, "Phone loss metrics mismatch."); }
static void OwoRotation() { var b = new byte[28]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b, 1); System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(b.AsSpan(4), 9); WriteFloat(b.AsSpan(12), 0); WriteFloat(b.AsSpan(16), 0); WriteFloat(b.AsSpan(20), 0); WriteFloat(b.AsSpan(24), 1); Assert(OwoTrackPacketParser.TryParse(b, out var p) && p.Sequence == 9 && p.Rotation.W == 1, "owoTrack rotation mismatch."); var body = PhoneMounting.ToBodyFrame(new Vector3(2, 3, 4)); Assert(body == new Vector3(-3, 2, 4), "Landscape top-left phone mounting transform mismatch."); }
static void BalanceBoardDerivesCop() { var b = new BalanceBoardSample("board", 1, new(1, DateTimeOffset.UnixEpoch), 10, 30, 10, 30); Assert(b.TotalKg == 80 && b.LeftKg == 20 && b.RightKg == 60, "Board load sums mismatch."); Assert(Math.Abs(b.CenterOfPressureX - 0.5f) < 0.001f && b.CenterOfPressureY == 0 && b.HasStableContact(), "Board CoP mismatch."); }
static void BalanceBoardProtocol()
{
    var calibrationBytes = new byte[32];
    for (var sensor = 0; sensor < 4; sensor++) { System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(calibrationBytes.AsSpan(4 + sensor * 2), 1000); System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(calibrationBytes.AsSpan(12 + sensor * 2), 2000); System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(calibrationBytes.AsSpan(20 + sensor * 2), 3000); }
    var calibration = BalanceBoardCalibration.Parse(calibrationBytes); var payload = new byte[11];
    for (var sensor = 0; sensor < 4; sensor++) System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(sensor * 2), 2000); payload[8] = 25; payload[10] = 0x82;
    var raw = BalanceBoardPacketParser.ParseExtensionPayload(payload); var sample = raw.ToSample(calibration, "board", 1, new(1, DateTimeOffset.UnixEpoch));
    Assert(raw.Temperature == 25 && raw.BatteryLevel == 0x82 && Math.Abs(sample.TotalKg - 68) < 0.001f && sample.CenterOfPressureX == 0, "Balance Board protocol conversion mismatch.");
}
static PersonalBoardMotion TestBoardProfile() => new(-.30, .18, .859, 1.009, 1.33, -.22, 10, -.50, .50, .55, .65);
static BalanceBoardSample BoardAt(long ticks, float copX)
{
    var left = 50 * (1 - copX); var right = 50 * (1 + copX);
    return new("board", ticks, new(ticks, DateTimeOffset.UtcNow), left * .30f, right * .30f, left * .70f, right * .70f);
}
static void BoardHoldGestureTurns()
{
    var f = new SensorFusionEngine(boardProfile: TestBoardProfile(), allowBoardOnly: true, allowBoardTurn: true); var t = System.Diagnostics.Stopwatch.GetTimestamp();
    f.ObserveBoard(BoardAt(t, .65f)); f.ObserveBoard(BoardAt(t + (long)(.60 * System.Diagnostics.Stopwatch.Frequency), .65f));
    var s = f.Update(t + (long)(.60 * System.Diagnostics.Stopwatch.Frequency));
    Assert(s.TurnTarget > .6 && s.TargetSpeed == 0, "A held right lean must turn right without forward motion.");
    f.ObserveBoard(BoardAt(t + (long)(.70 * System.Diagnostics.Stopwatch.Frequency), 0));
    Assert(f.Update(t + (long)(.70 * System.Diagnostics.Stopwatch.Frequency)).TurnTarget == 0, "Returning to center must stop turning.");
}
static void BoardWalkingDoesNotTurn()
{
    var f = new SensorFusionEngine(boardProfile: TestBoardProfile(), allowBoardOnly: true); var t = System.Diagnostics.Stopwatch.GetTimestamp();
    for (var i = 0; i < 6; i++) { t += (long)(.55 * System.Diagnostics.Stopwatch.Frequency); f.ObserveBoard(BoardAt(t, i % 2 == 0 ? -.65f : .65f)); }
    var s = f.Update(t); Assert(s.TargetSpeed > 0 && s.TurnTarget == 0, "Alternating board steps must walk, never turn.");
}
static void WriteFloat(Span<byte> target, float value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(target, BitConverter.SingleToInt32Bits(value));
static void TorsoCannotStart() { var g = new GaitEngine(); for (var i = 0; i < 100; i++) g.ObservePhoneRhythm(1); Assert(g.Update(System.Diagnostics.Stopwatch.GetTimestamp()).State == GaitState.Idle, "Phone/torso must not start gait."); }
static void ExperimentalPhoneOnlyIsExplicitlyGated()
{
    var profile = new PersonalPhoneMotion(.062, .530, .820, 1, 11.61, 15.95, 49.76); var f = new SensorFusionEngine(phoneProfile: profile, allowPhoneOnly: true); var t = System.Diagnostics.Stopwatch.GetTimestamp();
    f.ObservePhoneMotion(.9, 16, t, 0); Assert(f.Update(t).TargetSpeed == 0, "Phone-only must not start on one movement sample.");
    for (var i = 1; i <= 40; i++) f.ObservePhoneMotion(.9, 16, t + i * System.Diagnostics.Stopwatch.Frequency / 100, 0);
    Assert(f.Update(t + System.Diagnostics.Stopwatch.Frequency * 4 / 10).TargetSpeed > 0, "Sustained phone motion should start experimental mode.");
    Assert(f.Update(t + System.Diagnostics.Stopwatch.Frequency).TargetSpeed == 0, "Stale phone motion must stop.");
}
static void BilateralMotionRejected() { var g = new GaitEngine(); var t = System.Diagnostics.Stopwatch.GetTimestamp(); for (var i = 0; i < 6; i++) { t += System.Diagnostics.Stopwatch.Frequency / 2; g.ObserveLeg(LegSide.Left, 140, t); g.ObserveLeg(LegSide.Right, 140, t + 1); g.ObserveLeg(LegSide.Left, 0, t + 2); g.ObserveLeg(LegSide.Right, 0, t + 3); } Assert(g.Update(t).State == GaitState.Idle && g.Update(t).Confidence == 0, "Bilateral motion must reset gait confidence."); }
static void SingleLegRejected() { var g = new GaitEngine(); var t = System.Diagnostics.Stopwatch.GetTimestamp(); for (var i = 0; i < 5; i++) { t += System.Diagnostics.Stopwatch.Frequency / 2; g.ObserveLeg(LegSide.Left, 150, t); g.ObserveLeg(LegSide.Left, 0, t + 1); } var snapshot = g.Update(t); Assert(snapshot.State == GaitState.Idle && snapshot.StepCount <= 2, "Repeated single-leg motion must not become walking or inflate the step count."); }
static void AlternatingLegsStart() { var g = new GaitEngine(); var t = System.Diagnostics.Stopwatch.GetTimestamp(); for (var i = 0; i < 4; i++) { g.ObserveLeg(i % 2 == 0 ? LegSide.Left : LegSide.Right, 120, t += System.Diagnostics.Stopwatch.Frequency / 2); g.ObserveLeg(i % 2 == 0 ? LegSide.Left : LegSide.Right, 0, t + 1); } var s = g.Update(t); Assert(s.State is GaitState.Walking or GaitState.FastWalk or GaitState.Running && s.TargetSpeed > 0, "Alternating gait should become active."); }
static void NaturalCadenceContinuity()
{
    var g = new GaitEngine(); var t = System.Diagnostics.Stopwatch.GetTimestamp(); var dt = System.Diagnostics.Stopwatch.Frequency / 100; var activeSeen = false; var pulses = 0;
    for (var i = 0; i < 800; i++)
    {
        t += dt; var phase = i / 100d * Math.PI * 2;
        g.ObserveLeg(LegSide.Left, Math.Max(0, Math.Sin(phase)) * 150, t);
        g.ObserveLeg(LegSide.Right, Math.Max(0, Math.Sin(phase + Math.PI)) * 150, t);
        var s = g.Update(t); if (s.TargetSpeed > 0) activeSeen = true; else if (activeSeen && i < 760) pulses++;
    }
    Assert(activeSeen && pulses == 0, $"Natural cadence pulsed {pulses} times.");
    Assert(g.Update(t + (long)(System.Diagnostics.Stopwatch.Frequency * .4)).TargetSpeed == 0, "A real stop must zero target within 400 ms.");
}
static void ThresholdHysteresisRejectsChatter() { var g = new GaitEngine(56); var t = System.Diagnostics.Stopwatch.GetTimestamp(); Assert(g.ObserveLeg(LegSide.Left, 60, t), "Initial rise expected."); Assert(!g.ObserveLeg(LegSide.Left, 50, t + 1), "A small dip must not release the swing."); Assert(!g.ObserveLeg(LegSide.Left, 60, t + 2), "Threshold chatter must not create a second step."); g.ObserveLeg(LegSide.Left, 20, t + 3); Assert(!g.ObserveLeg(LegSide.Left, 60, t + System.Diagnostics.Stopwatch.Frequency / 2), "Same-leg rebound must remain suppressed."); }
static void SwingAmplitudeControlsPace()
{
    static double Run(double peak)
    {
        var g = new GaitEngine(); var t = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var i = 0; i < 8; i++) { var side = i % 2 == 0 ? LegSide.Left : LegSide.Right; t += System.Diagnostics.Stopwatch.Frequency / 2; g.ObserveLeg(side, 60, t); g.ObserveLeg(side, peak, t + 1); g.ObserveLeg(side, 0, t + 2); }
        return g.Update(t).TargetSpeed;
    }
    Assert(Run(175) > Run(75) + 0.15, "Swing amplitude must materially affect pace at equal cadence.");
}
static async Task LearnedPacePrior()
{
    var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models", "deepgait-pace-v1.json"));
    var prior = await GaitPacePrior.LoadAsync(path);
    var slow = prior.EstimateAnalogPace(1.2, 80); var fast = prior.EstimateAnalogPace(2.4, 180);
    Assert(slow is >= 0.5 and <= 1 && fast is >= 0.5 and <= 1 && fast > slow + 0.15, "Learned pace prior must preserve a meaningful speed range.");
}
static void GaitStops() { var g = new GaitEngine(); var t = System.Diagnostics.Stopwatch.GetTimestamp(); g.ObserveLeg(LegSide.Left, 120, t); g.ObserveLeg(LegSide.Left, 0, t + 1); g.ObserveLeg(LegSide.Right, 120, t += System.Diagnostics.Stopwatch.Frequency / 2); Assert(g.Update(t + (long)(System.Diagnostics.Stopwatch.Frequency * 0.65)).State == GaitState.Stopping, "Expected prompt stopping hysteresis."); Assert(g.Update(t + System.Diagnostics.Stopwatch.Frequency * 2).State == GaitState.Idle, "Expected idle after stale data."); }
static void OptionalFusionCannotStart() { var f = new SensorFusionEngine(); var t = System.Diagnostics.Stopwatch.GetTimestamp(); f.ObservePhoneRhythm(1, t); f.ObserveBoard(new("board", 1, new(t, DateTimeOffset.UtcNow), 20, 20, 20, 20)); var s = f.Update(t); Assert(s.TargetSpeed == 0 && s.Gait.State == GaitState.Idle, "Phone and board must never create locomotion without leg evidence."); }
static void StaleOptionalSensorsDoNotBlock() { var f = new SensorFusionEngine(); var t = System.Diagnostics.Stopwatch.GetTimestamp(); f.ObservePhoneRhythm(1, t); f.ObserveBoard(new("board", 1, new(t, DateTimeOffset.UtcNow), 20, 20, 20, 20)); for (var i = 0; i < 4; i++) { var side = i % 2 == 0 ? LegSide.Left : LegSide.Right; t += System.Diagnostics.Stopwatch.Frequency / 2; f.ObserveLeg(side, 140, t); f.ObserveLeg(side, 0, t + 1); } var s = f.Update(t); Assert(!s.PhoneFresh && !s.BoardFresh && s.TargetSpeed > 0, "Optional stale sensors must degrade rather than block Joy-Con gait."); }
static void SpeedIsSmoothed() { var s = new LocomotionSmoother(); var first = s.Update(1, TimeSpan.FromMilliseconds(100)); Assert(first > 0 && first < 1, "Acceleration must be smooth."); var down = s.Update(0, TimeSpan.FromMilliseconds(100)); Assert(down >= 0 && down < first, "Deceleration must be smooth."); }
static async Task VrSessionResponseContract()
{
    var sink = new TestOutputSink(); await using var session = new VrLocomotionSession(sink); await session.StartAsync();
    var active = new FusionSnapshot(new(GaitState.Walking, 2, .9, .8, LegSide.Right, 4), .9, .8, false, false, false, 0);
    for (var i = 0; i < 20; i++) await session.UpdateAsync(active, TimeSpan.FromMilliseconds(10));
    Assert(sink.Values.All(x => x.X == 0), "Locomotion must never introduce sideways drift.");
    Assert(sink.Values[^1].Y >= .55f, "Walking must reach a useful speed within 200 ms.");
    var stopped = active with { Gait = active.Gait with { State = GaitState.Idle, TargetSpeed = 0 }, TargetSpeed = 0 };
    for (var i = 0; i < 10; i++) await session.UpdateAsync(stopped, TimeSpan.FromMilliseconds(10));
    Assert(sink.Values[^1].Y == 0, "A real stop must reach zero within 100 ms.");
}
static async Task LogRetentionEnforcesBudget()
{
    var directory = Path.Combine(Path.GetTempPath(), $"niirmotion-retention-{Guid.NewGuid():N}"); Directory.CreateDirectory(directory);
    try
    {
        for (var i = 0; i < 6; i++) { var path = Path.Combine(directory, $"{i}.csv"); await File.WriteAllBytesAsync(path, new byte[100]); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i)); }
        var remaining = StorageRetention.EnforceDirectoryBudget(directory, 350, keepNewest: 2);
        Assert(remaining <= 350 && Directory.GetFiles(directory).Length == 3, "Oldest logs should be removed until the budget is met.");
        Assert(File.Exists(Path.Combine(directory, "5.csv")) && File.Exists(Path.Combine(directory, "4.csv")), "Newest protected logs must remain.");
    }
    finally { Directory.Delete(directory, true); }
}
static void AlyxPhysicalForwardOverride()
{
    const string input = """{"bindings":{"/actions/move":{"sources":[{"path":"/user/hand/left/input/joystick","inputs":{"click":{"output":"/actions/move/in/adjustheight"},"position":{"output":"/actions/move/in/teleportturn"}}},{"path":"/user/hand/left/input/joystick","inputs":{"click":{"output":"/actions/move/in/walk"}}}]}}}""";
    var output = AlyxBindingOverride.RemovePhysicalForwardVector(input);
    using var json = JsonDocument.Parse(output); var sources = json.RootElement.GetProperty("bindings").GetProperty("/actions/move").GetProperty("sources");
    Assert(!sources[0].GetProperty("inputs").TryGetProperty("position", out _), "Physical forward vector must be removed in NiiMotion mode.");
    Assert(sources[0].GetProperty("inputs").GetProperty("click").GetProperty("output").GetString() == "/actions/move/in/adjustheight", "Controller click must remain intact.");
    Assert(sources[1].GetProperty("inputs").GetProperty("click").GetProperty("output").GetString() == "/actions/move/in/walk", "Controller walk button must remain intact.");
}
static void Arizona2PhysicalMovementOverride()
{
    const string input = """{"bindings":{"/actions/vertigo":{"sources":[{"path":"/user/hand/left/input/joystick","inputs":{"click":{"output":"/actions/vertigo/in/axis0_press"},"position":{"output":"/actions/vertigo/in/axis0_axis2d"},"touch":{"output":"/actions/vertigo/in/axis0_touch"}}},{"path":"/user/hand/right/input/joystick","inputs":{"click":{"output":"/actions/vertigo/in/axis0_press"},"position":{"output":"/actions/vertigo/in/axis0_axis2d"}}}]}}}""";
    var output = AlyxBindingOverride.RemoveArizonaSunshine2PhysicalMovement(input); using var json = JsonDocument.Parse(output);
    var sources = json.RootElement.GetProperty("bindings").GetProperty("/actions/vertigo").GetProperty("sources");
    Assert(!sources[0].GetProperty("inputs").TryGetProperty("position", out _) && !sources[1].GetProperty("inputs").TryGetProperty("position", out _), "Both physical movement vectors must be removed.");
    Assert(sources[0].GetProperty("inputs").TryGetProperty("click", out _) && sources[0].GetProperty("inputs").TryGetProperty("touch", out _), "Controller click and touch must remain intact.");
}
static void CalibrationQualitySegments()
{
    var points = new List<CalibrationStreamPoint>();
    for (var tenth = 0; tenth < 300; tenth++)
    {
        var seconds = tenth / 10d;
        points.Add(new("left", tenth, seconds));
        if (seconds is < 10 or >= 20) points.Add(new("right", tenth, seconds));
    }
    var report = CalibrationQualityAnalyzer.Analyze(points, 30, 10);
    Assert(report.Segments.Count == 3, "Recording was not split into fixed quality segments.");
    Assert(!report.Segments[0].NeedsRedo && report.Segments[1].NeedsRedo && !report.Segments[2].NeedsRedo, "Only the broken sensor interval should require a redo.");
    Assert(report.RedoSegments.Single().StartSeconds == 10, "Wrong redo interval was reported.");
}
static async Task CalibrationSegmentRepair()
{
    var root = Path.Combine(Path.GetTempPath(), "niirmotion-repair-" + Guid.NewGuid().ToString("N"));
    var originalFolder = Path.Combine(root, "phase-2-original"); var repairFolder = Path.Combine(root, "repair"); Directory.CreateDirectory(originalFolder); Directory.CreateDirectory(repairFolder);
    try
    {
        static string Row(long sequence, double seconds) => JsonSerializer.Serialize(new { sequence, timestamp = new { monotonicTicks = (long)(seconds * Stopwatch.Frequency), receivedAtUtc = DateTimeOffset.UnixEpoch } });
        foreach (var stream in new[] { "left", "right" })
        {
            var originalRows = Enumerable.Range(0, 300).Where(i => stream == "left" || i < 100 || i >= 200).Select(i => Row(i, i / 10d));
            await File.WriteAllLinesAsync(Path.Combine(originalFolder, stream + ".jsonl"), originalRows);
            await File.WriteAllLinesAsync(Path.Combine(repairFolder, stream + ".jsonl"), Enumerable.Range(0, 100).Select(i => Row(i, 100 + i / 10d)));
        }
        await File.WriteAllTextAsync(Path.Combine(originalFolder, "manifest.json"), "{\"version\":1}");
        var quality = GuidedCalibrationRecorder.AnalyzeFolder(originalFolder, TimeSpan.FromSeconds(30)); var broken = quality.RedoSegments.Single();
        var original = new GuidedCalibrationResult(SensorFamily.JoyCon, 2, TimeSpan.FromSeconds(30), new Dictionary<string, int>(), originalFolder, quality);
        var repairQuality = GuidedCalibrationRecorder.AnalyzeFolder(repairFolder, TimeSpan.FromSeconds(10));
        var repair = new GuidedCalibrationResult(SensorFamily.JoyCon, 2, TimeSpan.FromSeconds(10), new Dictionary<string, int>(), repairFolder, repairQuality);
        var result = await new CalibrationSegmentRepairService().ReplaceAsync(original, broken, repair);
        Assert(result.Quality.IsClean, "Repaired calibration should pass segment quality checks.");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(originalFolder, "manifest.json")));
        Assert(manifest.RootElement.GetProperty("superseded").GetBoolean(), "Original calibration was not marked as superseded.");
        Assert(File.ReadLines(Path.Combine(result.Folder, "right.jsonl")).Count() == 300, "Replacement did not restore the missing stream interval.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
static void CalibrationRejectsIncomplete() { try { new GaitCalibrationAccumulator().Complete(); throw new InvalidOperationException("Expected incomplete calibration rejection."); } catch (InvalidOperationException ex) when (ex.Message.Contains("100")) { } }
static async Task CalibrationRoundTrip() { var a = new GaitCalibrationAccumulator(); for (var i = 0; i < 120; i++) { a.ObserveRest(LegSide.Left, 2 + i % 3); a.ObserveRest(LegSide.Right, 3 + i % 2); } for (var i = 0; i < 8; i++) a.ObserveStep(i * 0.5); var p = a.Complete(); await using var s = new MemoryStream(); var store = new CalibrationStore(); await store.SaveAsync(p, s); s.Position = 0; var loaded = await store.LoadAsync(s); Assert(loaded.Version == 1 && loaded.RecommendedLegThresholdDps >= 35 && loaded.ObservedCadenceMaxHz == 2, "Calibration round-trip mismatch."); }
static (string, Func<Task>) Sync(string name, Action action) => (name, () => { action(); return Task.CompletedTask; });
static async Task RecordingRoundTrip()
{
    var original = new JoyConImuSample("joycon-left", 12, new SensorTimestamp(1000, DateTimeOffset.UnixEpoch), Vector3.One, new Vector3(2, 3, 4), 1);
    await using var stream = new MemoryStream(); var recorder = new JsonLinesSensorRecorder(); await recorder.RecordAsync(One(original), stream); stream.Position = 0;
    var replayed = new List<JoyConImuSample>(); await foreach (var sample in new JoyConReplayReader().ReadAsync(stream, 1000)) replayed.Add(sample);
    Assert(replayed.Count == 1 && replayed[0].Sequence == 12 && replayed[0].AccelerationG == Vector3.One, "Replay content mismatch.");
}
static async Task BalanceBoardRecordingRoundTrip()
{
    var original = new BalanceBoardSample("board", 3, new SensorTimestamp(500, DateTimeOffset.UnixEpoch), 11, 12, 13, 14);
    await using var stream = new MemoryStream(); await new JsonLinesSensorRecorder().RecordAsync(OneBoard(original), stream); stream.Position = 0;
    var replayed = new List<BalanceBoardSample>(); await foreach (var sample in new BalanceBoardReplayReader().ReadAsync(stream, 1000)) replayed.Add(sample);
    Assert(replayed.Count == 1 && replayed[0].TotalKg == 50 && replayed[0].Sequence == 3, "Balance Board replay mismatch.");
}
static async IAsyncEnumerable<BalanceBoardSample> OneBoard(BalanceBoardSample sample) { yield return sample; await Task.CompletedTask; }
static async IAsyncEnumerable<JoyConImuSample> One(JoyConImuSample sample) { yield return sample; await Task.CompletedTask; }
static async Task PhoneUdpRoundTrip()
{
    const string token = "test-token-123456"; var port = Random.Shared.Next(20000, 40000); await using var source = new PhoneSensorSource(token, port); await source.StartAsync();
    using var sender = new UdpClient(); var rejected = new PhonePacket(1, "wrong-token-123", "phone", 1, 1, [0,0,0,1], [0,0,9.81f], [0,0,0]); var accepted = rejected with { SessionToken = token, Sequence = 2 };
    await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(rejected), "127.0.0.1", port); await sender.SendAsync(JsonSerializer.SerializeToUtf8Bytes(accepted), "127.0.0.1", port);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); var sample = await source.Samples.ReadAsync(timeout.Token); Assert(sample.Sequence == 2 && sample.Orientation.W == 1, "Phone UDP packet mismatch.");
}
static async Task VrOutputLifecycle()
{
    var sink = new TestOutputSink(); await using var output = new VrOutputController(sink); await output.StartAsync(); await output.SetAsync(new(2, -2)); await output.StopAsync();
    Assert(sink.Values.SequenceEqual([LocomotionVector.Zero, new(1, -1), LocomotionVector.Zero]), "Output must start/stop at zero and clamp values."); Assert(!sink.IsAttached, "Output must detach on stop.");
}
static async Task VrOutputOffRejects()
{
    await using var output = new VrOutputController(new TestOutputSink()); try { await output.SetAsync(new(0, 1)); throw new InvalidOperationException("Expected OFF rejection."); } catch (InvalidOperationException ex) when (ex.Message.Contains("OFF")) { }
}
static async Task VrOutputFailureDetaches()
{
    var sink = new TestOutputSink { FailOnNonZero = true }; await using var output = new VrOutputController(sink); await output.StartAsync(); try { await output.SetAsync(new(0, 1)); } catch (IOException) { }
    Assert(!output.IsEnabled && !sink.IsAttached && sink.Values[^1] == LocomotionVector.Zero, "Failure must zero and detach output.");
}
static async Task FusedGaitDrivesOutput()
{
    var sink = new TestOutputSink(); await using var session = new VrLocomotionSession(sink); await session.StartAsync();
    var gait = new GaitSnapshot(GaitState.Walking, 2, 0.8, 0.7, LegSide.Right, 4); var fusion = new FusionSnapshot(gait, 0.8, 0.7, false, false, false, 0);
    await session.UpdateAsync(fusion, TimeSpan.FromMilliseconds(100)); await session.StopAsync();
    Assert(sink.Values.Count >= 3 && sink.Values[0] == LocomotionVector.Zero && sink.Values.Any(x => x.Y > 0) && sink.Values[^1] == LocomotionVector.Zero && !sink.IsAttached, "Fused output lifecycle mismatch.");
}
static async Task BoardTurnDrivesHorizontalOutput()
{
    var sink = new TestOutputSink(); await using var session = new VrLocomotionSession(sink); await session.StartAsync();
    var idle = new GaitSnapshot(GaitState.Idle, 0, 0, 0, null, 0);
    var turn = new FusionSnapshot(idle, 0, 0, false, true, true, 0, .65);
    for (var i = 0; i < 20; i++) await session.UpdateAsync(turn, TimeSpan.FromMilliseconds(10));
    Assert(sink.Values.Any(x => x.X > .4f) && sink.Values.All(x => x.Y == 0), "Board turn must drive horizontal turn without forward motion.");
}
static async Task NamedPipeOutputProtocol()
{
    var name = $"NiiRMotion.Tests.{Guid.NewGuid():N}"; using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.In, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
    var stage = "connect"; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await using var sink = new NamedPipeVrOutputSink(name); try { var accept = Task.Run(server.WaitForConnection, timeout.Token); await sink.AttachAsync(timeout.Token); await accept.WaitAsync(timeout.Token); stage = "transfer"; var bytes = new byte[12]; var write = sink.WriteAsync(new(0.25f, -0.75f), timeout.Token).AsTask(); var read = server.ReadExactlyAsync(bytes, timeout.Token).AsTask(); await Task.WhenAll(write, read); Assert(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0x31524D4E, "Pipe magic mismatch."); Assert(BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4))) == 0.25f, "Pipe X mismatch."); Assert(BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8))) == -0.75f, "Pipe Y mismatch."); } catch (OperationCanceledException) { throw new InvalidOperationException($"Pipe test timed out during {stage}."); }
}
static void NativeDriverExportsFactory()
{
    if (!OperatingSystem.IsWindows()) return; var path = Path.Combine(Environment.CurrentDirectory, "native", "openvr-driver", "dist", "bin", "win64", "driver_niirmotion.dll"); Assert(File.Exists(path), "Native driver DLL missing.");
    var module = System.Runtime.InteropServices.NativeLibrary.Load(path); try { Assert(System.Runtime.InteropServices.NativeLibrary.TryGetExport(module, "HmdDriverFactory", out _), "HmdDriverFactory export missing."); } finally { System.Runtime.InteropServices.NativeLibrary.Free(module); }
}
static void NativeDriverPoseContract()
{
    var path = Path.Combine(Environment.CurrentDirectory, "native", "openvr-driver", "driver.cpp"); var source = File.ReadAllText(path);
    Assert(source.Contains("TrackingResult_Running_OK") && source.Contains("TrackedDevicePoseUpdated") && source.Contains("poseIsValid = true"), "Treadmill must remain an active, valid stationary SteamVR input source.");
    Assert(source.Contains("yawRate >= 1.60") && source.Contains("turnSuppressUntil_"), "HMD turning must suppress only unmistakable fast turns without weakening normal walking.");
    Assert(source.Contains("/input/turnstick/x") && source.Contains("turnXHandle_"), "Board turn must use a dedicated SteamVR turn axis.");
}
static void AlyxBindingContract()
{
    var path = Path.Combine(Environment.CurrentDirectory, "native", "openvr-driver", "dist", "resources", "input", "default_bindings", "steam.app.546560_niirmotion.json"); using var json = JsonDocument.Parse(File.ReadAllText(path)); var text = json.RootElement.GetRawText();
    Assert(text.Contains("/user/treadmill/input/joystick", StringComparison.OrdinalIgnoreCase), "Treadmill source missing."); Assert(text.Contains("/actions/move/in/teleportturn", StringComparison.OrdinalIgnoreCase), "Alyx movement vector missing."); Assert(text.Contains("/actions/move/in/walk", StringComparison.OrdinalIgnoreCase), "Alyx walk activation missing.");
    var options = json.RootElement.GetProperty("options");
    Assert(options.GetProperty("returnBindingsWithLeftHand").GetBoolean() && !options.GetProperty("returnBindingsWithRightHand").GetBoolean(), "Treadmill binding must preserve the physical right-hand turn controller.");
}
static void Arizona2BindingContract()
{
    var path = Path.Combine(Environment.CurrentDirectory, "native", "openvr-driver", "dist", "resources", "input", "default_bindings", "steam.app.1540210_niirmotion.json");
    using var json = JsonDocument.Parse(File.ReadAllText(path)); var text = json.RootElement.GetRawText();
    Assert(text.Contains("/user/treadmill/input/joystick", StringComparison.OrdinalIgnoreCase), "Arizona 2 treadmill source missing.");
    Assert(text.Contains("/actions/vertigo/in/axis0_axis2d", StringComparison.OrdinalIgnoreCase), "Arizona 2 movement vector missing.");
    Assert(text.Contains("/actions/vertigo/in/axis0_press", StringComparison.OrdinalIgnoreCase), "Arizona 2 sprint activation missing.");
}
static async Task<int> HardwareSmokeAsync()
{
    var devices = HidDeviceEnumerator.FindJoyCons();
    Console.WriteLine($"Detected Joy-Cons: {devices.Count}");
    if (devices.Count == 0) foreach (var path in HidDeviceEnumerator.FindAllHidPaths().Where(x => x.Contains("057e", StringComparison.OrdinalIgnoreCase))) Console.WriteLine($"  Nintendo HID path: {path}");
    foreach (var device in devices) Console.WriteLine($"  {device.Side} {device.VendorId:X4}:{device.ProductId:X4}");
    if (!devices.Any(x => x.Side == JoyConSide.Left) || !devices.Any(x => x.Side == JoyConSide.Right)) { Console.Error.WriteLine("Both original Joy-Cons are required."); return 2; }
    var failures = 0;
    foreach (var device in devices.GroupBy(x => x.Side).Select(x => x.First()))
    {
        try
        {
            await using var source = new JoyConSensorSource(device);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            await source.StartAsync(timeout.Token); Console.WriteLine($"{device.Side}: HID input={source.InputReportLength}, output={source.OutputReportLength}, calibration={source.FactoryCalibration}"); var captured = new List<JoyConImuSample>();
            try { await foreach (var sample in source.Samples.ReadAllAsync(timeout.Token)) { captured.Add(sample); if (captured.Count >= 300) break; } } catch (OperationCanceledException) { }
            var count = captured.Count;
            var timing = source.Timing;
            Console.WriteLine($"{device.Side}: samples={count}, rate={timing.SampleRateHz:F1}Hz, jitter={timing.JitterMs:F2}ms, age={timing.PacketAgeMs:F1}ms");
            await using var recording = new MemoryStream(); await new JsonLinesSensorRecorder().RecordAsync(ToAsync(captured), recording); recording.Position = 0; var replayCount = 0; await foreach (var _ in new JoyConReplayReader().ReadAsync(recording, 1000)) replayCount++;
            Console.WriteLine($"{device.Side}: real-recording bytes={recording.Length}, replayed={replayCount}");
            if (count == 0 || replayCount != count) failures++;
        }
        catch (Exception ex) { failures++; Console.Error.WriteLine($"{device.Side}: {ex}"); }
    }
    return failures == 0 ? 0 : 3;
}
static async IAsyncEnumerable<JoyConImuSample> ToAsync(IEnumerable<JoyConImuSample> samples) { foreach (var sample in samples) yield return sample; await Task.CompletedTask; }
static async Task<int> CapturePhoneAsync()
{
    using var listener = new UdpClient(PhoneSensorSource.DefaultPort); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    Console.WriteLine($"Listening for phone UDP on {PhoneSensorSource.DefaultPort}...");
    try
    {
        for (var i = 0; i < 40; i++)
        {
            var packet = await listener.ReceiveAsync(timeout.Token); var type = packet.Buffer.Length >= 4 ? packet.Buffer[3] : packet.Buffer[0];
            Console.WriteLine($"PHONE_PACKET remote={packet.RemoteEndPoint} type={type} length={packet.Buffer.Length} hex={Convert.ToHexString(packet.Buffer.AsSpan(0, Math.Min(packet.Buffer.Length, 96)))}");
            if (type == 3) { var hello = new byte[13]; hello[0] = 3; System.Text.Encoding.ASCII.GetBytes("Hey OVR =D 5").CopyTo(hello, 1); await listener.SendAsync(hello, packet.RemoteEndPoint, timeout.Token); }
            else if (type == 10) await listener.SendAsync(packet.Buffer, packet.RemoteEndPoint, timeout.Token);
        }
        return 0;
    }
    catch (OperationCanceledException) { Console.Error.WriteLine("No phone packets received before timeout."); return 5; }
}
static async Task<int> OwoTrackSmokeAsync()
{
    await using var source = new OwoTrackSensorSource(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); await source.StartAsync(timeout.Token); var samples = new List<PhoneImuSample>();
    try { await foreach (var sample in source.Samples.ReadAllAsync(timeout.Token)) { samples.Add(sample); if (samples.Count >= 120) break; } } catch (OperationCanceledException) { }
    var timing = source.Timing; Console.WriteLine($"owoTrack endpoint={source.PhoneEndpoint}, samples={samples.Count}, rate={timing.SampleRateHz:F1}Hz, jitter={timing.JitterMs:F2}ms, missing={source.MissingPackets}, outOfOrder={source.OutOfOrderPackets}");
    if (samples.Count > 0) Console.WriteLine($"latest orientation={samples[^1].Orientation}, accel={samples[^1].AccelerationMps2}, gyro={samples[^1].AngularVelocityRadps}");
    await using var recording = new MemoryStream(); await new JsonLinesSensorRecorder().RecordAsync(ToPhoneAsync(samples), recording); recording.Position = 0; var replayed = 0; await foreach (var _ in new PhoneReplayReader().ReadAsync(recording, 1000)) replayed++;
    Console.WriteLine($"phone real-recording bytes={recording.Length}, replayed={replayed}"); return samples.Count > 0 && replayed == samples.Count ? 0 : 6;
}
static async Task<int> GaitCalibrationAsync()
{
    var devices = HidDeviceEnumerator.FindJoyCons().GroupBy(x => x.Side).Select(x => x.First()).ToArray();
    if (!devices.Any(x => x.Side == JoyConSide.Left) || !devices.Any(x => x.Side == JoyConSide.Right)) { Console.Error.WriteLine("CALIBRATION_ERROR: Both Joy-Cons are required."); return 7; }
    var accumulator = new GaitCalibrationAccumulator(); var gait = new GaitEngine(); var sync = new object(); var phase = 0; var start = System.Diagnostics.Stopwatch.GetTimestamp();
    using var lifetime = new CancellationTokenSource();
    var readers = devices.Select(async device =>
    {
        await using var source = new JoyConSensorSource(device); await source.StartAsync(lifetime.Token); var side = device.Side == JoyConSide.Left ? LegSide.Left : LegSide.Right;
        await foreach (var sample in source.Samples.ReadAllAsync(lifetime.Token))
        {
            var magnitude = sample.AngularVelocityDps.Length();
            lock (sync)
            {
                if (phase == 0) accumulator.ObserveRest(side, magnitude);
                else if (phase == 1 && gait.ObserveLeg(side, magnitude, sample.Timestamp.MonotonicTicks)) accumulator.ObserveStep((sample.Timestamp.MonotonicTicks - start) / (double)System.Diagnostics.Stopwatch.Frequency);
            }
        }
    }).ToArray();
    if (OperatingSystem.IsWindows()) Console.Beep(700, 350);
    Console.WriteLine("REST_NOW"); await Task.Delay(TimeSpan.FromSeconds(8));
    phase = 1; start = System.Diagnostics.Stopwatch.GetTimestamp();
    if (OperatingSystem.IsWindows()) { Console.Beep(1050, 160); await Task.Delay(100); Console.Beep(1050, 160); }
    Console.WriteLine("WALK_NOW"); await Task.Delay(TimeSpan.FromSeconds(20));
    phase = 2; lifetime.Cancel();
    if (OperatingSystem.IsWindows()) Console.Beep(450, 600);
    try { await Task.WhenAll(readers); } catch (OperationCanceledException) { }
    try
    {
        GaitCalibrationProfile profile; lock (sync) profile = accumulator.Complete();
        var directory = Path.Combine(Environment.CurrentDirectory, "calibration"); Directory.CreateDirectory(directory); var path = Path.Combine(directory, "gait-v1.json"); await using var output = File.Create(path); await new CalibrationStore().SaveAsync(profile, output);
        Console.WriteLine($"CALIBRATION_OK path={path} threshold={profile.RecommendedLegThresholdDps:F2} cadence={profile.ObservedCadenceMinHz:F2}-{profile.ObservedCadenceMaxHz:F2}Hz leftNoise={profile.LeftRestMeanDps:F2}±{profile.LeftRestStdDevDps:F2} rightNoise={profile.RightRestMeanDps:F2}±{profile.RightRestStdDevDps:F2}"); return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine($"CALIBRATION_ERROR: {ex.Message}"); return 8; }
}
static async Task<int> MotionValidationAsync()
{
    var config = NiiMotionPaths.Config;
    var gaitPath = Path.Combine(config, "personal-gait-pace.json");
    var movePath = Path.Combine(config, "personal-psmove-training.json");
    var phonePath = Path.Combine(config, "personal-phone-motion.json");
    if (!File.Exists(gaitPath) || !File.Exists(movePath) || !File.Exists(phonePath)) { Console.Error.WriteLine("VALIDATION_ERROR: combined personal models are missing"); return 9; }
    var devices = HidDeviceEnumerator.FindJoyCons().GroupBy(x => x.Side).Select(x => x.First()).ToArray();
    if (!devices.Any(x => x.Side == JoyConSide.Left) || !devices.Any(x => x.Side == JoyConSide.Right)) { Console.Error.WriteLine("VALIDATION_ERROR: both Joy-Cons are required"); return 9; }
    var personal = await PersonalGaitPace.LoadAsync(gaitPath);
    var phoneProfile = await PersonalPhoneMotion.LoadAsync(phonePath);
    var moveProfile = JsonSerializer.Deserialize<PsMoveTrainingProfile>(await File.ReadAllTextAsync(movePath)) ?? throw new InvalidDataException("PS Move profile is empty.");
    var pacePath = Path.Combine(NiiMotionPaths.Models, "deepgait-pace-v1.json");
    var pace = File.Exists(pacePath) ? await GaitPacePrior.LoadAsync(pacePath) : null;
    var fusion = new SensorFusionEngine(56, pacePrior: pace, personalPace: personal, phoneProfile: phoneProfile);
    var moveGait = new PsMoveGaitEngine(moveProfile);
    var sync = new object(); var phase = -1; var active = new int[4]; var phoneFresh = new int[4]; var steps = new long[4]; var frames = new List<object>(24000); using var lifetime = new CancellationTokenSource();
    var hybridGate = new HybridGaitAgreementGate();
    var joySources = devices.Select(x => (Device: x, Source: new JoyConSensorSource(x))).ToArray();
    foreach (var item in joySources) await item.Source.StartAsync(lifetime.Token);
    var moveSource = new PsMoveSensorSource(NiiMotionPaths.PsMoveAssignments, NiiMotionPaths.PsMoveFactoryCalibration); await moveSource.StartAsync(lifetime.Token);
    var phoneSource = new OwoTrackSensorSource(); await phoneSource.StartAsync(lifetime.Token);
    var readers = joySources.Select(async item => { var side = item.Device.Side == JoyConSide.Left ? LegSide.Left : LegSide.Right; await foreach (var sample in item.Source.Samples.ReadAllAsync(lifetime.Token)) lock (sync) { fusion.ObserveLeg(side, sample.AngularVelocityDps.Length(), sample.Timestamp.MonotonicTicks); frames.Add(new { Phase = phase, Sensor = "joycon", Side = side, Sample = sample }); } }).ToList();
    readers.Add(Task.Run(async () => { await foreach (var sample in moveSource.Samples.ReadAllAsync(lifetime.Token)) lock (sync) { moveGait.Observe(sample); frames.Add(new { Phase = phase, Sensor = "psmove", Side = sample.Side, Sample = sample }); } }));
    readers.Add(Task.Run(async () => { await foreach (var sample in phoneSource.Samples.ReadAllAsync(lifetime.Token)) { var body = PhoneMounting.ToBodyFrame(sample); lock (sync) { fusion.ObservePhoneMotion(body.AngularVelocityRadps.Length(), body.AccelerationMps2.Length(), sample.Timestamp.MonotonicTicks, body.VerticalTurnRadps); frames.Add(new { Phase = phase, Sensor = "phone", Side = "body", Sample = body }); } } }));
    var evaluator = Task.Run(async () => { using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10)); while (await timer.WaitForNextTickAsync(lifetime.Token)) lock (sync) { var now = Stopwatch.GetTimestamp(); var primary = fusion.Update(now); var secondary = moveGait.Update(now); var snapshot = hybridGate.Combine(primary, secondary, now); var p = phase; if (p >= 0) { if (snapshot.TargetSpeed > .01) active[p]++; if (snapshot.PhoneFresh) phoneFresh[p]++; steps[p] = snapshot.Gait.StepCount; frames.Add(new { Phase = p, Sensor = "decision", Ticks = now, snapshot.TargetSpeed, snapshot.GlobalConfidence, snapshot.PhoneFresh, snapshot.Gait, Primary = primary.Gait, Secondary = secondary }); } } });
    Console.WriteLine("READY_STAND"); await Console.In.ReadLineAsync();
    if (OperatingSystem.IsWindows()) Console.Beep(700, 350);
    Console.WriteLine("VALIDATE_STAND"); await Task.Delay(TimeSpan.FromSeconds(5)); phase = -1;
    Console.WriteLine("READY_CROUCH_BEND"); await Console.In.ReadLineAsync();
    if (OperatingSystem.IsWindows()) { Console.Beep(500, 160); await Task.Delay(100); Console.Beep(500, 160); }
    Console.WriteLine("VALIDATE_CROUCH_BEND"); phase = 1; await Task.Delay(TimeSpan.FromSeconds(8)); phase = -1;
    Console.WriteLine("READY_WALK"); await Console.In.ReadLineAsync();
    if (OperatingSystem.IsWindows()) Console.Beep(750, 300);
    Console.WriteLine("VALIDATE_WALK");
    if (OperatingSystem.IsWindows()) { Console.Beep(1050, 160); await Task.Delay(100); Console.Beep(1050, 160); }
    phase = 2; await Task.Delay(TimeSpan.FromSeconds(10)); phase = -1;
    Console.WriteLine("READY_STOP"); await Console.In.ReadLineAsync();
    if (OperatingSystem.IsWindows()) Console.Beep(450, 600);
    Console.WriteLine("VALIDATE_STOP"); phase = 3; await Task.Delay(TimeSpan.FromSeconds(5)); lifetime.Cancel();
    if (OperatingSystem.IsWindows()) Console.Beep(850, 180);
    try { await Task.WhenAll(readers.Append(evaluator)); } catch (OperationCanceledException) { }
    foreach (var item in joySources) await item.Source.DisposeAsync(); await moveSource.DisposeAsync(); await phoneSource.DisposeAsync();
    var recordingDirectory = Path.Combine(Environment.CurrentDirectory, "recordings"); Directory.CreateDirectory(recordingDirectory); var recordingPath = Path.Combine(recordingDirectory, "latest-labeled-validation.jsonl");
    var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
    await using (var output = new StreamWriter(File.Create(recordingPath))) foreach (var item in frames) await output.WriteLineAsync(JsonSerializer.Serialize(item, jsonOptions));
    Console.WriteLine($"VALIDATION_RECORDING path={recordingPath} samples={frames.Count} bytes={new FileInfo(recordingPath).Length}");
    Console.WriteLine($"VALIDATION_RESULT standActive={active[0]} crouchActive={active[1]} walkActive={active[2]} stopActive={active[3]} standSteps={steps[0]} crouchSteps={steps[1]} walkSteps={steps[2]} stopSteps={steps[3]} phoneFreshWalk={phoneFresh[2]}");
    return active[0] == 0 && active[1] <= 5 && active[2] >= 200 && active[3] == 0 && phoneFresh[2] >= 500 ? 0 : 10;
}
static async Task<int> ReplayMotionValidationAsync()
{
    var recordingPath = Path.Combine(Environment.CurrentDirectory, "recordings", "latest-labeled-validation.jsonl");
    if (!File.Exists(recordingPath)) { Console.Error.WriteLine("REPLAY_ERROR: recording is missing"); return 12; }
    var gaitPath = Path.Combine(NiiMotionPaths.Config, "personal-gait-pace.json");
    var movePath = Path.Combine(NiiMotionPaths.Config, "personal-psmove-training.json");
    var phonePath = Path.Combine(NiiMotionPaths.Config, "personal-phone-motion.json");
    var personal = await PersonalGaitPace.LoadAsync(gaitPath);
    var phoneProfile = await PersonalPhoneMotion.LoadAsync(phonePath);
    var moveProfile = JsonSerializer.Deserialize<PsMoveTrainingProfile>(await File.ReadAllTextAsync(movePath)) ?? throw new InvalidDataException("PS Move profile is empty.");
    var pacePath = Path.Combine(NiiMotionPaths.Models, "deepgait-pace-v1.json");
    var pace = File.Exists(pacePath) ? await GaitPacePrior.LoadAsync(pacePath) : null;
    var fusion = new SensorFusionEngine(56, pacePrior: pace, personalPace: personal, phoneProfile: phoneProfile);
    var moveGait = new PsMoveGaitEngine(moveProfile); var gate = new HybridGaitAgreementGate();
    var options = new JsonSerializerOptions { IncludeFields = true }; var active = new int[4]; var decisions = new int[4];
    foreach (var line in File.ReadLines(recordingPath))
    {
        using var document = JsonDocument.Parse(line); var root = document.RootElement;
        var phase = root.GetProperty("Phase").GetInt32(); var sensor = root.GetProperty("Sensor").GetString();
        if (sensor == "joycon")
        {
            var sample = JsonSerializer.Deserialize<JoyConImuSample>(root.GetProperty("Sample"), options);
            var side = root.GetProperty("Side").GetInt32() == 0 ? LegSide.Left : LegSide.Right;
            fusion.ObserveLeg(side, sample.AngularVelocityDps.Length(), sample.Timestamp.MonotonicTicks);
        }
        else if (sensor == "psmove") moveGait.Observe(JsonSerializer.Deserialize<PsMoveImuSample>(root.GetProperty("Sample"), options));
        else if (sensor == "phone")
        {
            var sample = JsonSerializer.Deserialize<PhoneImuSample>(root.GetProperty("Sample"), options);
            fusion.ObservePhoneMotion(sample.AngularVelocityRadps.Length(), sample.AccelerationMps2.Length(), sample.Timestamp.MonotonicTicks, sample.AngularVelocityRadps.Y);
        }
        else if (sensor == "decision" && phase >= 0)
        {
            var ticks = root.GetProperty("Ticks").GetInt64(); var snapshot = gate.Combine(fusion.Update(ticks), moveGait.Update(ticks), ticks);
            decisions[phase]++; if (snapshot.TargetSpeed > .01) active[phase]++;
        }
    }
    Console.WriteLine($"REPLAY_RESULT standActive={active[0]}/{decisions[0]} crouchActive={active[1]}/{decisions[1]} walkActive={active[2]}/{decisions[2]} stopActive={active[3]}/{decisions[3]}");
    return active[0] == 0 && active[1] <= 5 && active[2] >= 200 && active[3] == 0 ? 0 : 12;
}
static async Task<int> VrOutputSmokeAsync()
{
    await using var output = new VrOutputController(new NamedPipeVrOutputSink()); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    try { await output.StartAsync(timeout.Token); Console.WriteLine("VR_OUTPUT_ATTACHED safeZero=true"); for (var i = 0; i < 10; i++) { await output.SetAsync(new(0, 0.10f), timeout.Token); await Task.Delay(20, timeout.Token); } await output.StopAsync(timeout.Token); Console.WriteLine("VR_OUTPUT_SMOKE_OK finalZero=true detached=true"); return 0; }
    catch (Exception ex) { Console.Error.WriteLine($"VR_OUTPUT_SMOKE_ERROR: {ex.Message}"); return 11; }
}
static async Task<int> WalkTuningCaptureAsync()
{
    var devices = HidDeviceEnumerator.FindJoyCons().GroupBy(x => x.Side).Select(x => x.First()).ToArray();
    if (!devices.Any(x => x.Side == JoyConSide.Left) || !devices.Any(x => x.Side == JoyConSide.Right)) { Console.Error.WriteLine("WALK_CAPTURE_ERROR: Both Joy-Cons are required."); return 12; }
    var calibrationPath = Path.Combine(Environment.CurrentDirectory, "calibration", "gait-v1.json");
    await using var calibrationInput = File.OpenRead(calibrationPath);
    var profile = await new CalibrationStore().LoadAsync(calibrationInput);
    var gait = new GaitEngine(profile.RecommendedLegThresholdDps); var sync = new object(); var phase = 0;
    var raw = new List<LabeledJoyConSample>(24000); var decisions = new List<WalkTuningDecision>(24000); var accepted = new List<(LegSide Side, long Ticks)>();
    using var lifetime = new CancellationTokenSource();
    var sources = devices.Select(x => (Device: x, Source: new JoyConSensorSource(x))).ToArray();
    foreach (var item in sources) await item.Source.StartAsync(lifetime.Token);
    var readers = sources.Select(async item =>
    {
        var side = item.Device.Side == JoyConSide.Left ? LegSide.Left : LegSide.Right;
        await foreach (var sample in item.Source.Samples.ReadAllAsync(lifetime.Token)) lock (sync)
        {
            if (phase != 1) continue;
            raw.Add(new LabeledJoyConSample("walk50", side, sample));
            if (gait.ObserveLeg(side, sample.AngularVelocityDps.Length(), sample.Timestamp.MonotonicTicks)) accepted.Add((side, sample.Timestamp.MonotonicTicks));
            var snapshot = gait.Update(sample.Timestamp.MonotonicTicks);
            decisions.Add(new WalkTuningDecision(sample.Timestamp.MonotonicTicks, snapshot.State, snapshot.CadenceHz, snapshot.Confidence, snapshot.TargetSpeed, snapshot.StepCount));
        }
    }).ToArray();
    await Task.Delay(TimeSpan.FromSeconds(3));
    if (OperatingSystem.IsWindows()) Console.Beep(1050, 250); phase = 1; Console.WriteLine("WALK_CAPTURE_STARTED duration=50s");
    await Task.Delay(TimeSpan.FromSeconds(50)); phase = 2;
    if (OperatingSystem.IsWindows()) Console.Beep(450, 650); lifetime.Cancel();
    try { await Task.WhenAll(readers); } catch (OperationCanceledException) { }
    foreach (var item in sources) await item.Source.DisposeAsync();
    var directory = Path.Combine(Environment.CurrentDirectory, "recordings"); Directory.CreateDirectory(directory);
    var rawPath = Path.Combine(directory, "walk-tuning-50s.jsonl"); var decisionPath = Path.Combine(directory, "walk-tuning-50s-decisions.jsonl");
    await using (var writer = new StreamWriter(File.Create(rawPath))) foreach (var item in raw) await writer.WriteLineAsync(JsonSerializer.Serialize(item));
    await using (var writer = new StreamWriter(File.Create(decisionPath))) foreach (var item in decisions) await writer.WriteLineAsync(JsonSerializer.Serialize(item));
    var alternating = accepted.Zip(accepted.Skip(1)).Count(x => x.First.Side != x.Second.Side);
    var intervals = accepted.Zip(accepted.Skip(1)).Select(x => (x.Second.Ticks - x.First.Ticks) / (double)System.Diagnostics.Stopwatch.Frequency).Where(x => x > 0).Order().ToArray();
    var medianInterval = intervals.Length == 0 ? 0 : intervals[intervals.Length / 2];
    Console.WriteLine($"WALK_CAPTURE_OK raw={raw.Count} decisions={decisions.Count} steps={accepted.Count} alternating={alternating} medianStepInterval={medianInterval:F3}s medianCadence={(medianInterval > 0 ? 1 / medianInterval : 0):F2}Hz rawPath={rawPath} decisionPath={decisionPath}");
    return 0;
}
static async Task<int> LegBalanceCaptureAsync()
{
    var devices = HidDeviceEnumerator.FindJoyCons().GroupBy(x => x.Side).Select(x => x.First()).ToArray();
    if (!devices.Any(x => x.Side == JoyConSide.Left) || !devices.Any(x => x.Side == JoyConSide.Right)) { Console.Error.WriteLine("LEG_BALANCE_ERROR: Both Joy-Cons are required."); return 13; }
    var sync = new object(); var phase = 0; var samples = new List<LegMagnitudeSample>(10000); using var lifetime = new CancellationTokenSource();
    var sources = devices.Select(x => (Device: x, Source: new JoyConSensorSource(x))).ToArray();
    foreach (var item in sources) await item.Source.StartAsync(lifetime.Token);
    var readers = sources.Select(async item =>
    {
        var side = item.Device.Side == JoyConSide.Left ? LegSide.Left : LegSide.Right;
        await foreach (var sample in item.Source.Samples.ReadAllAsync(lifetime.Token)) lock (sync)
        {
            if (phase is 1 or 2) samples.Add(new LegMagnitudeSample(phase == 1 ? "left-lifts" : "right-lifts", side, sample.Timestamp.MonotonicTicks, sample.AngularVelocityDps.Length()));
        }
    }).ToArray();
    await Task.Delay(TimeSpan.FromSeconds(3)); if (OperatingSystem.IsWindows()) Console.Beep(500, 300); phase = 1; Console.WriteLine("LEFT_LIFTS_NOW");
    await Task.Delay(TimeSpan.FromSeconds(10)); phase = 2; if (OperatingSystem.IsWindows()) { Console.Beep(1050, 160); await Task.Delay(100); Console.Beep(1050, 160); } Console.WriteLine("RIGHT_LIFTS_NOW");
    await Task.Delay(TimeSpan.FromSeconds(10)); phase = 3; if (OperatingSystem.IsWindows()) Console.Beep(450, 650); lifetime.Cancel();
    try { await Task.WhenAll(readers); } catch (OperationCanceledException) { }
    foreach (var item in sources) await item.Source.DisposeAsync();
    var directory = Path.Combine(Environment.CurrentDirectory, "recordings"); Directory.CreateDirectory(directory); var path = Path.Combine(directory, "leg-balance.jsonl");
    await using (var writer = new StreamWriter(File.Create(path))) foreach (var item in samples) await writer.WriteLineAsync(JsonSerializer.Serialize(item));
    foreach (var phaseName in new[] { "left-lifts", "right-lifts" }) foreach (var side in new[] { LegSide.Left, LegSide.Right })
    {
        var values = samples.Where(x => x.Phase == phaseName && x.Side == side).Select(x => x.MagnitudeDps).Order().ToArray();
        double P(double q) => values.Length == 0 ? 0 : values[Math.Min(values.Length - 1, (int)(values.Length * q))];
        Console.WriteLine($"LEG_STATS phase={phaseName} sensor={side} n={values.Length} p50={P(.50):F1} p90={P(.90):F1} p95={P(.95):F1} p99={P(.99):F1} max={(values.Length == 0 ? 0 : values[^1]):F1} above80={values.Count(x => x >= 80)}");
    }
    Console.WriteLine($"LEG_BALANCE_OK path={path} samples={samples.Count}"); return 0;
}
static async Task<int> VrOutputForwardTestAsync()
{
    await using var output = new VrOutputController(new NamedPipeVrOutputSink()); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    try { Console.WriteLine("VR_FORWARD_TEST_ARMED delay=10s speed=0.35 duration=2s audioCues=true"); await Task.Delay(10000, timeout.Token); if (OperatingSystem.IsWindows()) Console.Beep(1000, 140); await Task.Delay(100, timeout.Token); if (OperatingSystem.IsWindows()) Console.Beep(1000, 140); await output.StartAsync(timeout.Token); var until = DateTime.UtcNow + TimeSpan.FromSeconds(2); while (DateTime.UtcNow < until) { await output.SetAsync(new(0, 0.35f), timeout.Token); await Task.Delay(20, timeout.Token); } await output.StopAsync(timeout.Token); if (OperatingSystem.IsWindows()) Console.Beep(500, 500); Console.WriteLine("VR_FORWARD_TEST_OK finalZero=true detached=true"); return 0; }
    catch (Exception ex) { Console.Error.WriteLine($"VR_FORWARD_TEST_ERROR: {ex.Message}"); return 12; }
}
static async Task<int> VrPaceSimulationAsync(int samplesPerStage = 400)
{
    await using var output = new VrOutputController(new NamedPipeVrOutputSink());
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try
    {
        Console.WriteLine($"VR_PACE_SIM_ARMED delay=3s stages=slow,normal,fast duration={samplesPerStage / 50.0:F0}s_each");
        await Task.Delay(3000, timeout.Token);
        await output.StartAsync(timeout.Token);
        var smoother = new LocomotionSmoother();
        var stages = new[] { (Name: "SLOW", Target: 0.75), (Name: "NORMAL", Target: 0.90), (Name: "FAST", Target: 1.00) };
        foreach (var stage in stages)
        {
            if (OperatingSystem.IsWindows()) Console.Beep(stage.Name == "SLOW" ? 650 : stage.Name == "NORMAL" ? 850 : 1050, 140);
            Console.WriteLine($"VR_PACE_STAGE {stage.Name} target={stage.Target:F2}");
            for (var i = 0; i < samplesPerStage; i++)
            {
                var speed = smoother.Update(stage.Target, TimeSpan.FromMilliseconds(20), 0.85, 0.95);
                await output.SetAsync(new(0, (float)speed), timeout.Token);
                await Task.Delay(20, timeout.Token);
            }
        }
        for (var i = 0; i < 20; i++)
        {
            var speed = smoother.Update(0, TimeSpan.FromMilliseconds(20), 0.85, 3.5);
            await output.SetAsync(new(0, (float)speed), timeout.Token);
            await Task.Delay(20, timeout.Token);
        }
        await output.StopAsync(timeout.Token);
        if (OperatingSystem.IsWindows()) Console.Beep(450, 500);
        Console.WriteLine("VR_PACE_SIM_OK finalZero=true detached=true");
        return 0;
    }
    catch (Exception ex) { try { await output.StopAsync(); } catch { } Console.Error.WriteLine($"VR_PACE_SIM_ERROR: {ex.Message}"); return 1; }
}
static async Task<int> VrStraightDriftTestAsync()
{
    await using var output = new VrOutputController(new NamedPipeVrOutputSink());
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    try
    {
        Console.WriteLine("VR_STRAIGHT_ARMED delay=3s x=0 y=0.75 duration=6s");
        await Task.Delay(3000, timeout.Token);
        await output.StartAsync(timeout.Token);
        if (OperatingSystem.IsWindows()) Console.Beep(900, 140);
        var smoother = new LocomotionSmoother();
        for (var i = 0; i < 300; i++)
        {
            var speed = smoother.Update(0.75, TimeSpan.FromMilliseconds(20), 1.2, 1.2);
            await output.SetAsync(new(0, (float)speed), timeout.Token);
            await Task.Delay(20, timeout.Token);
        }
        await output.StopAsync(timeout.Token);
        if (OperatingSystem.IsWindows()) Console.Beep(450, 400);
        Console.WriteLine("VR_STRAIGHT_OK x=0 finalZero=true");
        return 0;
    }
    catch (Exception ex) { try { await output.StopAsync(); } catch { } Console.Error.WriteLine($"VR_STRAIGHT_ERROR: {ex.Message}"); return 1; }
}
static async IAsyncEnumerable<PhoneImuSample> ToPhoneAsync(IEnumerable<PhoneImuSample> samples) { foreach (var sample in samples) yield return sample; await Task.CompletedTask; }
static async Task HmdPoseRoundTrip()
{
    var sample = new HmdPoseSample("hmd", 1, new SensorTimestamp(Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow), true,
        new Vector3(1, 2, 3), Quaternion.Identity, .4f, .2f);
    await using var stream = new MemoryStream();
    await new HmdPoseRecorder().RecordAsync(ToHmdAsync([sample]), stream); stream.Position = 0;
    HmdPoseSample? result = null; await foreach (var item in new HmdPoseReplayReader().ReadAsync(stream, 1000)) result = item;
    Assert(result is { IsTracked: true } && result.Value.PositionMeters == sample.PositionMeters, "HMD pose replay changed the sample.");
}
static void HmdValidationQualityTest()
{
    Assert(HmdValidationQuality.Evaluate("capture", 1800, 1760, 180, 95).Passed, "Stable HMD capture should pass.");
    Assert(!HmdValidationQuality.Evaluate("capture", 1800, 900, 180, 95).Passed, "Poor tracking must fail.");
    Assert(!HmdValidationQuality.Evaluate("capture", 1800, 1760, 180, 12).Passed, "Missing turns must fail.");
    Assert(!HmdValidationQuality.Evaluate("capture", 20, 20, 3, 95).Passed, "Very short capture must fail.");
}
static void HmdFusionPolicyTest()
{
    var now = Stopwatch.GetTimestamp();
    var weakGait = new GaitSnapshot(GaitState.Walking, .8, .4, .35, LegSide.Left, 2);
    var weak = new FusionSnapshot(weakGait, .4, .35, false, false, true, 0);
    var turn = new HmdPoseSample("hmd", 1, new SensorTimestamp(now, DateTimeOffset.UtcNow), true, Vector3.Zero, Quaternion.Identity, 0, 1.6f);
    var suppressed = HmdFusionPolicy.Apply(weak, turn, now, true);
    Assert(suppressed.Fresh && suppressed.Turning && suppressed.SuppressedFalseForward && suppressed.Snapshot.TargetSpeed == 0, "Weak forward evidence during a clear turn must be suppressed.");

    var strongGait = new GaitSnapshot(GaitState.Walking, 1.8, .9, .7, LegSide.Right, 8);
    var strong = weak with { Gait = strongGait, GlobalConfidence = .9, TargetSpeed = .7 };
    Assert(HmdFusionPolicy.Apply(strong, turn, now, true).Snapshot.TargetSpeed == .7, "Strong walking must continue while turning.");
    Assert(HmdFusionPolicy.Apply(weak, turn, now + Stopwatch.Frequency, true).Snapshot.TargetSpeed == .35, "Stale HMD data must not affect locomotion.");
    Assert(HmdFusionPolicy.Apply(weak, turn, now, false).Snapshot.TargetSpeed == .35, "Unvalidated HMD data must not affect locomotion.");
}
static void EnduranceSimulationTest()
{
    var result = new EnduranceSimulationService().Run(TimeSpan.FromHours(4));
    Assert(result.Samples == 720000 && result.Steps > 0 && result.SafeZeroPassed && result.PeakAllocatedMb < 256, "Accelerated endurance contract failed.");
}
static async IAsyncEnumerable<HmdPoseSample> ToHmdAsync(IEnumerable<HmdPoseSample> samples) { foreach (var sample in samples) yield return sample; await Task.CompletedTask; }
record LabeledJoyConSample(string Phase, LegSide Side, JoyConImuSample Sample);
record WalkTuningDecision(long Ticks, GaitState State, double CadenceHz, double Confidence, double TargetSpeed, long StepCount);
record LegMagnitudeSample(string Phase, LegSide Side, long Ticks, double MagnitudeDps);
sealed class StaticHttpHandler(byte[] content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content), RequestMessage = request });
}
sealed class TestOutputSink : IAnalogLocomotionSink
{
    public bool IsAttached { get; private set; } public bool FailOnNonZero { get; init; } public List<LocomotionVector> Values { get; } = [];
    public ValueTask AttachAsync(CancellationToken cancellationToken = default) { IsAttached = true; return ValueTask.CompletedTask; }
    public ValueTask WriteAsync(LocomotionVector value, CancellationToken cancellationToken = default) { if (FailOnNonZero && value != LocomotionVector.Zero) throw new IOException("simulated output failure"); Values.Add(value); return ValueTask.CompletedTask; }
    public ValueTask DetachAsync(CancellationToken cancellationToken = default) { IsAttached = false; return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { IsAttached = false; return ValueTask.CompletedTask; }
}
