# Configuration firewall

This sample turns key/value configuration input into either immutable validated
settings or explicit issues. The policy contains no environment or configuration
provider access: an adapter supplies the values at the shell boundary.

There are deliberately no silent defaults. The narrow `Service:*` contract
requires an absolute HTTP(S) endpoint, retry limit 1–10, and a closed read-only
or write mode. It does not implement secret loading, provider precedence, or a
complete `IConfiguration` replacement.

```bash
dotnet run --project samples/120-configuration-firewall -c Release -- --verify
```
