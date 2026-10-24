using EfBoundary.Domain;
using Microsoft.EntityFrameworkCore;

namespace EfBoundary.Infrastructure;

// Tracked persistence state stays inside the infrastructure assembly. The
// nullable database column deliberately remains visible shell advice (CSAN0004).
public sealed class OrderRow
{
    public int Id { get; set; }
    public int Quantity { get; set; }
    public string? Memo { get; set; }
}

public sealed class OrderDatabase(DbContextOptions<OrderDatabase> options) : DbContext(options)
{
    public DbSet<OrderRow> Orders => Set<OrderRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderRow>().HasKey(row => row.Id);
        modelBuilder.Entity<OrderRow>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<OrderRow>().Property(row => row.Memo).HasMaxLength(120);
    }
}

public sealed class OrderStore(OrderDatabase database)
{
    public async Task SaveAsync(OrderValue order, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var memo = order.Memo switch
        {
            NoMemo => null,
            MemoText text => text.Value
        };
        database.Orders.Add(new OrderRow { Id = order.Id.Value, Quantity = order.Quantity.Value, Memo = memo });
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        database.ChangeTracker.Clear();
    }

    public async Task<OrderOutcome> ReadAsync(OrderId id, CancellationToken cancellationToken)
    {
        var row = await database.Orders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id.Value, cancellationToken).ConfigureAwait(false);
        if (row is null) { return new MissingOrder(id); }
        OrderMemo memo = row.Memo is string text ? new MemoText(text) : new NoMemo();
        return OrderDecisions.FromPersisted(row.Id, row.Quantity, memo);
    }
}
