using System.Security.Cryptography;
using System.Xml.Linq;

namespace FinancialManagement.Tests;

public sealed class BrandingResourceTests
{
    private const string AppIconResource = "Resources/Branding/TresoriaAppIcon.ico";
    private const string AppIconPngResource = "Resources/Branding/TresoriaAppIcon.png";
    private const string SidebarLogoResource = "Resources/Branding/TresoriaSidebarLogo.png";
    private const string AppIconSha256 = "5DCDB586AE9563B1D29775E1AC9F74490152A93E4F54E4637E06A05A23CF9B5E";
    private const string SidebarLogoSha256 = "94E3DFB2BBFA0DA2F2DE4FAE32B18841E540E22152C3E1BE2059D94112228901";

    [Fact]
    public void Shell_uses_Tresoria_title_icon_and_approved_sidebar_logo()
    {
        var desktopProject = DesktopProjectDirectory();
        var mainWindow = XDocument.Load(Path.Combine(desktopProject, "MainWindow.xaml"));
        var window = Assert.IsType<XElement>(mainWindow.Root);

        Assert.Equal("Tresoria", window.Attribute("Title")?.Value);
        Assert.Equal(AppIconResource, window.Attribute("Icon")?.Value);

        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var logo = Assert.Single(
            mainWindow.Descendants(presentation + "Image"),
            element => element.Attribute(xaml + "Name")?.Value == "SidebarBrandLogo");

        Assert.Equal(SidebarLogoResource, logo.Attribute("Source")?.Value);
        Assert.Equal("Uniform", logo.Attribute("Stretch")?.Value);
        Assert.Equal("Logo Tresoria", logo.Attribute("AutomationProperties.Name")?.Value);

        var logoSurface = Assert.IsType<XElement>(logo.Parent);
        Assert.Equal("216", logoSurface.Attribute("Width")?.Value);
        Assert.Equal("16,16,16,14", logoSurface.Attribute("Margin")?.Value);
        Assert.Null(logoSurface.Attribute("Background"));
        Assert.Null(logoSurface.Attribute("BorderThickness"));
        Assert.Equal("190", logo.Attribute("Width")?.Value);

        var primaryBrandText = mainWindow.Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Where(value => value is not null)
            .ToArray();
        Assert.DoesNotContain("GESTION", primaryBrandText);
        Assert.DoesNotContain("financière", primaryBrandText);

        var contentHost = Assert.Single(
            mainWindow.Descendants(presentation + "ContentControl"),
            element => element.Attribute("Content")?.Value == "{Binding CurrentPage}");
        Assert.Equal("26,22,24,24", contentHost.Attribute("Margin")?.Value);
    }

    [Theory]
    [InlineData("LoginWindow.xaml", "Connexion — Tresoria")]
    [InlineData("FirstRunWindow.xaml", "Première initialisation — Tresoria")]
    [InlineData("InitializationWindow.xaml", "Configuration initiale — Tresoria")]
    public void Supporting_windows_use_Tresoria_title_and_application_icon(
        string fileName,
        string expectedTitle)
    {
        var document = XDocument.Load(Path.Combine(DesktopProjectDirectory(), fileName));
        var window = Assert.IsType<XElement>(document.Root);

        Assert.Equal(expectedTitle, window.Attribute("Title")?.Value);
        Assert.Equal(AppIconResource, window.Attribute("Icon")?.Value);
    }

    [Fact]
    public void Approved_assets_are_exact_project_resources_and_icon_has_common_Windows_sizes()
    {
        var desktopProject = DesktopProjectDirectory();
        var appIconPng = Path.Combine(desktopProject, AppIconPngResource.Replace('/', Path.DirectorySeparatorChar));
        var sidebarLogo = Path.Combine(desktopProject, SidebarLogoResource.Replace('/', Path.DirectorySeparatorChar));
        var appIcon = Path.Combine(desktopProject, AppIconResource.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(AppIconSha256, Sha256(appIconPng));
        Assert.Equal(SidebarLogoSha256, Sha256(sidebarLogo));
        Assert.Equal((2172, 724), ReadPngDimensions(sidebarLogo));
        Assert.True(File.Exists(appIcon));
        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, ReadIcoSizes(appIcon));

        var project = XDocument.Load(Path.Combine(desktopProject, "FinancialManagement.Desktop.csproj"));
        Assert.Equal(
            @"Resources\Branding\TresoriaAppIcon.ico",
            project.Descendants("ApplicationIcon").Single().Value);

        var resources = project.Descendants("Resource")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .ToArray();
        Assert.Contains(@"Resources\Branding\TresoriaAppIcon.ico", resources);
        Assert.Contains(@"Resources\Branding\TresoriaAppIcon.png", resources);
        Assert.Contains(@"Resources\Branding\TresoriaSidebarLogo.png", resources);
    }

    [Fact]
    public void Branding_references_are_portable_and_old_product_identity_is_removed()
    {
        var desktopProject = DesktopProjectDirectory();
        var files = new[]
        {
            "FinancialManagement.Desktop.csproj",
            "MainWindow.xaml",
            "LoginWindow.xaml",
            "FirstRunWindow.xaml",
            "InitializationWindow.xaml",
            Path.Combine("Services", "BackupDialogService.cs")
        };

        var source = string.Join(
            Environment.NewLine,
            files.Select(file => File.ReadAllText(Path.Combine(desktopProject, file))));

        Assert.DoesNotContain("Gestion financière", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\Downloads\", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\Desktop\", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\Temp\", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Downloads/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Desktop/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Temp/", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string DesktopProjectDirectory() =>
        Path.Combine(RepositoryRoot(), "src", "FinancialManagement.Desktop");

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FinancialManagement.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static int[] ReadIcoSizes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 6);
        Assert.Equal((ushort)0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal((ushort)1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        Assert.True(bytes.Length >= 6 + (count * 16));

        return Enumerable.Range(0, count)
            .Select(index => bytes[6 + (index * 16)] is 0 ? 256 : bytes[6 + (index * 16)])
            .Order()
            .ToArray();
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 24);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);

        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (width, height);
    }
}
