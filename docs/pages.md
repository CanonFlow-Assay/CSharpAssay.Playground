# Publishing the rich documentation

The Material-3-inspired site is static HTML, CSS, and JavaScript under `docs/`.
It adds no application runtime, package, or Aspire dependency.
Its concise “what this is and is not” information structure takes presentation
inspiration from the [Aspire introduction](https://aspire.dev/get-started/what-is-aspire/),
without implying any product or runtime integration.

Preview it locally from the repository root:

```text
python3 -m http.server --directory docs 8080
```

After the Draft PR is human-reviewed and merged, a repository administrator can
open [Settings → Pages](https://github.com/CanonFlow-Assay/CSharpAssay.Playground/settings/pages),
choose **Deploy from a branch**, select `main` and `/docs`, then save. Do not
select the feature branch: documentation should publish only from reviewed
`main`.

The evidence gate checks the site's required version, positioning, and rule
content. GitHub Pages serves the reviewed static files; it is not evidence
authority and does not run CSharpAssay.
