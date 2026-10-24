import { account, type Passkey, type TwoFactorSetup, type TwoFactorStatus } from "@cutwire/api-client"
import { Button, Dialog, DialogContent, Skeleton, toast } from "@cutwire/ui"
import { Download, Fingerprint, Pencil, Trash2 } from "lucide-react"
import QRCode from "qrcode"
import { useEffect, useState, type FormEvent } from "react"
import { useMe } from "../auth"
import { createPasskeyCredential, fieldError, hasUnmappedError, passkeysSupported, PASSWORD_MIN } from "../lib"
import { FormError, PasswordMeter, Section, SubmitButton, TextField, useAction } from "../ui"

function ChangePassword() {
  const [current, setCurrent] = useState("")
  const [next, setNext] = useState("")
  const action = useAction()
  const submit = (e: FormEvent) => {
    e.preventDefault()
    void action.run(async () => {
      await account.changePassword({ currentPassword: current || undefined, newPassword: next })
      setCurrent("")
      setNext("")
      toast.success("Password changed")
    })
  }
  return (
    <Section title="Password" description="Leave the current password empty if your account does not have one yet.">
      <form onSubmit={submit} className="space-y-4">
        <FormError message={hasUnmappedError(action.error, "currentPassword", "newPassword") ? action.message : null} />
        <TextField label="Current password" type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} error={fieldError(action.error, "currentPassword")} />
        <TextField label="New password" type="password" autoComplete="new-password" required value={next} onChange={(e) => setNext(e.target.value)} error={next && next.length < PASSWORD_MIN ? `Use at least ${PASSWORD_MIN} characters.` : fieldError(action.error, "newPassword")}>
          <PasswordMeter password={next} />
        </TextField>
        <SubmitButton pending={action.pending} disabled={next.length < PASSWORD_MIN}>Change password</SubmitButton>
      </form>
    </Section>
  )
}

function ChangeEmail() {
  const { me } = useMe()
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [sent, setSent] = useState(false)
  const action = useAction()
  const submit = (e: FormEvent) => {
    e.preventDefault()
    void action.run(async () => {
      await account.changeEmail({ newEmail: email.trim(), password })
      setSent(true)
      setPassword("")
      toast.success("Confirmation email sent")
    })
  }
  return (
    <Section title="Email" description={<>Current address: <strong>{me?.email}</strong>{me && !me.emailVerified && " (not verified)"}</>}>
      <form onSubmit={submit} className="space-y-4">
        <FormError message={hasUnmappedError(action.error, "newEmail", "password") ? action.message : null} />
        {sent && <p role="status" className="text-sm text-muted-foreground">We sent a confirmation link to {email}. Your email changes after you open it.</p>}
        <TextField label="New email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} error={fieldError(action.error, "newEmail")} />
        <TextField label="Password" type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} error={fieldError(action.error, "password")} />
        <SubmitButton pending={action.pending}>Change email</SubmitButton>
      </form>
    </Section>
  )
}

function RecoveryCodes({ codes, onClose }: { codes: string[]; onClose: () => void }) {
  const text = codes.join("\n")
  const download = () => {
    const url = URL.createObjectURL(new Blob([text + "\n"], { type: "text/plain" }))
    const a = document.createElement("a")
    a.href = url
    a.download = "cutwire-recovery-codes.txt"
    a.click()
    URL.revokeObjectURL(url)
  }
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent title="Recovery codes" description="Store these somewhere safe. Each code works once and they will not be shown again.">
        <ul className="grid grid-cols-2 gap-2 rounded-lg bg-muted p-3 font-mono text-sm">
          {codes.map((c) => (
            <li key={c}>{c}</li>
          ))}
        </ul>
        <div className="mt-4 flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => void navigator.clipboard.writeText(text).then(() => toast.success("Copied"))}>Copy</Button>
          <Button variant="secondary" onClick={download}>
            <Download className="size-4" aria-hidden />
            Download
          </Button>
          <Button className="ml-auto" onClick={onClose}>I saved them</Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}

