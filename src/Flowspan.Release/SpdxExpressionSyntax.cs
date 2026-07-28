using NuGet.Packaging.Licenses;

namespace Flowspan.Release;

public static class SpdxExpressionSyntax
{
    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > ReleaseBounds.MaximumTextLength
            || value.Any(char.IsControl))
        {
            return false;
        }

        try
        {
            return NuGetLicenseExpression.Parse(value)
                .HasOnlyStandardIdentifiers();
        }
        catch (NuGetLicenseExpressionParsingException)
        {
            return false;
        }
    }
}
