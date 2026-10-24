import type { Me } from "@cutwire/api-client"

export const isModerator = (me: Me | null | undefined): boolean =>
  !!me?.roles.some((r) => r.toLowerCase() === "moderator" || r.toLowerCase() === "admin")
