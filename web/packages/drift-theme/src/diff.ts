import { clone, deepEqual, isObject } from "./merge"
import { mergeLayers } from "./resolve"
import { GROUPS, type Json, type JsonObject, type Library, type ThemeDoc } from "./types"

function prune(value: Json, base: Json | undefined): Json | undefined {
  if (isObject(value) && isObject(base)) {
    const out: JsonObject = {}
    for (const k of Object.keys(value)) {
      const kept = prune(value[k], base[k])
      if (kept !== undefined) out[k] = kept
    }
    return Object.keys(out).length === 0 ? undefined : out
  }
  return deepEqual(value, base) ? undefined : value
}

/**
 * The smallest document that resolves like `doc`: every token that equals what the base (the
 * `extends` chain plus the same-variant built-in underneath) already provides is dropped. Identity
 * fields (`name`, `schemaVersion`, `extends`, `variant`) and `variants` overlays are kept as they are.
 */
export function diffAgainstBase(doc: ThemeDoc, lib: Library): ThemeDoc {
  const identity: JsonObject = {}
  for (const k of ["name", "schemaVersion", "extends", "variant"]) if (k in doc) identity[k] = doc[k]
  const base = mergeLayers(identity, lib).merged

  const out: JsonObject = {}
  for (const k of Object.keys(doc)) {
    if (!(GROUPS as readonly string[]).includes(k)) {
      out[k] = clone(doc[k])
      continue
    }
    const kept = prune(doc[k], base[k])
    if (kept !== undefined) out[k] = clone(kept)
  }
  return out
}
