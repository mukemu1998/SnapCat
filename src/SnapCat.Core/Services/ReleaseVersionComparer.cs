namespace SnapCat.Core.Services;

public static class ReleaseVersionComparer
{
    public static bool IsNewer(string candidateVersion, string currentVersion)
    {
        return Compare(candidateVersion, currentVersion) > 0;
    }

    public static int Compare(string leftVersion, string rightVersion)
    {
        var left = ReleaseVersion.Parse(leftVersion);
        var right = ReleaseVersion.Parse(rightVersion);

        var numeric = left.Numeric.CompareTo(right.Numeric);
        if (numeric != 0)
        {
            return numeric;
        }

        if (string.IsNullOrWhiteSpace(left.PreRelease) && string.IsNullOrWhiteSpace(right.PreRelease))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(left.PreRelease))
        {
            return 1;
        }

        if (string.IsNullOrWhiteSpace(right.PreRelease))
        {
            return -1;
        }

        return ComparePreRelease(left.PreRelease, right.PreRelease);
    }

    private static int ComparePreRelease(string left, string right)
    {
        var leftParts = left.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var rightParts = right.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var length = Math.Max(leftParts.Length, rightParts.Length);
        for (var index = 0; index < length; index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }

            if (index >= rightParts.Length)
            {
                return 1;
            }

            var leftPart = leftParts[index];
            var rightPart = rightParts[index];
            var leftIsNumber = int.TryParse(leftPart, out var leftNumber);
            var rightIsNumber = int.TryParse(rightPart, out var rightNumber);
            var result = leftIsNumber && rightIsNumber
                ? leftNumber.CompareTo(rightNumber)
                : leftIsNumber
                    ? -1
                    : rightIsNumber
                        ? 1
                        : string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    private readonly record struct ReleaseVersion(Version Numeric, string PreRelease)
    {
        public static ReleaseVersion Parse(string value)
        {
            var normalized = value?.Trim().TrimStart('v', 'V') ?? string.Empty;
            var separator = normalized.IndexOf('-');
            var numericText = separator >= 0 ? normalized[..separator] : normalized;
            var preRelease = separator >= 0 ? normalized[(separator + 1)..] : string.Empty;
            if (!Version.TryParse(numericText, out var numeric))
            {
                throw new FormatException($"无效版本号：{value}");
            }

            return new ReleaseVersion(numeric, preRelease);
        }
    }
}
