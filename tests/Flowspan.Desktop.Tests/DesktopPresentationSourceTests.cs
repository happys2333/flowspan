using System.Text;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopPresentationSourceTests
{
    private static readonly string[] AdditionalPresentationSources =
    [
        "DesktopIdentityStartup.cs",
        "DesktopLocalNetworkPermissionGuide.cs",
        "DesktopRemoteWindowPermissionService.cs",
        "DesktopTrustedPeerConnections.cs",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> AllowedMachineLiterals =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ActivityWorkspaceViewModel.cs"] =
            [
                "O",
                "activity.receive",
                "activity.replace",
                "internal-failure",
                "none",
                "operation-in-progress",
                "peer-unavailable",
                "undo-unavailable",
                "workspace.note/v1",
            ],
            ["DesktopIdentityStartup.cs"] =
            [
                "identity.credential_store_unavailable",
                "identity.initialization_failed",
                "identity.linux_secret_service_unavailable",
                "identity.platform_unsupported",
                "start",
                "timeout",
            ],
            ["LocalDataViewModel.cs"] = ["O"],
            ["LocalPairingViewModel.cs"] = ["u"],
            ["RemoteWindowWorkspaceViewModel.cs"] =
            [
                "O",
                "native_adapters_unavailable",
                "service_state_unavailable",
            ],
            ["SceneApplyViewModel.cs"] = ["O"],
            ["SceneRepositoryViewModel.cs"] = ["O"],
            ["TrustedDevicesViewModel.cs"] =
            [
                "activity.offer",
                "activity.receive",
                "activity.replace",
                "activity.swap",
                "file.receive",
                "mirror.drive",
                "mirror.view",
                "scene.apply",
                "u",
            ],
        };

    private static readonly IReadOnlyDictionary<string, string[]> AllowedInternalExceptionMessages =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ActivityWorkspaceViewModel.cs"] =
            [
                "A Replace operation that was not delivered needs an exact preflight or delivery failure.",
                "A failed desktop Replace inventory must have a failure code.",
                "A production Activity runtime was not configured.",
                "Destructive Replace is not configured by this Activity service.",
                "Replace target inventory is not configured by this Activity service.",
                "Semantic Move is not configured by this Activity service.",
                "Target-local Replace undo is not configured by this Activity service.",
            ],
            ["DesktopLocalNetworkPermissionGuide.cs"] =
            [
                "The desktop platform family is not supported.",
                "The desktop shell supports Windows, macOS, and Linux only.",
            ],
            ["DesktopTrustedPeerConnections.cs"] =
            [
                "A desktop reconnect loop can run only once.",
                "An authenticated session has no current Trust Record.",
                "An authenticated session requires an initialized protocol version.",
                "One or more trusted-peer reconnect loops failed to close.",
                "The desktop idle channel received a message before an Activity handler was available.",
                "The desktop reconnect loop factory returned null.",
                "The trusted-peer connection state is not supported.",
                "Trusted peer connections can be started only once.",
            ],
            ["LocalPairingViewModel.cs"] =
            [
                "One or more local pairing view resources failed to close.",
                "The local pairing candidate Trust state is not supported.",
            ],
            ["SceneApplyViewModel.cs"] =
            [
                "A Scene Replace row requires its exact confirmation.",
                "A Scene source selection must come from the exact preview candidates.",
                "Scene Apply is not configured by this desktop service.",
            ],
            ["SceneRepositoryViewModel.cs"] =
            [
                "The Scene repository is not configured by this desktop service.",
            ],
            ["TrustedDevicesViewModel.cs"] =
            [
                "One or more trusted-device resources failed to close.",
                "The Capability does not have a desktop label.",
                "The desktop Trust mutation status is not supported.",
            ],
            ["WorkspaceShellViewModel.cs"] =
            [
                "A production local-pairing runtime was not configured.",
                "One or more desktop resources failed to close.",
            ],
        };

    [Fact]
    public void PresentationSourcesContainNoDirectUserFacingProse()
    {
        string sourceDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "DesktopSource");
        string[] paths = Directory.GetFiles(
                sourceDirectory,
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .Where(path => IsPresentationSource(Path.GetFileName(path)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.All(
            AdditionalPresentationSources,
            fileName => Assert.Contains(
                paths,
                path => Path.GetFileName(path) == fileName));
        var sources = paths.ToDictionary(
            static path => Path.GetFileName(path),
            File.ReadAllText,
            StringComparer.Ordinal);

        AssertAllowlistEntriesAreCurrent(sources);

        PresentationLiteral[] violations = sources
            .SelectMany(pair => FindPresentationLiterals(pair.Key, pair.Value))
            .OrderBy(static literal => literal.FileName, StringComparer.Ordinal)
            .ThenBy(static literal => literal.Line)
            .ThenBy(static literal => literal.Value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Direct presentation text must use DesktopText.Get/Format. "
            + "Machine literals and internal exception messages require an exact, "
            + "reviewable allowlist entry.\n"
            + string.Join('\n', violations.Select(static literal =>
                $"{literal.FileName}:{literal.Line}: \"{literal.Value}\"")));
    }

    [Fact]
    public void LexerFindsEverySupportedProseFormAndNestedInterpolationFallback()
    {
        const string source = """"
            var regular = "regular prose";
            var verbatim = @"verbatim prose";
            var interpolated = $"Outer visible status for {name ?? "direct fallback"}.";
            var raw = """raw prose""";
            var rawInterpolated = $"""Raw visible status for {name}.""";
            """";

        PresentationLiteral[] literals = FindPresentationLiterals(
                "SyntheticViewModel.cs",
                source)
            .ToArray();

        Assert.Equal(
            [
                "regular prose",
                "verbatim prose",
                "Outer visible status for {...}.",
                "direct fallback",
                "raw prose",
                "Raw visible status for {...}.",
            ],
            literals.Select(static literal => literal.Value));
    }

    private static void AssertAllowlistEntriesAreCurrent(
        Dictionary<string, string> sources)
    {
        IReadOnlyDictionary<string, string[]>[] allowlists =
        [
            AllowedMachineLiterals,
            AllowedInternalExceptionMessages,
        ];

        foreach (IReadOnlyDictionary<string, string[]> allowlist in allowlists)
        {
            foreach ((string fileName, string[] values) in allowlist)
            {
                Assert.Equal(
                    values.Length,
                    values.Distinct(StringComparer.Ordinal).Count());
                Assert.True(
                    sources.TryGetValue(fileName, out string? source),
                    $"Allowlisted source '{fileName}' is not scanned.");
                var lexer = new CSharpStringLexer(source);
                string[] sourceValues = lexer.ReadAll()
                    .Select(static literal => literal.Value)
                    .ToArray();

                Assert.All(
                    values,
                    value => Assert.Contains(value, sourceValues));
            }
        }
    }

    private static bool IsPresentationSource(string fileName) =>
        fileName.EndsWith("ViewModel.cs", StringComparison.Ordinal)
        || AdditionalPresentationSources.Contains(fileName, StringComparer.Ordinal);

    private static IEnumerable<PresentationLiteral> FindPresentationLiterals(
        string fileName,
        string source)
    {
        var lexer = new CSharpStringLexer(source);
        foreach (StringLiteral literal in lexer.ReadAll())
        {
            if (!literal.HasPresentationCharacters
                || IsDesktopResourceKey(source, literal)
                || IsAllowed(fileName, literal.Value, AllowedMachineLiterals)
                || IsAllowed(
                    fileName,
                    literal.Value,
                    AllowedInternalExceptionMessages))
            {
                continue;
            }

            yield return new PresentationLiteral(
                fileName,
                GetLine(source, literal.Start),
                literal.Value);
        }
    }

    private static bool IsDesktopResourceKey(
        string source,
        StringLiteral literal)
    {
        if (literal.IsInterpolated)
        {
            return false;
        }

        int position = literal.Start - 1;
        while (position >= 0 && char.IsWhiteSpace(source[position]))
        {
            position--;
        }

        if (position < 0 || source[position] != '(')
        {
            return false;
        }

        position--;
        while (position >= 0 && char.IsWhiteSpace(source[position]))
        {
            position--;
        }

        int end = position + 1;
        while (position >= 0
               && (char.IsLetterOrDigit(source[position])
                   || source[position] is '.' or '_'))
        {
            position--;
        }

        string call = source[(position + 1)..end];
        return call is "DesktopText.Get" or "DesktopText.Format";
    }

    private static bool IsAllowed(
        string fileName,
        string value,
        IReadOnlyDictionary<string, string[]> allowlist) =>
        allowlist.TryGetValue(fileName, out string[]? values)
        && values.Contains(value, StringComparer.Ordinal);

    private static int GetLine(string source, int position)
    {
        int line = 1;
        for (int index = 0; index < position; index++)
        {
            if (source[index] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private sealed class CSharpStringLexer(string source)
    {
        private readonly List<StringLiteral> literals = [];

        public List<StringLiteral> ReadAll()
        {
            int position = 0;
            while (position < source.Length)
            {
                if (SkipTriviaOrCharacter(ref position)
                    || TryReadString(ref position))
                {
                    continue;
                }

                position++;
            }

            literals.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            return literals;
        }

        private bool TryReadString(ref int position)
        {
            int start = position;
            int quoteStart;
            int dollarCount = 0;
            bool verbatim = false;

            if (source[position] == '"')
            {
                quoteStart = position;
            }
            else if (source[position] == '@')
            {
                int next = position + 1;
                if (next < source.Length && source[next] == '$')
                {
                    dollarCount = 1;
                    next++;
                }

                if (next >= source.Length || source[next] != '"')
                {
                    return false;
                }

                verbatim = true;
                quoteStart = next;
            }
            else if (source[position] == '$')
            {
                int next = position;
                while (next < source.Length && source[next] == '$')
                {
                    dollarCount++;
                    next++;
                }

                if (next < source.Length && source[next] == '@')
                {
                    if (dollarCount != 1)
                    {
                        return false;
                    }

                    verbatim = true;
                    next++;
                }

                if (next >= source.Length || source[next] != '"')
                {
                    return false;
                }

                quoteStart = next;
            }
            else
            {
                return false;
            }

            int quoteCount = CountRun(quoteStart, '"');
            if (!verbatim && quoteCount >= 3)
            {
                ReadRawString(start, quoteStart, quoteCount, dollarCount, ref position);
            }
            else
            {
                ReadQuotedString(
                    start,
                    quoteStart,
                    verbatim,
                    dollarCount == 1,
                    ref position);
            }

            return true;
        }

        private void ReadQuotedString(
            int start,
            int quoteStart,
            bool verbatim,
            bool interpolated,
            ref int position)
        {
            var value = new StringBuilder();
            var presentationText = new StringBuilder();
            int index = quoteStart + 1;

            while (index < source.Length)
            {
                char current = source[index];
                if (current == '"')
                {
                    if (verbatim
                        && index + 1 < source.Length
                        && source[index + 1] == '"')
                    {
                        value.Append('"');
                        presentationText.Append('"');
                        index += 2;
                        continue;
                    }

                    index++;
                    break;
                }

                if (!verbatim && current == '\\')
                {
                    AppendEscape(value, presentationText, ref index);
                    continue;
                }

                if (interpolated && current == '{')
                {
                    if (index + 1 < source.Length && source[index + 1] == '{')
                    {
                        value.Append('{');
                        presentationText.Append('{');
                        index += 2;
                        continue;
                    }

                    value.Append("{...}");
                    index++;
                    ReadInterpolationExpression(ref index, 1);
                    continue;
                }

                if (interpolated
                    && current == '}'
                    && index + 1 < source.Length
                    && source[index + 1] == '}')
                {
                    value.Append('}');
                    presentationText.Append('}');
                    index += 2;
                    continue;
                }

                value.Append(current);
                presentationText.Append(current);
                index++;
            }

            literals.Add(new StringLiteral(
                start,
                value.ToString(),
                interpolated,
                ContainsPresentationCharacters(presentationText)));
            position = index;
        }

        private void ReadRawString(
            int start,
            int quoteStart,
            int quoteCount,
            int dollarCount,
            ref int position)
        {
            var value = new StringBuilder();
            var presentationText = new StringBuilder();
            int index = quoteStart + quoteCount;

            while (index < source.Length)
            {
                if (source[index] == '"'
                    && CountRun(index, '"') >= quoteCount)
                {
                    index += quoteCount;
                    break;
                }

                if (dollarCount > 0
                    && source[index] == '{'
                    && CountRun(index, '{') >= dollarCount)
                {
                    value.Append("{...}");
                    index += dollarCount;
                    ReadInterpolationExpression(ref index, dollarCount);
                    continue;
                }

                value.Append(source[index]);
                presentationText.Append(source[index]);
                index++;
            }

            literals.Add(new StringLiteral(
                start,
                value.ToString().Trim(),
                dollarCount > 0,
                ContainsPresentationCharacters(presentationText)));
            position = index;
        }

        private void ReadInterpolationExpression(
            ref int position,
            int closingBraceCount)
        {
            int braceDepth = 0;
            while (position < source.Length)
            {
                if (SkipTriviaOrCharacter(ref position)
                    || TryReadString(ref position))
                {
                    continue;
                }

                if (source[position] == '{')
                {
                    braceDepth++;
                    position++;
                    continue;
                }

                if (source[position] == '}')
                {
                    if (braceDepth == 0
                        && CountRun(position, '}') >= closingBraceCount)
                    {
                        position += closingBraceCount;
                        return;
                    }

                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }
                }

                position++;
            }
        }

        private bool SkipTriviaOrCharacter(ref int position)
        {
            if (source[position] == '/'
                && position + 1 < source.Length
                && source[position + 1] == '/')
            {
                position += 2;
                while (position < source.Length && source[position] != '\n')
                {
                    position++;
                }

                return true;
            }

            if (source[position] == '/'
                && position + 1 < source.Length
                && source[position + 1] == '*')
            {
                position += 2;
                while (position + 1 < source.Length
                       && (source[position] != '*'
                           || source[position + 1] != '/'))
                {
                    position++;
                }

                position = Math.Min(source.Length, position + 2);
                return true;
            }

            if (source[position] != '\'')
            {
                return false;
            }

            position++;
            while (position < source.Length)
            {
                if (source[position] == '\\')
                {
                    position = Math.Min(source.Length, position + 2);
                }
                else if (source[position++] == '\'')
                {
                    break;
                }
            }

            return true;
        }

        private void AppendEscape(
            StringBuilder value,
            StringBuilder presentationText,
            ref int position)
        {
            position++;
            if (position >= source.Length)
            {
                return;
            }

            char escaped = source[position++];
            char decoded = escaped switch
            {
                '\'' => '\'',
                '"' => '"',
                '\\' => '\\',
                '0' => '\0',
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'v' => '\v',
                _ => escaped,
            };
            value.Append(decoded);
            presentationText.Append(decoded);
        }

        private int CountRun(int position, char expected)
        {
            int end = position;
            while (end < source.Length && source[end] == expected)
            {
                end++;
            }

            return end - position;
        }

        private static bool ContainsPresentationCharacters(StringBuilder text) =>
            text.ToString().Any(char.IsLetterOrDigit);
    }

    private sealed record StringLiteral(
        int Start,
        string Value,
        bool IsInterpolated,
        bool HasPresentationCharacters);

    private sealed record PresentationLiteral(
        string FileName,
        int Line,
        string Value);
}
