using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Playground.RuleMatrix.Refined.Core
{
    public sealed record ImmutableBuffers(
        ImmutableQueue<string> Pending,
        ImmutableStack<string> Deferred,
        ImmutableDictionary<string, int> Attempts);
}

namespace Playground.RuleMatrix.Refined.Shell
{
    public static class AwaitedTaskControl
    {
        public static async Task<int> ReadAsync(Task<int> task) =>
            await task.ConfigureAwait(false);

        public static async Task WaitForCompletionAsync(Task task) =>
            await task.ConfigureAwait(false);
    }
}
