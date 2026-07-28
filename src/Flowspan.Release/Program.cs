namespace Flowspan.Release;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                throw new ReleaseInputException(
                    "A release command is required.");
            }

            string command = args[0];
            IReadOnlyDictionary<string, string> options = ParseOptions(args[1..]);
            return command switch
            {
                "prepare" => Prepare(options),
                "tree-digest" => PrintTreeDigest(options),
                "verify-build-inputs" => VerifyBuildInputs(options),
                "seal" => Seal(options),
                "verify" => Verify(options),
                _ => throw new ReleaseInputException(
                    "The release command is unsupported."),
            };
        }
        catch (ReleaseInputException exception)
        {
            Console.Error.WriteLine($"Release input rejected: {exception.Message}");
            return 2;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(
                $"Release command failed: {exception.GetType().Name}.");
            return 1;
        }
    }

    private static int Prepare(IReadOnlyDictionary<string, string> options)
    {
        RequireOptions(
            options,
            "publish",
            "stage",
            "version",
            "build-version",
            "commit",
            "repository",
            "rid",
            "source-date-epoch",
            "channel",
            "minimum-version",
            "download-base",
            "builder-id",
            "invocation-id");
        if (!long.TryParse(
            options["source-date-epoch"],
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out long epoch))
        {
            throw new ReleaseInputException(
                "SOURCE_DATE_EPOCH is not an integer.");
        }

        ReleaseContext context = ReleaseContext.Create(
            options["version"],
            options["build-version"],
            options["commit"],
            options["repository"],
            options["rid"],
            epoch,
            options["channel"],
            options["minimum-version"],
            options["download-base"],
            options["builder-id"],
            options["invocation-id"]);
        string stage = StagePreparer.Prepare(
            options["publish"],
            options["stage"],
            context);
        Console.WriteLine($"Prepared release stage: {stage}");
        return 0;
    }

    private static int PrintTreeDigest(
        IReadOnlyDictionary<string, string> options)
    {
        RequireOptions(options, "stage");
        Console.WriteLine(ReleaseSealer.ComputeSignedTreeSha256(options["stage"]));
        return 0;
    }

    private static int VerifyBuildInputs(
        IReadOnlyDictionary<string, string> options)
    {
        RequireOptions(options, "lock-file", "nuget-packages");
        BuildPackageLock.Verify(
            options["lock-file"],
            options["nuget-packages"]);
        Console.WriteLine("Release build inputs verification passed.");
        return 0;
    }

    private static int Seal(IReadOnlyDictionary<string, string> options)
    {
        RequireOptions(
            options,
            ["stage", "output", "lock-file", "runtime-lock-file", "nuget-packages", "signature-state"],
            ["signature-report"]);
        PackageRecordSet records = ReleaseSealer.Seal(
            options["stage"],
            options["output"],
            options["lock-file"],
            options["runtime-lock-file"],
            options["nuget-packages"],
            options["signature-state"],
            options.GetValueOrDefault("signature-report"));
        Console.WriteLine($"Sealed release package: {records.ArchiveFileName}");
        return 0;
    }

    private static int Verify(IReadOnlyDictionary<string, string> options)
    {
        RequireOptions(options, "output");
        ReleaseVerifier.VerifyDirectory(options["output"]);
        Console.WriteLine("Release package verification passed.");
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(
        string[] args)
    {
        if (args.Length % 2 != 0)
        {
            throw new ReleaseInputException(
                "Release options must be --name value pairs.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            string option = args[index];
            string value = args[index + 1];
            if (!option.StartsWith("--", StringComparison.Ordinal)
                || option.Length <= 2
                || string.IsNullOrEmpty(value)
                || !result.TryAdd(option[2..], value))
            {
                throw new ReleaseInputException(
                    "A release option is malformed or duplicated.");
            }
        }

        return result;
    }

    private static void RequireOptions(
        IReadOnlyDictionary<string, string> options,
        params string[] required) =>
        RequireOptions(options, required, []);

    private static void RequireOptions(
        IReadOnlyDictionary<string, string> options,
        IReadOnlyCollection<string> required,
        IReadOnlyCollection<string> optional)
    {
        if (required.Any(option => !options.ContainsKey(option)))
        {
            throw new ReleaseInputException(
                "A required release option is missing.");
        }

        var allowed = new HashSet<string>(required, StringComparer.Ordinal);
        allowed.UnionWith(optional);
        if (options.Keys.Any(option => !allowed.Contains(option)))
        {
            throw new ReleaseInputException(
                "An unknown release option was provided.");
        }
    }
}
