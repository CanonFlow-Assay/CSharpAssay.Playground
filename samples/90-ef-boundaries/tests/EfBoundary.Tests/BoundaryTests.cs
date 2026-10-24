using System.Xml.Linq;
using EfBoundary.Domain;
using EfBoundary.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EfBoundary.Tests;

public sealed class BoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Committed_sqlite_row_maps_to_immutable_native_domain_values(bool withMemo)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var database = CreateDatabase(connection);
        await database.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var store = new OrderStore(database);
        OrderMemo memo = withMemo ? new MemoText("delivery") : new NoMemo();
        var expected = new OrderValue(new(7), new(3), memo);
        await store.SaveAsync(expected, TestContext.Current.CancellationToken);
        var outcome = await store.ReadAsync(new(7), TestContext.Current.CancellationToken);
        Assert.Equal(expected, Assert.IsType<StoredOrder>(outcome.Value).Value);
        Assert.Equal(1, await database.Orders.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_database_row_is_a_native_case()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var database = CreateDatabase(connection);
        await database.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new OrderId(99), Assert.IsType<MissingOrder>((await new OrderStore(database).ReadAsync(new(99), TestContext.Current.CancellationToken)).Value).Id);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void Persisted_values_still_cross_the_domain_validation_boundary(int id, int quantity)
    {
        Assert.IsType<InvalidOrder>(OrderDecisions.FromPersisted(id, quantity, new NoMemo()).Value);
    }

    [Fact]
    public async Task Invalid_existing_database_state_does_not_become_an_accepted_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var database = CreateDatabase(connection);
        await database.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        database.Orders.Add(new() { Id = 7, Quantity = -1 });
        await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.ChangeTracker.Clear();
        Assert.IsType<InvalidOrder>((await new OrderStore(database).ReadAsync(new(7), TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public void Domain_has_no_ef_or_infrastructure_reference_and_exposes_no_queryable_api()
    {
        var assembly = typeof(OrderValue).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name is { } name &&
            (name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || name == "EfBoundary.Infrastructure"));
        var project = FindSampleFile("src/EfBoundary.Domain/EfBoundary.Domain.csproj");
        var xml = XDocument.Load(project);
        Assert.Empty(xml.Descendants("ProjectReference"));
        Assert.DoesNotContain(xml.Descendants("PackageReference"), reference => ((string?)reference.Attribute("Include") ?? "").StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance))
            {
                foreach (var apiType in method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
                {
                    Assert.False(typeof(IQueryable).IsAssignableFrom(apiType));
                    Assert.False(typeof(DbContext).IsAssignableFrom(apiType));
                    Assert.NotEqual(typeof(OrderRow).Assembly, apiType.Assembly);
                }
            }
        }
    }

    private static OrderDatabase CreateDatabase(SqliteConnection connection) => new(new DbContextOptionsBuilder<OrderDatabase>().UseSqlite(connection).Options);

    private static string FindSampleFile(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) { return candidate; }
        }
        throw new FileNotFoundException("Sample source is required for the architecture check.", relative);
    }
}
