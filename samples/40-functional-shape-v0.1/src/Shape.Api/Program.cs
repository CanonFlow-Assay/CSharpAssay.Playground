using Shape.Api;
using Shape.Application;
using Shape.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<InMemoryOrderStore>();
builder.Services.AddSingleton<IOrderStore>(services =>
    services.GetRequiredService<InMemoryOrderStore>());
builder.Services.AddSingleton<SubmitOrderHandler>();

var app = builder.Build();
app.MapPost(
    "/orders",
    async (OrderRequest request, SubmitOrderHandler handler, CancellationToken cancellationToken) =>
    {
        var submission = OrderEndpoint.ToSubmission(request);
        var result = await handler.HandleAsync(submission, cancellationToken)
            .ConfigureAwait(false);
        return OrderEndpoint.ToResponse(result);
    });

app.Run();

public partial class Program;
