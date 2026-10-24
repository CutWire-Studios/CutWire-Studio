# CutWire Studio

The CutWire creative workspace at [cutwire.studio](https://cutwire.studio): a launcher for web tools (Forge, Theme Maker, Drift Web), a community hub for sharing Drift effects, transitions, sound effects, themes, LUTs and templates, and stock media.

- `backend/`: .NET 10 API (Clean Architecture), published as a single binary that also serves the web apps.
- `web/`: pnpm workspace with the React apps and shared packages.
- `deploy/`: container image build.
- `docs/`: architecture and design notes.

See [docs/architecture.md](docs/architecture.md).
