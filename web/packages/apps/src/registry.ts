export type AppStatus = "live" | "beta" | "coming-soon"
export type AppCategory = "create" | "community" | "library"
export type AppAccent = "drift" | "community" | "stock" | "forge" | "themes" | "music"
export type AppIconName =
  | "clapperboard"
  | "users"
  | "library"
  | "sparkles"
  | "shuffle"
  | "audio-lines"
  | "palette"
  | "music"

export interface AppEntry {
  id: string
  name: string
  tagline: string
  path: string
  icon: AppIconName
  accent: AppAccent
  status: AppStatus
  category: AppCategory
}

export interface TileEntry {
  id: string
  appId: string
  name: string
  tagline: string
  icon: AppIconName
  category: AppCategory
  accent: AppAccent
  status: AppStatus
  href: string
}

export const apps: readonly AppEntry[] = [
  {
    id: "edit",
    name: "Drift Web",
    tagline: "Edit videos in your browser with Drift.",
    path: "/edit",
    icon: "clapperboard",
    accent: "drift",
    status: "coming-soon",
    category: "create",
  },
  {
    id: "forge",
    name: "Drift Forge",
    tagline: "Build effects, transitions and sounds for Drift.",
    path: "/forge",
    icon: "sparkles",
    accent: "forge",
    status: "live",
    category: "create",
  },
  {
    id: "themes",
    name: "Drift Theme Maker",
    tagline: "Design and share themes for Drift.",
    path: "/themes",
    icon: "palette",
    accent: "themes",
    status: "live",
    category: "create",
  },
  {
    id: "music",
    name: "Music Maker",
    tagline: "Compose music for your videos.",
    path: "/music",
    icon: "music",
    accent: "music",
    status: "coming-soon",
    category: "create",
  },
  {
    id: "community",
    name: "Community",
    tagline: "Browse and share what people make with CutWire.",
    path: "/community",
    icon: "users",
    accent: "community",
    status: "live",
    category: "community",
  },
  {
    id: "stock",
    name: "Stock Library",
    tagline: "Free stock footage, music and sound effects.",
    path: "/stock",
    icon: "library",
    accent: "stock",
    status: "live",
    category: "library",
  },
]

type TileSpec = Omit<TileEntry, "accent" | "status" | "category"> & { category?: AppCategory }

const tileSpecs: readonly TileSpec[] = [
  { id: "edit", appId: "edit", name: "Edit Videos", tagline: "Drift Web, the Drift editor in your browser.", icon: "clapperboard", href: "/edit" },
  { id: "community", appId: "community", name: "Community", tagline: "Browse and share effects, transitions and themes.", icon: "users", href: "/community" },
  { id: "stock", appId: "stock", name: "Stock Library", tagline: "Free footage, music and sound effects.", icon: "library", href: "/stock" },
  { id: "forge-effect", appId: "forge", name: "Effect Maker", tagline: "Build video effects without writing code.", icon: "sparkles", href: "/forge/new?kind=effect" },
  { id: "forge-transition", appId: "forge", name: "Transition Maker", tagline: "Craft transitions between clips.", icon: "shuffle", href: "/forge/new?kind=transition" },
  { id: "forge-audio", appId: "forge", name: "Sound Effect Maker", tagline: "Design sound effects for your edits.", icon: "audio-lines", href: "/forge/new?kind=audio" },
  { id: "themes", appId: "themes", name: "Drift Theme Maker", tagline: "Design themes for the Drift interface.", icon: "palette", href: "/themes" },
  { id: "music", appId: "music", name: "Music Maker", tagline: "Compose music for your videos.", icon: "music", href: "/music" },
]

export function getApp(id: string): AppEntry | undefined {
  return apps.find((a) => a.id === id)
}

export const tiles: readonly TileEntry[] = tileSpecs.map((spec) => {
  const app = getApp(spec.appId)
  if (!app) throw new Error(`Tile ${spec.id} references unknown app ${spec.appId}`)
  return { ...spec, category: spec.category ?? app.category, accent: app.accent, status: app.status }
})

export const categoryLabels: Record<AppCategory, string> = {
  create: "Create",
  community: "Community",
  library: "Library",
}

export function tilesByCategory(): { category: AppCategory; label: string; tiles: TileEntry[] }[] {
  return (Object.keys(categoryLabels) as AppCategory[])
    .map((category) => ({ category, label: categoryLabels[category], tiles: tiles.filter((t) => t.category === category) }))
    .filter((g) => g.tiles.length > 0)
}
