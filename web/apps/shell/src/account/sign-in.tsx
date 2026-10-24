import { account, type ExternalProvider } from "@cutwire/api-client"
import { Button, Checkbox, toast } from "@cutwire/ui"
import { Fingerprint } from "lucide-react"
import { useEffect, useState, type FormEvent } from "react"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { useMe } from "./auth"
import { getPasskeyAssertion, passkeysSupported, sanitizeReturn, signInOutcome, withReturn, type SignInOutcome } from "./lib"
import { AuthCard, FormError, SubmitButton, TextField, useAction } from "./ui"

const EXTERNAL_ERRORS: Record<string, string> = {
  external_email_unverified: "That provider did not verify your email address, so we could not use it to sign you in.",
  external_email_conflict: "An unverified account already uses that email. Verify it or sign in with your password.",
  locked_out: "This account is temporarily locked. Try again in 10 minutes.",
  external_failed: "Sign-in with that provider failed. Please try again.",
}

type Step = "password" | "two-factor" | "verify"

export function SignInPage() {
  const [params] = useSearchParams()
  const ret = sanitizeReturn(params.get("return"))
  const navigate = useNavigate()
  const { me } = useMe()
  const [step, setStep] = useState<Step>("password")
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [remember, setRemember] = useState(true)
  const [providers, setProviders] = useState<ExternalProvider[]>([])
  const action = useAction()

  useEffect(() => {
    account.externalProviders().then(setProviders, () => setProviders([]))
  }, [])

  useEffect(() => {
    if (me && step === "password") window.location.replace(ret)
  }, [me, step, ret])

  const handle = (o: SignInOutcome) => {
    if (o.kind === "done") window.location.assign(o.to)
    else if (o.kind === "two-factor") setStep("two-factor")
    else if (o.kind === "verify") setStep("verify")
    else if (o.kind === "pending-profile") navigate(o.to)
    else toast.error("Unexpected response from the server.")
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    void action.run(async () => {
      const res = await account.signIn({ email: email.trim(), password, rememberMe: remember })
      handle(signInOutcome(res.status, ret))
    })
  }

  const passkey = () =>
    void action.run(async () => {
      const options = await account.passkeyRequestOptions()
      const credential = await getPasskeyAssertion(options)
      const res = await account.passkeySignIn(credential)
      handle(signInOutcome(res.status, ret))
    })

  if (step === "two-factor") return <TwoFactorStep remember={remember} onDone={handle} ret={ret} />
  if (step === "verify") return <VerifyStep email={email} onBack={() => setStep("password")} />

  const externalError = params.get("error")
  const topError = action.message ?? (externalError ? (EXTERNAL_ERRORS[externalError] ?? EXTERNAL_ERRORS.external_failed!) : null)

  return (
    <AuthCard
      title="Sign in"
      description="Welcome back to CutWire Studio."
      footer={
        <>
          New here?{" "}
          <Link className="font-semibold text-primary hover:underline" to={withReturn("/account/sign-up", ret)}>
            Create an account
          </Link>
        </>
      }
    >
      <form onSubmit={submit} className="space-y-4" noValidate>
        <FormError message={topError} />
        <TextField label="Email" type="email" autoComplete="username webauthn" required value={email} onChange={(e) => setEmail(e.target.value)} />
        <TextField label="Password" type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        <div className="flex items-center justify-between gap-3">
          <label className="flex cursor-pointer items-center gap-2 text-sm">
            <Checkbox checked={remember} onCheckedChange={(v) => setRemember(v === true)} aria-label="Keep me signed in" />
            Keep me signed in
          </label>
          <Link className="text-sm font-semibold text-primary hover:underline" to="/account/forgot-password">
            Forgot password?
          </Link>
        </div>
        <SubmitButton pending={action.pending} className="w-full" size="lg">
          Sign in
        </SubmitButton>
      </form>
      {(passkeysSupported() || providers.length > 0) && (
        <div className="mt-6 space-y-3">
          <div className="flex items-center gap-3 text-xs text-muted-foreground uppercase">
            <span className="h-px flex-1 bg-border" />
            or
            <span className="h-px flex-1 bg-border" />
          </div>
          {passkeysSupported() && (
            <Button variant="secondary" className="w-full" onClick={passkey} disabled={action.pending}>
              <Fingerprint className="size-4" aria-hidden />
              Sign in with a passkey
            </Button>
          )}
          {providers.map((p) => (
            <Button key={p.name} asChild variant="secondary" className="w-full">
              <a href={`/api/v1/account/external/${encodeURIComponent(p.name)}?return=${encodeURIComponent(ret)}`}>Continue with {p.displayName}</a>
            </Button>
          ))}
        </div>
      )}
    </AuthCard>
  )
}

