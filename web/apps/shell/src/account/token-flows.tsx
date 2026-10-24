import { account } from "@cutwire/api-client"
import { Button, Spinner, toast } from "@cutwire/ui"
import { CheckCircle2, XCircle } from "lucide-react"
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react"
import { Link, useSearchParams } from "react-router-dom"
import { useMe } from "./auth"
import { fieldError, PASSWORD_MIN } from "./lib"
import { AuthCard, FormError, PasswordMeter, SubmitButton, TextField, useAction } from "./ui"

function Result({ ok, title, children, action }: { ok: boolean; title: string; children?: ReactNode; action?: ReactNode }) {
  return (
    <AuthCard title={title}>
      <div className="flex flex-col items-center gap-4 text-center">
        {ok ? <CheckCircle2 className="size-10 text-green-600" aria-hidden /> : <XCircle className="size-10 text-destructive" aria-hidden />}
        {children && <p className="text-muted-foreground">{children}</p>}
        {action}
      </div>
    </AuthCard>
  )
}

const toSignIn = (
  <Button asChild>
    <Link to="/account/sign-in">Go to sign in</Link>
  </Button>
)

/** Runs a one-shot confirmation call when the page loads (guarded against StrictMode double effects). */
function useConfirm(run: (() => Promise<void>) | null) {
  const [state, setState] = useState<"pending" | "ok" | { error: string }>("pending")
  const started = useRef(false)
  useEffect(() => {
    if (!run) {
      setState({ error: "This link is incomplete. Request a new one." })
      return
    }
    if (started.current) return
    started.current = true
    run().then(
      () => setState("ok"),
      (e: Error) => setState({ error: e.message || "This link is invalid or has expired." }),
    )
  }, [run])
  return state
}

export function VerifyEmailPage() {
  const [p] = useSearchParams()
  const userId = p.get("userId")
  const token = p.get("token")
  const state = useConfirm(userId && token ? () => account.verifyEmail({ userId, token }) : null)
  if (state === "pending") return <Spinner className="mx-auto mt-24" />
  if (state === "ok") return <Result ok title="Email verified" action={toSignIn}>Your email is verified. You can sign in now.</Result>
  return (
    <Result ok={false} title="Verification failed" action={toSignIn}>
      {state.error} You can request a new verification email from the sign-in page.
    </Result>
  )
}

export function ConfirmEmailChangePage() {
  const [p] = useSearchParams()
  const { refresh } = useMe()
  const userId = p.get("userId")
  const email = p.get("email")
  const token = p.get("token")
  const state = useConfirm(
    userId && email && token
      ? async () => {
          await account.confirmEmailChange({ userId, email, token })
          await refresh()
        }
      : null,
  )
  if (state === "pending") return <Spinner className="mx-auto mt-24" />
  if (state === "ok")
    return (
      <Result ok title="Email updated" action={<Button asChild><Link to="/account/security">Back to settings</Link></Button>}>
        Your email address is now {email}.
      </Result>
    )
  return <Result ok={false} title="Could not change email">{state.error}</Result>
}

export function ForgotPasswordPage() {
  const [email, setEmail] = useState("")
  const [sent, setSent] = useState(false)
  const action = useAction()
  const submit = (e: FormEvent) => {
    e.preventDefault()
    void action.run(async () => {
      await account.forgotPassword(email.trim())
      setSent(true)
    })
  }
  if (sent)
    return (
      <AuthCard title="Check your email" description="If an account exists for that address, we sent a link to reset the password.">
        <Button asChild variant="secondary" className="w-full">
          <Link to="/account/sign-in">Back to sign in</Link>
        </Button>
      </AuthCard>
    )
  return (
    <AuthCard title="Forgot your password?" description="Enter your email and we will send you a reset link.">
      <form onSubmit={submit} className="space-y-4">
        <FormError message={action.message} />
        <TextField label="Email" type="email" autoComplete="email" required autoFocus value={email} onChange={(e) => setEmail(e.target.value)} />
        <SubmitButton pending={action.pending} className="w-full" size="lg">
          Send reset link
        </SubmitButton>
      </form>
    </AuthCard>
  )
}

export function ResetPasswordPage() {
  const [p] = useSearchParams()
  const email = p.get("email")
  const token = p.get("token")
  const [password, setPassword] = useState("")
  const [done, setDone] = useState(false)
  const action = useAction()
  if (!email || !token) return <Result ok={false} title="Invalid link" action={<Button asChild><Link to="/account/forgot-password">Request a new link</Link></Button>}>This reset link is incomplete.</Result>
  if (done)
    return (
      <Result ok title="Password updated" action={toSignIn}>
        Your password has been reset.
      </Result>
    )
  const short = password.length > 0 && password.length < PASSWORD_MIN
  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (password.length < PASSWORD_MIN) return
    void action.run(async () => {
      await account.resetPassword({ email, token, newPassword: password })
      setDone(true)
      toast.success("Password updated")
    })
  }
  return (
    <AuthCard title="Choose a new password" description={`For ${email}`}>
      <form onSubmit={submit} className="space-y-4">
        <FormError message={fieldError(action.error, "newPassword", "password") ? null : action.message} />
        <TextField
          label="New password"
          type="password"
          autoComplete="new-password"
          required
          autoFocus
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          error={short ? `Use at least ${PASSWORD_MIN} characters.` : fieldError(action.error, "newPassword", "password")}
        >
          <PasswordMeter password={password} />
        </TextField>
        <SubmitButton pending={action.pending} disabled={password.length < PASSWORD_MIN} className="w-full" size="lg">
          Reset password
        </SubmitButton>
      </form>
    </AuthCard>
  )
}
