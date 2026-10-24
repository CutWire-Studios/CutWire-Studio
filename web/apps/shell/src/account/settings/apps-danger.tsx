import { account, type ConnectedApp } from "@cutwire/api-client"
import { Button, Skeleton, toast } from "@cutwire/ui"
import { useEffect, useState, type FormEvent } from "react"
import { useMe } from "../auth"
import { FormError, Section, SubmitButton, TextField, useAction } from "../ui"

export function AppsTab() {
  const [list, setList] = useState<ConnectedApp[] | null>(null)
  const action = useAction()
  const load = () => account.connectedApps().then(setList)
  useEffect(() => {
    void load()
  }, [])
  return (
    <Section title="Connected apps" description="Applications you have authorized to access your account.">
      <FormError message={action.message} />
      {!list ? (
        <Skeleton className="h-16" />
      ) : list.length === 0 ? (
        <p className="text-sm text-muted-foreground">No connected apps.</p>
      ) : (
        <ul className="divide-y divide-border">
          {list.map((a) => (
            <li key={a.id} className="flex items-center gap-3 py-3">
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-semibold">{a.clientName}</span>
                <span className="block truncate font-mono text-xs text-muted-foreground">{a.scopes.join(" ")}</span>
                {a.createdAt && <span className="block text-xs text-muted-foreground">Authorized {new Date(a.createdAt).toLocaleDateString()}</span>}
              </span>
              <Button
                size="sm"
                variant="secondary"
                disabled={action.pending}
                onClick={() =>
                  void action.run(async () => {
                    if (!window.confirm(`Revoke access for "${a.clientName}"?`)) return
                    await account.revokeConnectedApp(a.id)
                    await load()
                    toast.success("Access revoked")
                  })
                }
              >
                Revoke
              </Button>
            </li>
          ))}
        </ul>
      )}
    </Section>
  )
}

export function DangerTab() {
  const { me } = useMe()
  const [confirmText, setConfirmText] = useState("")
  const [password, setPassword] = useState("")
  const action = useAction()
  const phrase = me?.handle ?? ""
  const submit = (e: FormEvent) => {
    e.preventDefault()
    void action.run(async () => {
      await account.deleteAccount(password)
      window.location.assign("/")
    })
  }
  return (
    <Section title="Delete account" description={"This anonymizes your profile to \"Deleted user\", revokes all tokens and frees your email. It cannot be undone."}>
      <form onSubmit={submit} className="space-y-4">
        <FormError message={action.message} />
        <TextField label={`Type ${phrase} to confirm`} autoComplete="off" value={confirmText} onChange={(e) => setConfirmText(e.target.value)} />
        <TextField label="Password" type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        <SubmitButton variant="destructive" pending={action.pending} disabled={!phrase || confirmText !== phrase || !password}>
          Delete my account
        </SubmitButton>
      </form>
    </Section>
  )
}
