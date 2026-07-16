using System.Text.RegularExpressions;
using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

/// <summary>
/// Resolves explicit @material references without exposing filesystem paths to prompt consumers.
/// Supports @{full file name} for names with spaces and @short-name for simple names.
/// </summary>
public static partial class ProjectAssetReferenceResolver
{
    public static IReadOnlyList<ProjectAssetReferenceMatch> Resolve(SnapCatProject project, string? text)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(text) || project.Assets.Count == 0)
        {
            return [];
        }

        var matches = new List<ProjectAssetReferenceMatch>();
        var matchedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match tokenMatch in AssetTokenRegex().Matches(text))
        {
            var token = tokenMatch.Groups["braced"].Success
                ? tokenMatch.Groups["braced"].Value.Trim()
                : tokenMatch.Groups["simple"].Value.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var candidates = project.Assets
                .Where(asset => MatchesAssetName(asset, token))
                .ToList();
            if (candidates.Count != 1 || !matchedIds.Add(candidates[0].Id))
            {
                continue;
            }

            matches.Add(new ProjectAssetReferenceMatch(token, candidates[0]));
        }

        return matches;
    }

    private static bool MatchesAssetName(ProjectAsset asset, string token) =>
        string.Equals(asset.DisplayName, token, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFileNameWithoutExtension(asset.DisplayName), token, StringComparison.OrdinalIgnoreCase)
        || string.Equals(asset.Id, token, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("@\\{(?<braced>[^}]+)\\}|@(?<simple>[\\p{L}\\p{N}_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AssetTokenRegex();
}
