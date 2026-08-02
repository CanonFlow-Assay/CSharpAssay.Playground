using System.Reflection;
using Xunit;
using System.Xml.Linq;
using Shape.Application;
using Shape.Domain;
using Shape.Infrastructure;

namespace Shape.Architecture.Tests;

public sealed class ShapeArchitectureTests
{
    [Fact]
    public void Domain_has_no_shell_or_framework_references()
    {
        var references = ReferencedAssemblies(typeof(OrderSubmission).Assembly);

        Assert.DoesNotContain("Shape.Application", references);
        Assert.DoesNotContain("Shape.Infrastructure", references);
        Assert.DoesNotContain("Shape.Api", references);
        Assert.DoesNotContain(
            references,
            name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_references_domain_but_not_shells()
    {
        var references = ReferencedAssemblies(typeof(SubmitOrderHandler).Assembly);

        Assert.Contains("Shape.Domain", references);
        Assert.DoesNotContain("Shape.Infrastructure", references);
        Assert.DoesNotContain("Shape.Api", references);
        Assert.DoesNotContain(
            references,
            name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Project_reference_graph_matches_the_contract()
    {
        Assert.Empty(ProjectReferences("Shape.Domain"));
        Assert.Equal(["Shape.Domain"], ProjectReferences("Shape.Application"));
        Assert.Equal(
            ["Shape.Application", "Shape.Domain"],
            ProjectReferences("Shape.Infrastructure"));
        Assert.Equal(
            ["Shape.Application", "Shape.Domain", "Shape.Infrastructure"],
            ProjectReferences("Shape.Api"));
    }

    [Fact]
    public void Analyzer_is_scoped_only_to_core_projects()
    {
        Assert.True(HasAnalyzerReference("Shape.Domain"));
        Assert.True(HasAnalyzerReference("Shape.Application"));
        Assert.False(HasAnalyzerReference("Shape.Infrastructure"));
        Assert.False(HasAnalyzerReference("Shape.Api"));
    }

    [Fact]
    public void Result_and_option_have_only_the_approved_closed_cases()
    {
        Assert.Equal(
            ["Failure", "Success"],
            PublicNestedCases(typeof(Result<string, OrderError>)));
        Assert.Equal(
            ["None", "Some"],
            PublicNestedCases(typeof(Option<string>)));
    }

    [Fact]
    public void Infrastructure_implements_the_application_owned_port()
    {
        Assert.Contains(
            typeof(IOrderStore),
            typeof(InMemoryOrderStore).GetInterfaces());
    }

    private static string[] ReferencedAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] ProjectReferences(string projectName)
    {
        var document = XDocument.Load(ProjectPath(projectName));
        return document
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(
                element.Attribute("Include")?.Value))
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasAnalyzerReference(string projectName)
    {
        var document = XDocument.Load(ProjectPath(projectName));
        var package = document
            .Descendants("PackageReference")
            .SingleOrDefault(element =>
                string.Equals(
                    element.Attribute("Include")?.Value,
                    "CsAssay.Analyzers",
                    StringComparison.Ordinal));
        return package is not null
            && string.Equals(
                package.Attribute("VersionOverride")?.Value,
                "0.1.2",
                StringComparison.Ordinal)
            && string.Equals(
                package.Attribute("PrivateAssets")?.Value,
                "all",
                StringComparison.Ordinal);
    }

    private static string[] PublicNestedCases(Type type) =>
        type.GetNestedTypes(BindingFlags.Public)
            .Where(nested => !nested.IsAbstract)
            .Select(nested => nested.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string ProjectPath(string projectName) =>
        Path.Combine(
            SampleRoot(),
            "src",
            projectName,
            $"{projectName}.csproj");

    private static string SampleRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
}
