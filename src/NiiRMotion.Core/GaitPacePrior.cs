using System.Text.Json;

namespace NiiRMotion.Core;

public sealed class GaitPacePrior
{
    private readonly double[] _mean, _scale, _coefficients;
    private readonly double _gyroPersonalizationScale;

    private GaitPacePrior(double[] mean, double[] scale, double[] coefficients, double personalP95Dps)
    {
        if (mean.Length != 5 || scale.Length != 5 || coefficients.Length != 6) throw new InvalidDataException("Unsupported gait pace model shape.");
        _mean = mean; _scale = scale; _coefficients = coefficients;
        _gyroPersonalizationScale = Math.Clamp(mean[1] / Math.Max(40, personalP95Dps), 1, 4);
    }

    public static async Task<GaitPacePrior> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var model = await JsonSerializer.DeserializeAsync<ModelFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
            ?? throw new InvalidDataException("Gait pace model is empty.");
        var personal = (model.NiirmotionPersonalization.LeftLiftP95Dps + model.NiirmotionPersonalization.RightLiftP95Dps) / 2;
        return new(model.Mean, model.Scale, model.Coefficients, personal);
    }

    public double EstimateKmh(double cadenceHz, double swingP95Dps)
    {
        cadenceHz = Math.Clamp(cadenceHz, 0.8, 4);
        var gyro = Math.Clamp(swingP95Dps * _gyroPersonalizationScale, 40, 900);
        Span<double> features = [cadenceHz, gyro, cadenceHz * cadenceHz, gyro * gyro, cadenceHz * gyro];
        var result = _coefficients[0];
        for (var i = 0; i < features.Length; i++) result += _coefficients[i + 1] * ((features[i] - _mean[i]) / _scale[i]);
        return Math.Clamp(result, 0, 10);
    }

    public double EstimateAnalogPace(double cadenceHz, double swingP95Dps) =>
        Math.Clamp(0.55 + (EstimateKmh(cadenceHz, swingP95Dps) - 2) * 0.09, 0.50, 1);

    private sealed record ModelFile(double[] Mean, double[] Scale, double[] Coefficients, Personalization NiirmotionPersonalization);
    private sealed record Personalization(double LeftLiftP95Dps, double RightLiftP95Dps);
}
