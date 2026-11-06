# Contributing

careeroc is a reference implementation: the full source of a product that no longer runs as a hosted service.
It is maintained on a best-effort basis.

## What is welcome

- Bug fixes, each with a test that fails before the fix.
- Documentation fixes, including any sentence that no longer matches the code.
- Small improvements that keep the design in [DESIGN.md](DESIGN.md).

Open an issue before a large change, a new dependency, or a new feature, so the approach is agreed first.
Report security problems privately, as [SECURITY.md](SECURITY.md) describes.

## Making a change

1. Read [AGENTS.md](AGENTS.md), which has the build and test commands and the conventions.
2. Keep the change inside the layer that owns it, and update the Markdown that describes the behavior you changed.
3. Run the test loop for the parts you touched, and all of it for auth, signing, proxy, token, or user-scoping changes.
4. Open a pull request against `main`; CI must pass, and `main` takes squash merges only.

Fixtures and examples stay fictional: no real people, companies, or email addresses.

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md).
