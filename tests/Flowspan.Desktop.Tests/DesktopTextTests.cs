using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopTextTests
{
    private static readonly string[] PresentationAttributes =
    [
        "AutomationProperties.HelpText",
        "AutomationProperties.Name",
        "Content",
        "Header",
        "Text",
        "Title",
        "ToolTip.Tip",
        "Watermark",
    ];

    private static readonly Regex PresentationResourceReference = new(
        @"\{(?:Dynamic|Static)Resource\s+(?<key>[A-Za-z0-9_]+)\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex InlineBindingPresentationLiteral = new(
        @"(?:StringFormat|FallbackValue|TargetNullValue)\s*=\s*"
        + @"(?!\{(?:Dynamic|Static)Resource\s+)",
        RegexOptions.CultureInvariant);

    private static HeadlessUnitTestSession HeadlessSession =>
        HeadlessUnitTestSession.GetOrStartForAssembly(
            typeof(DesktopTextTests).Assembly);

    [Fact]
    public void NeutralCatalogResolvesRequiredText()
    {
        Assert.Equal(
            "Flowspan \u2014 Continuous workspace",
            DesktopText.Get("MainWindow_Title"));
    }

    [Fact]
    public void TemplatesUseTheCurrentDisplayCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var expiresAt = new DateTimeOffset(
                2026,
                12,
                31,
                17,
                45,
                0,
                TimeSpan.Zero);

            Assert.Equal(
                expiresAt.ToString("g", CultureInfo.CurrentCulture),
                DesktopText.Format("PairingPrompt_ExpiresAt", expiresAt));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void UnsupportedUiCultureFallsBackToNeutralEnglish()
    {
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            Assert.Equal(
                "Flowspan \u2014 Continuous workspace",
                DesktopText.Get("MainWindow_Title"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void MissingResourceFailsWithItsExactKey()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => DesktopText.Get("Missing_Desktop_Resource"));

        Assert.Contains(
            "Missing_Desktop_Resource",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NeutralCatalogPopulatesAvaloniaResources()
    {
        var resources = new ResourceDictionary();

        DesktopText.AddTo(resources);

        Assert.Equal(
            "Flowspan \u2014 Continuous workspace",
            resources["MainWindow_Title"]);
    }

    [Fact]
    public void NeutralCatalogContainsOnlyValidCompositeFormats()
    {
        var resources = new ResourceDictionary();
        DesktopText.AddTo(resources);

        Assert.All(
            resources.OrderBy(entry => entry.Key?.ToString(), StringComparer.Ordinal),
            entry =>
            {
                string key = Assert.IsType<string>(entry.Key);
                string value = Assert.IsType<string>(entry.Value);
                FormatException? exception = Record.Exception(
                    () => CompositeFormat.Parse(value)) as FormatException;

                Assert.True(
                    exception is null,
                    $"Resource '{key}' is not a valid composite format: "
                    + exception?.Message);
            });
    }

    [Fact]
    public async Task ApplicationPublishesCatalogBeforeWindowsLoad()
    {
        await HeadlessSession.Dispatch(
            () => Assert.Equal(
                "Flowspan \u2014 Continuous workspace",
                Avalonia.Application.Current!.Resources["MainWindow_Title"]),
            CancellationToken.None);
    }

    [Fact]
    public void MainWindowContainsNoLiteralPresentationText()
    {
        XDocument document = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "Source.MainWindow.axaml"),
            LoadOptions.SetLineInfo);
        IEnumerable<string> attributeLiterals = GetPresentationAttributes(document)
            .Where(attribute => !attribute.Value.StartsWith('{')
                || InlineBindingPresentationLiteral.IsMatch(attribute.Value))
            .Select(attribute =>
            {
                var lineInfo = (IXmlLineInfo)attribute;
                return $"line {lineInfo.LineNumber}: "
                    + $"{attribute.Name.LocalName}=\"{attribute.Value}\"";
            });
        IEnumerable<string> elementTextLiterals = document.DescendantNodes()
            .OfType<XText>()
            .Where(node => !string.IsNullOrWhiteSpace(node.Value))
            .Select(node =>
            {
                var lineInfo = (IXmlLineInfo)node;
                return $"line {lineInfo.LineNumber}: element text "
                    + $"\"{node.Value.Trim()}\"";
            });
        string[] literals = attributeLiterals
            .Concat(elementTextLiterals)
            .ToArray();

        Assert.Empty(literals);
    }

    [Fact]
    public void MainWindowPresentationResourceReferencesResolve()
    {
        XDocument document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Source.MainWindow.axaml"));
        string[] keys = GetPresentationResourceKeys(document)
            .ToArray();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.False(
            string.IsNullOrWhiteSpace(DesktopText.Get(key)),
            $"Resource '{key}' is blank."));
    }

    [Fact]
    public void CatalogAndCommittedDesktopReferencesAreComplete()
    {
        XDocument document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Source.MainWindow.axaml"));
        var referencedKeys = GetPresentationResourceKeys(document)
            .ToHashSet(StringComparer.Ordinal);
        var resourceCall = new Regex(
            "DesktopText\\.(?:Get|Format)\\(\\s*\"(?<key>[A-Za-z0-9_]+)\"",
            RegexOptions.CultureInvariant);
        foreach (string path in Directory.GetFiles(
                     Path.Combine(AppContext.BaseDirectory, "DesktopSource"),
                     "*.cs",
                     SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(path);
            foreach (Match match in resourceCall.Matches(source))
            {
                referencedKeys.Add(match.Groups["key"].Value);
            }
        }

        var resources = new ResourceDictionary();
        DesktopText.AddTo(resources);
        string[] catalogKeys = resources.Keys
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] sourceKeys = referencedKeys
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(catalogKeys, sourceKeys);
    }

    [Fact]
    public void InteractiveControlsExposeExternalizedAutomationNames()
    {
        XDocument document = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "Source.MainWindow.axaml"),
            LoadOptions.SetLineInfo);
        string[] interactiveElements =
        [
            "Button",
            "CheckBox",
            "ComboBox",
            "ListBox",
            "RadioButton",
            "Slider",
            "TextBox",
            "ToggleButton",
        ];
        string[] unnamed = document.Descendants()
            .Where(element => interactiveElements.Contains(
                element.Name.LocalName,
                StringComparer.Ordinal))
            .Where(element => element.Attributes().All(attribute =>
                attribute.Name.LocalName != "AutomationProperties.Name"
                || !attribute.Value.StartsWith('{')))
            .Select(element =>
            {
                var lineInfo = (IXmlLineInfo)element;
                return $"line {lineInfo.LineNumber}: {element.Name.LocalName}";
            })
            .ToArray();

        Assert.Empty(unnamed);
    }

    [Fact]
    public void MainWindowDeclaresNoRequiredMotion()
    {
        XDocument document = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "Source.MainWindow.axaml"),
            LoadOptions.SetLineInfo);
        string[] motionDeclarations = document.Descendants()
            .Where(element => ContainsMotionName(element.Name.LocalName)
                || element.Attributes().Any(attribute =>
                    ContainsMotionName(attribute.Name.LocalName)
                    || (attribute.Name.LocalName == "Property"
                        && ContainsMotionName(attribute.Value))))
            .Select(element =>
            {
                var lineInfo = (IXmlLineInfo)element;
                return $"line {lineInfo.LineNumber}: {element.Name.LocalName}";
            })
            .ToArray();

        Assert.Empty(motionDeclarations);
    }

    private static bool ContainsMotionName(string value) =>
        value.Contains("Animation", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Transition", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<XAttribute> GetPresentationAttributes(
        XDocument document) =>
        document.Descendants()
            .Attributes()
            .Where(attribute => PresentationAttributes.Contains(
                attribute.Name.LocalName,
                StringComparer.Ordinal));

    private static IEnumerable<string> GetPresentationResourceKeys(
        XDocument document) =>
        GetPresentationAttributes(document)
            .SelectMany(attribute => PresentationResourceReference
                .Matches(attribute.Value)
                .Cast<Match>())
            .Select(match => match.Groups["key"].Value);
}
