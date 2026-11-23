using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Playground.RuleMatrix.Impure
{
    public sealed record MutableBuffers(
        Queue<string> Pending,
        Stack<string> Deferred,
        ConcurrentDictionary<string, int> Attempts);
}

namespace Playground.RuleMatrix.Impure.Shell
{
    public static class BlockingTaskControl
    {
        public static int ReadResult(Task<int> task) => task.Result;

        public static void WaitForCompletion(Task task) => task.Wait();

        public static int ReadAwaiter(Task<int> task) => task.GetAwaiter().GetResult();
    }
}
