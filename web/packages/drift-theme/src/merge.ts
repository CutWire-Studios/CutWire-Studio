import type { Json, JsonObject } from "./types"

export function isObject(v: unknown): v is JsonObject {
  return typeof v === "object" && v !== null && !Array.isArray(v)
}

/** Mirrors `deepMerge` in ThemeManager.cpp: objects merge key by key, everything else is replaced. */
export function mergeTheme<T extends Json | undefined>(base: T, overlay: Json | undefined): Json {
  if (isObject(base) && isObject(overlay)) {
    const out: JsonObject = { ...base }
    for (const key of Object.keys(overlay)) {
      out[key] = key in out ? mergeTheme(out[key], overlay[key]) : clone(overlay[key])
    }
    return out
  }
  return clone(overlay as Json)
}

export function clone<T extends Json | undefined>(v: T): T {
  return v === undefined || typeof v !== "object" || v === null ? v : (JSON.parse(JSON.stringify(v)) as T)
}

export function deepEqual(a: Json | undefined, b: Json | undefined): boolean {
  if (a === b) return true
  if (Array.isArray(a) && Array.isArray(b)) return a.length === b.length && a.every((v, i) => deepEqual(v, b[i]))
  if (isObject(a) && isObject(b)) {
    const ka = Object.keys(a)
    return ka.length === Object.keys(b).length && ka.every((k) => k in b && deepEqual(a[k], b[k]))
  }
  return false
}

const own = (o: object, k: string) => Object.prototype.hasOwnProperty.call(o, k)

export function splitPath(path: string): string[] {
  return path.split(".").filter(Boolean)
}

/** Dotted lookup that only descends through objects, as `lookup` does in ThemeManager.cpp. */
export function getPath(root: Json | undefined, path: string): Json | undefined {
  let cur: Json | undefined = root
  const parts = splitPath(path)
  if (parts.length === 0) return undefined
  for (const part of parts) {
    if (!isObject(cur) || !own(cur, part)) return undefined
    cur = cur[part]
  }
  return cur
}

/** Returns a copy of `root` with `path` set. */
export function setPath(root: JsonObject, path: string, value: Json): JsonObject {
  const [head, ...rest] = splitPath(path)
  if (head === undefined) return root
  const out: JsonObject = { ...root }
  if (rest.length === 0) out[head] = value
  else out[head] = setPath(isObject(out[head]) ? (out[head] as JsonObject) : {}, rest.join("."), value)
  return out
}

/** Returns a copy of `root` without `path`; objects emptied by the removal are dropped too. */
export function deletePath(root: JsonObject, path: string): JsonObject {
  const [head, ...rest] = splitPath(path)
  if (head === undefined || !own(root, head)) return root
  const out: JsonObject = { ...root }
  if (rest.length === 0) {
    delete out[head]
    return out
  }
  const child = out[head]
  if (!isObject(child)) return root
  const next = deletePath(child, rest.join("."))
  if (Object.keys(next).length === 0) delete out[head]
  else out[head] = next
  return out
}

/** `{pointer, touch}` pairs are leaves to the editor, so they are not descended into. */
export function isPair(v: Json | undefined): v is { pointer: Json; touch: Json } {
  return isObject(v) && Object.keys(v).length === 2 && "pointer" in v && "touch" in v
}

export function flattenLeaves(root: Json | undefined, prefix = "", out: Array<[string, Json]> = []): Array<[string, Json]> {
  if (isObject(root) && !isPair(root)) {
    for (const k of Object.keys(root)) flattenLeaves(root[k], prefix ? `${prefix}.${k}` : k, out)
  } else if (root !== undefined) {
    out.push([prefix, root])
  }
  return out
}
