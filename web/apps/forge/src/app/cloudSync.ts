import { useAccount } from "@/services/account/account"
import { useEditor } from "@/state/editor"
import { sync } from "@/services/cloud"
import { toast } from "@/shared/ui/toast"

let started = false

/** Wires the engine to the session, the network, tab visibility and the open editor. */
export function startCloudSync() {
  if (started) return
  started = true
  sync.notifier = toast
  sync.bridge = {
    isOpen: (id) => useEditor.getState().localId === id && !!useEditor.getState().doc,
    currentDoc: (id) => (useEditor.getState().localId === id ? useEditor.getState().doc : null),
    apply: (_id, doc) => useEditor.getState().applyRemote(doc),
  }
  const follow = (s: { loaded: boolean; signedIn: boolean }) => s.loaded && sync.setSignedIn(s.signedIn)
  useAccount.subscribe(follow)
  const acc = useAccount.getState()
  if (acc.loaded) follow(acc)
  else void acc.refresh()

  sync.setOnline(navigator.onLine)
  addEventListener("online", () => sync.setOnline(true))
  addEventListener("offline", () => sync.setOnline(false))
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "hidden") void sync.flush()
    else void sync.refreshList()
  })
}
