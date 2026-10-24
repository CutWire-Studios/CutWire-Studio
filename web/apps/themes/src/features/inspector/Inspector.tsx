import { catalog, findToken, getPath, type Json, type JsonObject, type TokenEntry } from "@cutwire/drift-theme"
import { Badge, Button, EmptyState } from "@cutwire/ui"
import { MousePointerClick, RotateCcw } from "lucide-react"
import { useMemo } from "react"
import { isRef, leafName, ownValue, referrersOf } from "@/lib/tokens"
import { useComputed } from "@/state/computed"
import { useEditor } from "@/state/editor"
import { useUi } from "@/state/ui"
import { ArrayEditor, ValueOrRef } from "./editors"
import { display, ValuePreview } from "./refPicker"

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-eyebrow text-muted-foreground uppercase">{title}</h3>
      {children}
    </section>
  )
}

function TokenLink({ path, onSelect, resolved }: { path: string; onSelect(p: string): void; resolved: JsonObject }) {
  const t = findToken(path)
  return (
    <li>
      <button type="button" onClick={() => onSelect(path)} className="flex w-full cursor-pointer items-center gap-2 rounded-md px-2 py-1 text-left text-xs hover:bg-muted">
        {t && <ValuePreview token={t} value={getPath(resolved, path)} />}
        <span className="min-w-0 flex-1 truncate font-mono">{path}</span>
      </button>
    </li>
  )
}

function Editor({ token, raw, resolvedValue }: { token: TokenEntry; raw: Json | undefined; resolvedValue: Json | undefined }) {
  const { resolved } = useComputed()
  const setToken = useEditor((s) => s.setToken)
  const common = { self: token.path, token, merged: resolved.merged, resolved: resolved.document }
  const onChange = (v: Json) => setToken(token.path, v)
  if (token.type === "array") return <ArrayEditor entry={token} raw={raw} resolvedList={resolvedValue} common={common} onChange={onChange} />
  return <ValueOrRef entry={token} common={common} raw={raw} literal={resolvedValue} onChange={onChange} />
}

export function Inspector() {
  const path = useUi((s) => s.selected)
  const select = useUi((s) => s.select)
  const { resolved, base } = useComputed()
  const doc = useEditor((s) => s.doc)
  const side = useEditor((s) => s.side)
  const resetToken = useEditor((s) => s.resetToken)
  const token = path ? findToken(path) : undefined
  const refs = useMemo(() => (path ? referrersOf(resolved.merged, path) : []), [resolved.merged, path])

  if (!path || !token) {
    return (
      <EmptyState
        className="py-12"
        icon={<MousePointerClick className="size-6" aria-hidden />}
        title="Select a token"
        description={path ? `${path} is not a known Drift token.` : "Click a part of the preview or pick a token from the list."}
      />
    )
  }
  const raw = getPath(resolved.merged, token.path)
  const own = ownValue(doc, side, token.path)
  const modified = own !== undefined
  const resolvedValue = getPath(resolved.document, token.path)
  const baseRaw = getPath(base.merged, token.path)
  const siblings = catalog.tokens.filter((t) => t.category === token.category && t.group === token.group && t.path !== token.path).slice(0, 60)

  return (
    <div className="flex flex-col gap-5 p-4">
      <header className="flex flex-col gap-2">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="text-xs text-muted-foreground">{token.category} / {token.group}</p>
            <h2 className="text-headline-sm truncate font-mono text-base font-semibold" title={token.path}>
              {leafName(token.path, token)}
            </h2>
          </div>
          <div className="flex shrink-0 items-center gap-1.5">
            <Badge>{token.type}</Badge>
            {modified && <Badge tone="primary">modified</Badge>}
          </div>
        </div>
        {token.description && <p className="text-sm text-muted-foreground">{token.description}</p>}
        <Button variant="secondary" size="sm" disabled={!modified} onClick={() => resetToken(token.path)} className="self-start">
          <RotateCcw className="size-3.5" aria-hidden /> Reset to base
        </Button>
      </header>

      <Section title="Edit">
        <Editor key={token.path} token={token} raw={raw} resolvedValue={resolvedValue} />
      </Section>

      <Section title="Resolved">
        <div className="flex items-center gap-2 text-sm">
          <ValuePreview token={token} value={resolvedValue} />
          <span className="font-mono text-xs break-all">{display(resolvedValue)}</span>
        </div>
        {isRef(raw) && <p className="font-mono text-xs break-all text-muted-foreground">via {String(raw)}</p>}
      </Section>

      <Section title="Base">
        <p className="text-xs text-muted-foreground">
          Without your override: <span className="font-mono break-all">{display(baseRaw)}</span>
        </p>
        <p className="text-xs text-muted-foreground">
          Drift dark: <span className="font-mono break-all">{display(token.dark)}</span>
          <br />
          Drift light: <span className="font-mono break-all">{display(token.light)}</span>
        </p>
      </Section>

      <Section title={`Referenced by (${refs.length})`}>
        {refs.length === 0 ? (
          <p className="text-xs text-muted-foreground">Nothing points at this token.</p>
        ) : (
          <ul>
            {refs.slice(0, 50).map((r) => (
              <TokenLink key={r} path={r} resolved={resolved.document} onSelect={(p) => select(p, findToken(p)?.group)} />
            ))}
            {refs.length > 50 && <li className="px-2 text-[11px] text-muted-foreground">and {refs.length - 50} more</li>}
          </ul>
        )}
      </Section>

      {siblings.length > 0 && (
        <Section title={`Also in ${token.group}`}>
          <ul>
            {siblings.map((s) => (
              <TokenLink key={s.path} path={s.path} resolved={resolved.document} onSelect={(p) => select(p, token.group)} />
            ))}
          </ul>
        </Section>
      )}
    </div>
  )
}
