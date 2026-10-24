import { useEffect, useRef, useState } from "react"
import { useNavigate } from "react-router"
import { errorMessage } from "@cutwire/forge-core/errors"
import { KIND_INFO } from "@cutwire/forge-core/doc/kinds"
import type { ForgeDoc, Kind } from "@cutwire/forge-core/doc/types"
import { emptyDoc, makeEffectId } from "@cutwire/forge-core/doc/util"
import { importFile } from "@cutwire/forge-core/export/archive"
import { STARTERS } from "@cutwire/forge-core/starters"
import { createSyncedEntry, mergeLibrary, sync, useSync } from "@/services/cloud"
import { deleteEntry, listLibrary, type LibraryEntry } from "@/services/storage/library"
import { SyncBanner, SyncIndicator } from "@/features/sync"
import { AppHeader } from "@/shared/ui/AppHeader"
import { Modal } from "@/shared/ui/Modal"
import { toast } from "@/shared/ui/toast"
import { CloudCard, LibraryCard, StarterCard } from "./cards"
import { useUrlActions } from "./useUrlActions"
import "./home.css"

export function freshCopy(doc: ForgeDoc, name = doc.meta.displayName): ForgeDoc {
  const d = structuredClone(doc)
  d.meta.displayName = name
  d.meta.id = makeEffectId(name)
  return d
}

export function Home() {
  const navigate = useNavigate()
  const [entries, setEntries] = useState<LibraryEntry[] | null>(null)
  const [picker, setPicker] = useState<Kind | null>(null)
  const opening = useUrlActions()
  const fileRef = useRef<HTMLInputElement>(null)

  const signedIn = useSync((s) => s.signedIn)
  const cloud = useSync((s) => s.cloud)
  const lastSyncedAt = useSync((s) => s.lastSyncedAt)

  const refresh = () => listLibrary().then(setEntries)
  useEffect(() => {
    void refresh()
  }, [cloud, lastSyncedAt])
  useEffect(() => {
    if (signedIn) void sync.refresh().then(refresh)
  }, [signedIn])

  const start = async (doc: ForgeDoc) => {
    const id = await createSyncedEntry(doc)
    navigate(`/edit/${id}`)
  }

  const openFile = async (file: File) => {
    try {
      const doc = await importFile(file.name.toLowerCase(), new Uint8Array(await file.arrayBuffer()))
      await start(doc)
    } catch (e) {
      toast(errorMessage(e), "error")
    }
  }

  if (opening) {
    return (
      <>
        <AppHeader />
        <main className="page empty-page">
          <h2>Opening shared effect…</h2>
        </main>
      </>
    )
  }

  return (
    <>
      <AppHeader>
        <SyncIndicator />
        <button type="button" className="btn btn-secondary btn-sm" onClick={() => fileRef.current?.click()}>
          Open file
        </button>
        <input
          ref={fileRef}
          type="file"
          accept=".driftfx,.zip,.json"
          hidden
          onChange={(e) => {
            const f = e.target.files?.[0]
            e.target.value = ""
            if (f) openFile(f)
          }}
        />
      </AppHeader>
      <main
        className="page home"
        onDragOver={(e) => e.preventDefault()}
        onDrop={(e) => {
          e.preventDefault()
          const f = e.dataTransfer.files[0]
          if (f) openFile(f)
        }}
      >
        <section className="hero">
          <h1>Make your own effects, transitions and audio effects for Drift</h1>
          <p className="hero-sub">
            Connect building blocks, watch the result live, then drop it straight into Drift. No code needed, and
            everything stays in your browser.
          </p>
          <div className="new-cards">
            <button type="button" className="new-card" onClick={() => setPicker("effect")}>
              <span className="new-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="28" height="28">
                  <path d="M12 3l2.2 5.6L20 9.5l-4.5 3.9L17 19l-5-3-5 3 1.5-5.6L4 9.5l5.8-.9z" fill="currentColor" />
                </svg>
              </span>
              <strong>New effect</strong>
              <span className="meta">Changes how a clip looks: colour, glitch, glow, distortion…</span>
            </button>
            <button type="button" className="new-card" onClick={() => setPicker("transition")}>
              <span className="new-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="28" height="28">
                  <rect x="3" y="5" width="9" height="14" rx="2" fill="currentColor" opacity=".45" />
                  <rect x="12" y="5" width="9" height="14" rx="2" fill="currentColor" />
                </svg>
              </span>
              <strong>New transition</strong>
              <span className="meta">Moves from one clip to the next: wipes, dissolves, pushes…</span>
            </button>
            <button type="button" className="new-card" onClick={() => start(emptyDoc("audio"))}>
              <span className="new-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="28" height="28">
                  <path d="M4 10v4M8 7v10M12 4v16M16 8v8M20 10v4" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" fill="none" />
                </svg>
              </span>
              <strong>New audio effect</strong>
              <span className="meta">Reshapes sound: echo, chorus, EQ, voice effects…</span>
            </button>
          </div>
        </section>

        <section className="library">
          <h2>Your creations</h2>
          <SyncBanner localOnly={(entries ?? []).filter((e) => !e.cloudId).length} onDone={refresh} />
          <Library entries={entries} cloud={cloud} signedIn={signedIn} refresh={refresh} />
        </section>
      </main>

      {picker && (
        <Modal title={`Start a new ${KIND_INFO[picker].label.toLowerCase()}`} onClose={() => setPicker(null)} wide>
          <div className="card-grid starter-grid">
            {STARTERS.filter((s) => s.doc.kind === picker).map((s) => (
              <StarterCard key={s.name} starter={s} onPick={() => start(freshCopy(s.doc))} />
            ))}
          </div>
        </Modal>
      )}
    </>
  )
}

