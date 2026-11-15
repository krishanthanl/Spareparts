using System.Xml.Linq;

namespace Backend.UnitTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_has_no_dependencies_and_application_only_references_domain()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Spareparts.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        XDocument Load(string project) => XDocument.Load(Path.Combine(root!.FullName, "src", "backend", project, project + ".csproj"));
        Assert.Empty(Load("Domain").Descendants("ProjectReference"));
        Assert.Empty(Load("Domain").Descendants("PackageReference"));
        Assert.Equal("../Domain/Domain.csproj", Assert.Single(Load("Application").Descendants("ProjectReference")).Attribute("Include")!.Value);
    }
}
