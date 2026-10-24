import { api, me } from "@cutwire/api-client"
import { create } from "zustand"

export interface Quota {
  userLeft: number
  userLimit: number
  globalLeft: number
}

interface Account {
  loaded: boolean
  signedIn: boolean
  name: string
  quota: Quota | null
  refresh(): Promise<void>
}

export const useAccount = create<Account>((set) => ({
  loaded: false,
  signedIn: false,
  name: "",
  quota: null,
  refresh: async () => {
    try {
      const user = await me()
      set({ loaded: true, signedIn: !!user, name: user ? (user.displayName ?? user.userName) : "", quota: null })
    } catch {
      set({ loaded: true, signedIn: false, name: "", quota: null })
    }
  },
}))

export function signIn() {
  location.assign(`/account/sign-in?return=${encodeURIComponent(location.pathname + location.search)}`)
}

export async function signOut() {
  await api("/account/sign-out", { method: "POST" })
  await useAccount.getState().refresh()
}
