using System.Text.Json;
using Playground.DecisionAssist.Probe;

if (args.Length != 1 || args[0] is not ("weak" or "strong"))
{
    return 64;
}
var weak = new[] { (Amount: 1, Expected: true), (Amount: -1, Expected: false) };
var cases = args[0] == "strong" ? weak.Append((Amount: 0, Expected: false)).ToArray() : weak;
var failures = cases.Count(test => (AmountPolicy.Validate(test.Amount) switch
{
    ValidAmount => true,
    InvalidAmount => false
}) != test.Expected);
Console.WriteLine(JsonSerializer.Serialize(new { tests = cases.Length, failures }));
return failures == 0 ? 0 : 7;