function PasswordPrompt({ title, description, confirmLabel, destructive, onConfirm, onClose }: { title: string; description: string; confirmLabel: string; destructive?: boolean; onConfirm: (password: string) => Promise<void>; onClose: () => void }) {
  const [password, setPassword] = useState("")
  const action = useAction()
  const submit = (e: FormEvent) => {
    e.preventDefault()
    void action.run(async () => {
      await onConfirm(password)
      onClose()
    })
  }
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent title={title} description={description}>
        <form onSubmit={submit} className="space-y-4">
          <FormError message={action.message} />
          <TextField label="Password" type="password" autoComplete="current-password" required autoFocus value={password} onChange={(e) => setPassword(e.target.value)} />
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={action.pending} variant={destructive ? "destructive" : "primary"}>{confirmLabel}</SubmitButton>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function TwoFactor() {
  const [status, setStatus] = useState<TwoFactorStatus | null>(null)
  const [setup, setSetup] = useState<TwoFactorSetup | null>(null)
  const [qr, setQr] = useState("")
  const [code, setCode] = useState("")
  const [codes, setCodes] = useState<string[] | null>(null)
  const [prompt, setPrompt] = useState<"disable" | "regenerate" | null>(null)
  const action = useAction()

  const load = () => account.twoFactor().then(setStatus)
  useEffect(() => {
    void load()
  }, [])

  useEffect(() => {
    if (setup) void QRCode.toDataURL(setup.otpAuthUri, { margin: 1, width: 192 }).then(setQr)
  }, [setup])

  const start = () =>
    void action.run(async () => {
      setSetup(await account.twoFactorSetup())
      setCode("")
    })
  const enable = (e: FormEvent) => {
    e.preventDefault()
    void action.run(async () => {
      const res = await account.twoFactorEnable(code.replace(/\s/g, ""))
      setSetup(null)
      setCodes(res.recoveryCodes)
      await load()
      toast.success("Two-step verification enabled")
    })
  }

  if (!status) return <Skeleton className="h-32" />
  return (
    <Section title="Two-step verification" description="Require a code from an authenticator app when you sign in with your password.">
      <div className="space-y-4">
        <FormError message={setup ? null : action.message} />
        {status.enabled ? (
          <>
            <p className="text-sm">Enabled. {status.recoveryCodesLeft} recovery code{status.recoveryCodesLeft === 1 ? "" : "s"} left.</p>
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => setPrompt("regenerate")}>Regenerate recovery codes</Button>
              <Button variant="destructive" onClick={() => setPrompt("disable")}>Disable</Button>
            </div>
          </>
        ) : setup ? (
          <form onSubmit={enable} className="space-y-4">
            <FormError message={action.message} />
            <p className="text-sm">Scan this QR code with your authenticator app, or enter the key manually.</p>
            <div className="flex flex-wrap items-center gap-4">
              {qr && <img src={qr} alt="QR code for your authenticator app" width={192} height={192} className="rounded-lg bg-white" />}
              <div>
                <p className="text-xs text-muted-foreground">Manual key</p>
                <code className="font-mono text-sm break-all select-all">{setup.sharedKey}</code>
              </div>
            </div>
            <TextField label="6-digit code" inputMode="numeric" autoComplete="one-time-code" required value={code} onChange={(e) => setCode(e.target.value)} error={fieldError(action.error, "code")} />
            <div className="flex gap-2">
              <SubmitButton pending={action.pending}>Verify and enable</SubmitButton>
              <Button variant="ghost" onClick={() => setSetup(null)}>Cancel</Button>
            </div>
          </form>
        ) : (
          <Button onClick={start} disabled={action.pending}>Set up authenticator app</Button>
        )}
      </div>
      {codes && <RecoveryCodes codes={codes} onClose={() => setCodes(null)} />}
      {prompt === "disable" && (
        <PasswordPrompt title="Disable two-step verification" description="Enter your password to confirm." confirmLabel="Disable" destructive onClose={() => setPrompt(null)} onConfirm={async (p) => { await account.twoFactorDisable(p); await load(); toast.success("Two-step verification disabled") }} />
      )}
      {prompt === "regenerate" && (
        <PasswordPrompt title="Regenerate recovery codes" description="Your existing codes will stop working." confirmLabel="Regenerate" onClose={() => setPrompt(null)} onConfirm={async (p) => { const r = await account.recoveryCodes(p); setCodes(r.recoveryCodes); await load() }} />
      )}
    </Section>
  )
}

