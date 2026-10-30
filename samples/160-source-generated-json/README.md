# Strict source-generated JSON contract

This sample uses a source-generated `System.Text.Json` contract only after a
small strict boundary gate checks the feature-flag document. The gate rejects
duplicates, unknown and missing properties, and shape mismatches that default
deserialization would otherwise not necessarily make a domain decision about.

The source-generated serializer maps the accepted wire shape; immutable policy
still owns the rollout range outcome. This is a narrow alternative to the DOM
comparison in `110-newtonsoft-functional`, not a general serializer performance
or security claim.

```bash
dotnet run --project samples/160-source-generated-json -c Release -- --verify
```
