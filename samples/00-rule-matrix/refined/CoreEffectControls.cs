using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Playground.RuleMatrix.Refined.Core;

namespace Playground.RuleMatrix.Refined.Core
{
    public sealed record DecisionInputs(DateTime Time, Guid Id, string Region);

    public static class CoreEffectControls
    {
        public static DateTime Expires(DecisionInputs inputs) => inputs.Time.AddDays(1);

        public static int ContentLength(string content) => content.Length;
    }
}

namespace Playground.RuleMatrix.Refined.Shell
{
    public static class InputAdapter
    {
        public static DecisionInputs Capture() =>
            new(DateTime.UtcNow, Guid.NewGuid(), Environment.GetEnvironmentVariable("REGION") ?? string.Empty);

        public static string Read(string path) => File.ReadAllText(path);

        public static void Print(string value) => Console.WriteLine(value);

        public static Task<string> Fetch(HttpClient client, Uri uri) => client.GetStringAsync(uri);
    }
}
