import { account, deviceDecision, type ConsentInfo } from "@cutwire/api-client"
import { Button, Spinner } from "@cutwire/ui"
import { Check, CheckCircle2, XCircle } from "lucide-react"
import { useEffect, useState, type FormEvent } from "react"
import { useNavigate, useSearchParams } from "react-router-dom"
import { useMe } from "./auth"
import { SCOPE_DESCRIPTIONS, sanitizeReturn, withReturn } from "./lib"
import { AuthCard, FormError, SubmitButton, TextField, useAction } from "./ui"

function ScopeList({ scopes }: { scopes: { name: string; displayName?: string; description?: string }[] }) {
  return (
    <ul className="space-y-2">
      {scopes.map((s) => (
        <li key={s.name} className="flex items-start gap-3 rounded-lg bg-muted px-3 py-2">
          <Check className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden />
          <span>
            <span className="block text-sm font-semibold">{s.displayName || s.name}</span>
            <span className="block text-xs text-muted-foreground">{s.description || SCOPE_DESCRIPTIONS[s.name] || s.name}</span>
          </span>
        </li>
      ))}
    </ul>
  )
}

export function ConsentPage() {
  const [params] = useSearchParams()
  const ret = sanitizeReturn(params.get("return"), "")
  const { me } = useMe()
  const navigate = useNavigate()
  const [info, setInfo] = useState<ConsentInfo | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const action = useAction()

  useEffect(() => {
    if (me === null) navigate(withReturn("/account/sign-in", ret), { replace: true })
  }, [me, ret, navigate])

  useEffect(() => {
    if (!ret) {
      setLoadError("This authorization request is missing or invalid.")
      return
    }
    if (!me) return
    account.consentInfo(ret).then(setInfo, (e: Error) => setLoadError(e.message))
  }, [ret, me])

  const decide = (approve: boolean) =>
    void action.run(async () => {
      const res = await account.consent(ret, approve)
      window.location.assign(res.redirect)
    })

  if (loadError) return <AuthCard title="Authorization error"><FormError message={loadError} /></AuthCard>
  if (!info) return <Spinner className="mx-auto mt-24" />

  return (
    <AuthCard title={`Authorize ${info.clientName}`} description={<>Signed in as <strong>{me?.displayName || me?.handle}</strong>. {info.clientName} is requesting access to your CutWire account.</>}>
      <div className="space-y-4">
        <FormError message={action.message} />
        <ScopeList scopes={info.scopes} />
        <div className="flex gap-3">
          <Button variant="secondary" className="flex-1" disabled={action.pending} onClick={() => decide(false)}>
            Deny
          </Button>
          <Button className="flex-1" disabled={action.pending} onClick={() => decide(true)}>
            {action.pending && <Spinner className="size-4" />}
            Approve
          </Button>
        </div>
      </div>
    </AuthCard>
  )
}

export function DevicePage() {
  const [params] = useSearchParams()
  const { me } = useMe()
  const userCode = params.get("user_code")
  const result = params.get("result")
  const error = params.get("error")

  if (result === "approved")
    return (
      <AuthCard title="Device connected">
        <p className="flex items-center gap-2"><CheckCircle2 className="size-5 text-green-600" aria-hidden /> You can return to the application now.</p>
      </AuthCard>
    )
  if (result === "denied")
    return (
      <AuthCard title="Request denied">
        <p className="flex items-center gap-2"><XCircle className="size-5 text-destructive" aria-hidden /> The device was not connected.</p>
      </AuthCard>
    )
  if (userCode && me !== undefined) return <DeviceApprove userCode={userCode} client={params.get("client")} scope={params.get("scope")} signedIn={!!me} />
  if (userCode) return <Spinner className="mx-auto mt-24" />
  return <DeviceEntry error={error} />
}

function DeviceEntry({ error }: { error: string | null }) {
  const [code, setCode] = useState("")
  const submit = (e: FormEvent) => {
    e.preventDefault()
    const normalized = code.trim().toUpperCase()
    if (normalized) window.location.assign(`/connect/verify?user_code=${encodeURIComponent(normalized)}`)
  }
  return (
    <AuthCard title="Connect a device" description="Enter the code shown on your device.">
      <form onSubmit={submit} className="space-y-4">
        <FormError message={error ? "That code is invalid or has expired. Check it and try again." : null} />
        <TextField label="Device code" autoComplete="off" autoCapitalize="characters" spellCheck={false} autoFocus required className="[&_input]:text-center [&_input]:font-mono [&_input]:tracking-widest" value={code} onChange={(e) => setCode(e.target.value)} />
        <SubmitButton className="w-full" size="lg">
          Continue
        </SubmitButton>
      </form>
    </AuthCard>
  )
}

function DeviceApprove({ userCode, client, scope, signedIn }: { userCode: string; client: string | null; scope: string | null; signedIn: boolean }) {
  const navigate = useNavigate()
  const action = useAction()
  const scopes = (scope ?? "").split(/[\s+]+/).filter(Boolean)
  useEffect(() => {
    if (!signedIn) navigate(withReturn("/account/sign-in", `/connect/verify?user_code=${encodeURIComponent(userCode)}`), { replace: true })
  }, [signedIn, userCode, navigate])
  const decide = (accept: boolean) =>
    void action.run(async () => {
      const outcome = await deviceDecision(userCode, accept)
      navigate(`/account/device?result=${outcome}`, { replace: true })
    })
  return (
    <AuthCard title={`Connect ${client ?? "a device"}`} description={<>Confirm that the code <span className="font-mono font-semibold">{userCode}</span> matches the one on your device.</>}>
      <div className="space-y-4">
        <FormError message={action.message} />
        {scopes.length > 0 && <ScopeList scopes={scopes.map((name) => ({ name }))} />}
        <div className="flex gap-3">
          <Button variant="secondary" className="flex-1" disabled={action.pending} onClick={() => decide(false)}>
            Deny
          </Button>
          <Button className="flex-1" disabled={action.pending} onClick={() => decide(true)}>
            Approve
          </Button>
        </div>
      </div>
    </AuthCard>
  )
}