function TwoFactorStep({ remember, onDone, ret }: { remember: boolean; onDone: (o: SignInOutcome) => void; ret: string }) {
  const [useRecovery, setUseRecovery] = useState(false)
  const [code, setCode] = useState("")
  const [machine, setMachine] = useState(false)
  const action = useAction()
  const submit = (e: FormEvent) => {
    e.preventDefault()
    void action.run(async () => {
      const value = code.trim()
      const res = await account.signIn2fa({
        ...(useRecovery ? { recoveryCode: value } : { code: value.replace(/\s/g, "") }),
        rememberMe: remember,
        rememberMachine: machine,
      })
      onDone(signInOutcome(res.status, ret))
    })
  }
  return (
    <AuthCard
      title="Two-step verification"
      description={useRecovery ? "Enter one of your recovery codes." : "Enter the 6-digit code from your authenticator app."}
    >
      <form onSubmit={submit} className="space-y-4">
        <FormError message={action.message} />
        <TextField
          key={String(useRecovery)}
          label={useRecovery ? "Recovery code" : "Authentication code"}
          autoComplete="one-time-code"
          inputMode={useRecovery ? "text" : "numeric"}
          autoFocus
          required
          value={code}
          onChange={(e) => setCode(e.target.value)}
        />
        <label className="flex cursor-pointer items-center gap-2 text-sm">
          <Checkbox checked={machine} onCheckedChange={(v) => setMachine(v === true)} aria-label="Remember this device" />
          Remember this device
        </label>
        <SubmitButton pending={action.pending} className="w-full" size="lg">
          Verify
        </SubmitButton>
        <button
          type="button"
          className="w-full text-sm font-semibold text-primary hover:underline"
          onClick={() => {
            setUseRecovery(!useRecovery)
            setCode("")
            action.reset()
          }}
        >
          {useRecovery ? "Use an authenticator code" : "Use a recovery code"}
        </button>
      </form>
    </AuthCard>
  )
}

export function ResendVerification({ email: initial, onBack }: { email?: string; onBack?: () => void }) {
  const [email, setEmail] = useState(initial ?? "")
  const [sent, setSent] = useState(false)
  const action = useAction()
  const submit = (e: FormEvent) => {
    e.preventDefault()
    void action.run(async () => {
      await account.resendVerification(email.trim())
      setSent(true)
      toast.success("Verification email sent")
    })
  }
  return (
    <form onSubmit={submit} className="space-y-4">
      <FormError message={action.message} />
      {!initial && <TextField label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />}
      <SubmitButton pending={action.pending} className="w-full" variant="secondary">
        {sent ? "Resend again" : "Resend verification email"}
      </SubmitButton>
      {onBack && (
        <button type="button" className="w-full text-sm font-semibold text-primary hover:underline" onClick={onBack}>
          Back to sign in
        </button>
      )}
    </form>
  )
}

function VerifyStep({ email, onBack }: { email: string; onBack: () => void }) {
  return (
    <AuthCard title="Verify your email" description={`Your password is correct, but ${email} is not verified yet. Check your inbox for the verification link.`}>
      <ResendVerification email={email} onBack={onBack} />
    </AuthCard>
  )
}
