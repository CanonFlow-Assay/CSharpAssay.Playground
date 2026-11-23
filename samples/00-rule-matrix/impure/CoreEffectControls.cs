using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Playground.RuleMatrix.Impure;

public static class CoreEffectControls
{
    public static (DateTime Time, Guid Id, string Region) Capture() =>
        (DateTime.UtcNow, Guid.NewGuid(), Environment.GetEnvironmentVariable("REGION") ?? string.Empty);

    public static string Read(string path) => File.ReadAllText(path);

    public static void Print(string value) => Console.WriteLine(value);

    public static Task<string> Fetch(HttpClient client, Uri uri) => client.GetStringAsync(uri);
}
