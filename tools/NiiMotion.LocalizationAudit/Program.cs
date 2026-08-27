using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var localizationPath = Path.Combine(root, "src", "NiiRMotion.App", "UiLocalization.cs");
var localizationTree = CSharpSyntaxTree.ParseText(File.ReadAllText(localizationPath), path: localizationPath);
var manualSources = localizationTree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>()
    .Where(x => x.IsKind(SyntaxKind.StringLiteralExpression)).Select(x => x.Token.ValueText).ToHashSet(StringComparer.Ordinal);
var generatedPath = Path.Combine(root, "src", "NiiRMotion.App", "Assets", "Localization", "en.generated.json");
var generated = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(generatedPath))
    ?? new Dictionary<string, string>(StringComparer.Ordinal);
var findings = new List<Finding>();
var sourceRoots = new[] { "NiiRMotion.App", "NiiRMotion.Infrastructure", "NiiRMotion.Core" }
    .Select(project => Path.Combine(root, "src", project));
foreach (var path in sourceRoots.SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
             .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
             .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
             .Where(path => !Path.GetFileName(path).Equals("UiLocalization.cs", StringComparison.OrdinalIgnoreCase)))
{
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
    foreach (var node in tree.GetRoot().DescendantNodes().OfType<ExpressionSyntax>())
    {
        string? source = node switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            InterpolatedStringExpressionSyntax interpolated => Normalize(interpolated),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(source) || !HasTurkish(source)) continue;
        string? translated = null;
        var covered = manualSources.Contains(source) || generated.TryGetValue(source, out translated);
        if (!covered || (translated is not null && HasTurkish(translated)))
            findings.Add(new(Path.GetFileName(path), tree.GetLineSpan(node.Span).StartLinePosition.Line + 1, source, translated ?? ""));
    }
}
Console.WriteLine(JsonSerializer.Serialize(findings.DistinctBy(x => x.Source).OrderBy(x => x.File).ThenBy(x => x.Line), new JsonSerializerOptions { WriteIndented = true }));
Console.Error.WriteLine($"Untranslated unique UI strings: {findings.Select(x => x.Source).Distinct().Count()}");
return findings.Count == 0 ? 0 : 2;

static string Normalize(InterpolatedStringExpressionSyntax value)
{
    var index = 0;
    return string.Concat(value.Contents.Select(content => content switch
    {
        InterpolatedStringTextSyntax text => text.TextToken.ValueText,
        InterpolationSyntax => $"{{VALUE{index++}}}",
        _ => ""
    }));
}

static bool HasTurkish(string value) => Regex.IsMatch(value, "[çğıöşüÇĞİÖŞÜ]", RegexOptions.CultureInvariant)
    || Regex.IsMatch(value, @"\b(?:bağlantı|cihaz|devam|durdur|faz|gerekli|hazır|kalibrasyon|kapat|kaydet|kayıt|oyun|önce|sağ|seç|sensör|sol|telefon|veri|yeniden|yürüyüş)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

internal sealed record Finding(string File, int Line, string Source, string Translation);
