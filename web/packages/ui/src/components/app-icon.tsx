import type { AppIconName } from "@cutwire/apps"
import { AudioLines, Clapperboard, Library, Music, Palette, Shuffle, Sparkles, Users, type LucideIcon } from "lucide-react"
import type { ComponentProps } from "react"

const icons: Record<AppIconName, LucideIcon> = {
  clapperboard: Clapperboard,
  users: Users,
  library: Library,
  sparkles: Sparkles,
  shuffle: Shuffle,
  "audio-lines": AudioLines,
  palette: Palette,
  music: Music,
}

export function AppIcon({ name, ...props }: { name: AppIconName } & ComponentProps<LucideIcon>) {
  const Icon = icons[name]
  return <Icon aria-hidden {...props} />
}
