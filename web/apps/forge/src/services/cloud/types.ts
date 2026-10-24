import type { ForgeDoc } from "@cutwire/forge-core/doc/types"

export type ConflictChoice = "mine" | "server" | "both"

/** What the sync engine needs to know about, and do to, the open editor. */
export interface EditorBridge {
  isOpen(localId: string): boolean
  currentDoc(localId: string): ForgeDoc | null
  /** Replaces the open document as ONE undo step. */
  apply(localId: string, doc: ForgeDoc): void
}

export interface EventSourceLike {
  addEventListener(type: string, fn: (e: { data: string }) => void): void
  close(): void
  onopen: (() => void) | null
}

export interface EngineOptions {
  clientId?: string
  debounceMs?: number
  notify?: (text: string, kind?: "info" | "error") => void
  encodeThumb?: (dataUrl: string) => Promise<Blob | null>
  openEvents?: (url: string) => EventSourceLike
  bridge?: EditorBridge
}

export type LinkResult = "created" | "adopted" | "conflict" | "failed"