function Library({
  entries,
  cloud,
  signedIn,
  refresh,
}: {
  entries: LibraryEntry[] | null
  cloud: ReturnType<typeof useSync.getState>["cloud"]
  signedIn: boolean
  refresh: () => void
}) {
  if (entries === null) return null
  const items = mergeLibrary(entries, signedIn ? cloud : null)
  if (!items.length) {
    return (
      <p className="meta">
        Nothing yet. Start with a new effect above, or drop a <code>.driftfx</code> or <code>.zip</code> from
        Drift Forge onto this page.
      </p>
    )
  }
  const run = async (fn: () => Promise<void>) => {
    try {
      await fn()
    } catch (e) {
      toast(errorMessage(e), "error")
    }
    refresh()
  }
  return (
    <div className="card-grid">
      {items.map((item) =>
        item.kind === "cloud" ? (
          <CloudCard
            key={item.cloud.id}
            cloud={item.cloud}
            onDeleteFromCloud={() => {
              if (!confirm(`Delete "${item.cloud.name}" from your cloud library? It isn't saved in this browser.`)) return
              void run(() => sync.deleteFromCloud(item.cloud.id))
            }}
          />
        ) : (
          <LibraryCard
            key={item.localId}
            entry={item.entry}
            onDuplicate={() =>
              run(async () => {
                await createSyncedEntry(freshCopy(item.entry.doc, `${item.entry.doc.meta.displayName} copy`), item.entry.thumb)
              })
            }
            onDelete={() => {
              if (!confirm(`Delete "${item.entry.doc.meta.displayName}"? This can't be undone.`)) return
              void run(() => deleteEntry(item.localId))
            }}
            onDeleteFromCloud={() => {
              if (!confirm(`Delete "${item.entry.doc.meta.displayName}" from the cloud? It stays in this browser.`)) return
              void run(() => sync.deleteFromCloud(item.entry.cloudId!, item.localId))
            }}
            onDeleteEverywhere={() => {
              if (!confirm(`Delete "${item.entry.doc.meta.displayName}" from the cloud and this browser? This can't be undone.`)) return
              void run(() => sync.deleteEverywhere(item.localId, item.entry.cloudId))
            }}
          />
        ),
      )}
    </div>
  )
}