function Passkeys() {
  const [list, setList] = useState<Passkey[] | null>(null)
  const [renaming, setRenaming] = useState<{ id: string; name: string } | null>(null)
  const action = useAction()
  const load = () => account.passkeys().then(setList)
  useEffect(() => {
    void load()
  }, [])

  const add = () =>
    void action.run(async () => {
      const options = await account.passkeyCreationOptions()
      const credential = await createPasskeyCredential(options)
      const name = window.prompt("Name this passkey", "My passkey")?.trim()
      await account.addPasskey(credential, name || undefined)
      await load()
      toast.success("Passkey added")
    })
  const remove = (p: Passkey) =>
    void action.run(async () => {
      if (!window.confirm(`Delete passkey "${p.name}"?`)) return
      await account.deletePasskey(p.id)
      await load()
      toast.success("Passkey deleted")
    })
  const rename = (e: FormEvent) => {
    e.preventDefault()
    if (!renaming) return
    void action.run(async () => {
      await account.renamePasskey(renaming.id, renaming.name.trim())
      setRenaming(null)
      await load()
    })
  }

  return (
    <Section title="Passkeys" description="Sign in with your fingerprint, face or a security key.">
      <div className="space-y-4">
        <FormError message={action.message} />
        {!list ? (
          <Skeleton className="h-16" />
        ) : (
          <ul className="divide-y divide-border">
            {list.length === 0 && <li className="py-2 text-sm text-muted-foreground">No passkeys yet.</li>}
            {list.map((p) => (
              <li key={p.id} className="flex items-center gap-3 py-3">
                <Fingerprint className="size-5 shrink-0 text-muted-foreground" aria-hidden />
                {renaming?.id === p.id ? (
                  <form onSubmit={rename} className="flex flex-1 gap-2">
                    <input aria-label="Passkey name" className="min-h-9 flex-1 rounded-lg border border-border bg-input px-3 text-sm" autoFocus value={renaming.name} onChange={(e) => setRenaming({ id: p.id, name: e.target.value })} />
                    <SubmitButton size="sm">Save</SubmitButton>
                    <Button size="sm" variant="ghost" onClick={() => setRenaming(null)}>Cancel</Button>
                  </form>
                ) : (
                  <>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-semibold">{p.name}</span>
                      <span className="block text-xs text-muted-foreground">Added {new Date(p.createdAt).toLocaleDateString()}</span>
                    </span>
                    <Button size="sm" variant="ghost" aria-label={`Rename ${p.name}`} onClick={() => setRenaming({ id: p.id, name: p.name })}><Pencil className="size-4" aria-hidden /></Button>
                    <Button size="sm" variant="ghost" aria-label={`Delete ${p.name}`} onClick={() => remove(p)}><Trash2 className="size-4" aria-hidden /></Button>
                  </>
                )}
              </li>
            ))}
          </ul>
        )}
        {passkeysSupported() ? (
          <Button variant="secondary" onClick={add} disabled={action.pending}>Add a passkey</Button>
        ) : (
          <p className="text-sm text-muted-foreground">This browser does not support passkeys.</p>
        )}
      </div>
    </Section>
  )
}

function Sessions() {
  const action = useAction()
  return (
    <Section title="Sessions" description="Sign out of every other browser and device. This one stays signed in.">
      <FormError message={action.message} />
      <Button
        variant="secondary"
        disabled={action.pending}
        onClick={() => void action.run(async () => { await account.signOutEverywhere(); toast.success("Signed out everywhere else") })}
      >
        Sign out everywhere
      </Button>
    </Section>
  )
}

export function SecurityTab() {
  return (
    <>
      <ChangePassword />
      <ChangeEmail />
      <TwoFactor />
      <Passkeys />
      <Sessions />
    </>
  )
}
