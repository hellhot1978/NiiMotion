namespace NiiRMotion.Infrastructure;

public enum PsMoveOnboardingStep { AssignControllers, ReadFactoryCalibration, CalibratePlacement, RecordFoundation, RecordDiscrimination, Ready }
public sealed record PsMoveOnboardingStatus(PsMoveOnboardingStep NextStep, int CompletedSteps, int TotalSteps, string Instruction)
{
    public bool IsReady => NextStep == PsMoveOnboardingStep.Ready;
}

public sealed class PsMoveOnboardingService
{
    public async Task<PsMoveOnboardingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var assignments = await new PsMoveAssignmentStore(NiiMotionPaths.PsMoveAssignments).LoadAsync(cancellationToken);
        if (assignments is not { IsComplete: true }) return Status(PsMoveOnboardingStep.AssignControllers, 0, "Sol ve sağ Move kontrolcülerini tanıt.");
        var factory = await new PsMoveCalibrationStore(NiiMotionPaths.PsMoveFactoryCalibration).LoadAsync(cancellationToken);
        if (!factory.Any(x => x.StableId == assignments.LeftStableId) || !factory.Any(x => x.StableId == assignments.RightStableId))
            return Status(PsMoveOnboardingStep.ReadFactoryCalibration, 1, "Kontrolcüleri sırayla USB ile bağlayarak fabrika kalibrasyonunu oku.");
        if (!File.Exists(NiiMotionPaths.PsMovePlacementCalibration)) return Status(PsMoveOnboardingStep.CalibratePlacement, 2, "Move'ları baldırlara tak ve nötr yerleşimi ölç.");
        if (!Directory.EnumerateDirectories(NiiMotionPaths.PsMoveData, "*-foundation").Any()) return Status(PsMoveOnboardingStep.RecordFoundation, 3, "İlk 5 dakikalık yönlendirmeli kaydı tamamla.");
        if (!Directory.EnumerateDirectories(NiiMotionPaths.PsMoveData, "*-discrimination").Any()) return Status(PsMoveOnboardingStep.RecordDiscrimination, 4, "Yanlış hareketleri ayıran ikinci kaydı tamamla.");
        if (!File.Exists(NiiMotionPaths.PsMoveTrainingProfile))
        {
            var profile = await new PsMoveTrainingAnalyzer().AnalyzeAsync(NiiMotionPaths.PsMoveData, cancellationToken);
            await PsMoveTrainingAnalyzer.SaveAsync(profile, NiiMotionPaths.PsMoveTrainingProfile, cancellationToken);
        }
        return Status(PsMoveOnboardingStep.Ready, 5, "PS Move kişisel profili hazır.");
    }

    private static PsMoveOnboardingStatus Status(PsMoveOnboardingStep step, int completed, string instruction) => new(step, completed, 5, instruction);
}
