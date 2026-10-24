import { account, PAT_SCOPES, type Pat } from "@cutwire/api-client"
import { Badge, Button, Checkbox, Select, Skeleton, toast } from "@cutwire/ui"
import { Copy, Trash2 } from "lucide-react"
import { useEffect, useState, type FormEvent } from "react"
import { fieldError, hasUnmappedError, SCOPE_DESCRIPTIONS } from "../lib"
import { FormError, Section, SubmitButton, TextField, useAction } from "../ui"

const EXPIRIES = [
  { value: "30", label: "30 days" },
  { value: "90", label: "90 days" },
  { value: "180", label: "180 days" },
  { value: "365", label: "1 year" },
]

export function TokensTab() {
  const [list, setList] = useState<Pat[] | null>(null)
  const [name, setName] = useState("")
  const [scopes, setScopes] = useState<string[]>([])
  const [days, setDays] = useState("90")
  const [secret, setSecret] = useState<string | null>(null)
  const action = useAction()
  const revoke = useAction()
  const load = () => account.tokens().then(setList)
  useEffect(() => {
    void load()
  }, [])

  const create = (e: FormEvent) => {
    e.preventDefault()
    void action.run(async () => {
      const expiresAt = new Date(Date.now() + Number(days) * 86_400_000).toISOString()
      const res = await account.createToken({ name: name.trim(), scopes, expiresAt })
      setSecret(res.secret)
      setName("")
      setScopes([])
      await load()
    })
  }

  return (
    <>
      <Section title="Create a personal access token" description="Use tokens to call the CutWire API from scripts and tools.">
        <form onSubmit={create} className="space-y-4">
          <FormError message={hasUnmappedError(action.error, "name", "scopes", "expiresAt") ? action.message : null} />
          {secret && (
            <div role="status" className="space-y-2 rounded-lg border border-primary/40 bg-primary/10 p-3">
              <p className="text-sm font-semibold">Copy your token now. It will not be shown again.</p>
              <div className="flex items-center gap-2">
                <code className="min-w-0 flex-1 font-mono text-sm break-all select-all">{secret}</code>
                <Button size="sm" variant="secondary" onClick={() => void navigator.clipboard.writeText(secret).then(() => toast.success("Copied"))}>
                  <Copy className="size-4" aria-hidden />
                  Copy
                </Button>
                <Button size="sm" variant="ghost" onClick={() => setSecret(null)}>Dismiss</Button>
              </div>
            </div>
          )}
          <TextField label="Name" required maxLength={64} value={name} onChange={(e) => setName(e.target.value)} error={fieldError(action.error, "name")} />
          <fieldset className="space-y-2">
            <legend className="text-label-md">Scopes</legend>
            {PAT_SCOPES.map((s) => (
              <label key={s} className="flex cursor-pointer items-start gap-2 text-sm">
                <Checkbox
                  className="mt-0.5"
                  checked={scopes.includes(s)}
                  onCheckedChange={(v) => setScopes((cur) => (v === true ? [...cur, s] : cur.filter((x) => x !== s)))}
                  aria-label={s}
                />
                <span>
                  <span className="font-mono">{s}</span>
                  <span className="block text-xs text-muted-foreground">{SCOPE_DESCRIPTIONS[s]}</span>
                </span>
              </label>
            ))}
            {fieldError(action.error, "scopes") && <p className="text-xs text-destructive">{fieldError(action.error, "scopes")}</p>}
          </fieldset>
          <div className="space-y-1.5">
            <span id="expiry-label" className="text-label-md block">Expires after</span>
            <Select value={days} onValueChange={setDays} options={EXPIRIES} aria-labelledby="expiry-label" className="max-w-48" />
          </div>
          <SubmitButton pending={action.pending} disabled={!name.trim() || scopes.length === 0}>Create token</SubmitButton>
        </form>
      </Section>
      <Section title="Your tokens">
        <FormError message={revoke.message} />
        {!list ? (
          <Skeleton className="h-16" />
        ) : list.length === 0 ? (
          <p className="text-sm text-muted-foreground">No tokens yet.</p>
        ) : (
          <ul className="divide-y divide-border">
            {list.map((t) => (
              <li key={t.id} className="flex items-center gap-3 py-3">
                <span className="min-w-0 flex-1">
                  <span className="flex items-center gap-2 text-sm font-semibold">
                    <span className="truncate">{t.name}</span>
                    {!t.active && <Badge>Inactive</Badge>}
                  </span>
                  <span className="block truncate font-mono text-xs text-muted-foreground">{t.scopes.join(" ")}</span>
                  <span className="block text-xs text-muted-foreground">
                    Expires {new Date(t.expiresAt).toLocaleDateString()}
                    {t.lastUsedAt ? `, last used ${new Date(t.lastUsedAt).toLocaleDateString()}` : ", never used"}
                  </span>
                </span>
                <Button
                  size="sm"
                  variant="ghost"
                  aria-label={`Revoke ${t.name}`}
                  disabled={revoke.pending}
                  onClick={() =>
                    void revoke.run(async () => {
                      if (!window.confirm(`Revoke token "${t.name}"?`)) return
                      await account.revokeToken(t.id)
                      await load()
                      toast.success("Token revoked")
                    })
                  }
                >
                  <Trash2 className="size-4" aria-hidden />
                </Button>
              </li>
            ))}
          </ul>
        )}
      </Section>
    </>
  )
}
