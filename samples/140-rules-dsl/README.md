# Small rules DSL

This sample parses a tiny `country=…;minimum=…;category=…` rule language into
an immutable native-union AST, then evaluates it as a pure decision. Parse
failures and business rejections remain separate and are both explicit.

The grammar is intentionally small: it is not an expression engine, a sandbox,
or an authorization system. Its useful lesson is that parsing and evaluation can
be tested without a mutable rule-object hierarchy or a rule-engine dependency.

```bash
dotnet run --project samples/140-rules-dsl -c Release -- --verify
```
