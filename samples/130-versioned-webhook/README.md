# Versioned webhook routing

The shell is responsible for HTTP parsing, real signature verification, and its
delivery-id store. It passes only resulting facts into this pure policy. The
policy accepts supported version/event combinations once and returns an explicit
command or rejection.

`SignatureVerified` is deliberately a supplied fact, not a claim that this
sample implements cryptography. `FirstDelivery` is likewise an adapter result,
not durable idempotency storage. The example is a versioning and decision-boundary
study, not a production webhook endpoint.

```bash
dotnet run --project samples/130-versioned-webhook -c Release -- --verify
```
