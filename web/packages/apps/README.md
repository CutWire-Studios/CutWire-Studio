# @cutwire/apps

The app registry that drives the launcher, the header app switcher and the backend app list.

## Add a tool

1. Add an entry to `apps` in `src/registry.ts` (`id`, `name`, `path`, `icon`, `accent`, `status`, `category`). `path` is the SPA prefix and must be unique.
2. Add one or more entries to `tileSpecs` (a tile is what users see in the launcher). One app can have several tiles, e.g. Forge has Effect, Transition and Sound Effect makers. `href` must start with the app `path`.
3. If you use a new icon, add its name to `AppIconName` and map it in `packages/ui/src/components/app-icon.tsx`. If you use a new accent, add it to `AppAccent` and a `.theme-<accent>` block in `packages/ui/src/ui.css`.
4. Create the SPA in `web/apps/<id>` (Vite `base: "/<id>/"`, `build.outDir: ../../dist/<id>`) and add an `AppHost` entry on the backend.
5. Use `status: "coming-soon"` to show the tile disabled until the tool ships.

`pnpm --filter @cutwire/apps test` checks the registry invariants.
