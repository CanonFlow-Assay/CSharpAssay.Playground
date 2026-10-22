using System.Text.Json;
using Playground.DecisionAssist.Probe;

if (args.Length != 1 || args[0] is not ("weak" or "strong" or "unreached" or "broken"))
{
    return 64;
}
var weak = new[] { (Enabled: true, Amount: 1, Expected: true), (Enabled: true, Amount: -1, Expected: false) };
var cases = args[0] switch
{
    "strong" => weak.Append((Enabled: true, Amount: 0, Expected: false)).ToArray(),
    "unreached" => [(false, 1, false), (false, 0, false)],
    "broken" => [(true, 0, true)],
    _ => weak
};
var policyCalls = 0;
var failures = 0;
foreach (var (enabled, amount, expected) in cases)
{
    var accepted = false;
    if (enabled)
    {
        policyCalls++;
        accepted = AmountPolicy.Validate(amount) switch
        {
            ValidAmount => true,
            InvalidAmount => false
        };
    }
    if (accepted != expected) { failures++; }
}
Console.WriteLine(JsonSerializer.Serialize(new { tests = cases.Length, failures, policyCalls }));
return failures == 0 ? 0 : 7;
