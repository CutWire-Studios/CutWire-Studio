import { account } from "@cutwire/api-client"
import { Checkbox } from "@cutwire/ui"
import { useState, type FormEvent } from "react"
import { Link, useSearchParams } from "react-router-dom"
import { fieldError, handleError, hasUnmappedError, PASSWORD_MIN, sanitizeReturn, validateHandle, withReturn } from "./lib"
import { ResendVerification } from "./sign-in"
import { AuthCard, FormError, PasswordMeter, SubmitButton, TextField, useAction } from "./ui"

export function HandleField({ value, onChange, serverError }: { value: string; onChange: (v: string) => void; serverError?: string }) {
  const local = value ? validateHandle(value) : null
  return (
    <TextField
      label="Handle"
      autoComplete="username"
      autoCapitalize="none"
      spellCheck={false}
      required
      minLength={3}
      maxLength={24}
      value={value}
      onChange={(e) => onChange(e.target.value.toLowerCase())}
      error={local ?? serverError}
      hint="3-24 characters: lowercase letters, numbers, _ and -"
    />
  )
}

export function SignUpPage() {
  const [params] = useSearchParams()
  const ret = sanitizeReturn(params.get("return"))
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [handle, setHandle] = useState("")
  const [displayName, setDisplayName] = useState("")
  const [terms, setTerms] = useState(false)
  const [done, setDone] = useState(false)
  const action = useAction()

  const passwordShort = password.length > 0 && password.length < PASSWORD_MIN
  const valid = email && password.length >= PASSWORD_MIN && !validateHandle(handle) && terms

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!valid) return
    void action.run(async () => {
      await account.signUp({ email: email.trim(), password, handle, displayName: displayName.trim() || undefined })
      setDone(true)
    })
  }

  if (done)
    return (
      <AuthCard title="Check your email" description={`We sent a verification link to ${email}. Open it to activate your account, then sign in.`}>
        <ResendVerification email={email.trim()} />
        <p className="mt-4 text-center text-sm">
          <Link className="font-semibold text-primary hover:underline" to={withReturn("/account/sign-in", ret)}>
            Go to sign in
          </Link>
        </p>
      </AuthCard>
    )

  return (
    <AuthCard
      title="Create your account"
      footer={
        <>
          Already have an account?{" "}
          <Link className="font-semibold text-primary hover:underline" to={withReturn("/account/sign-in", ret)}>
            Sign in
          </Link>
        </>
      }
    >
      <form onSubmit={submit} className="space-y-4" noValidate>
        <FormError message={hasUnmappedError(action.error, "email", "password", "handle", "displayName") ? action.message : null} />
        <TextField label="Email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} error={fieldError(action.error, "email")} />
        <TextField
          label="Password"
          type="password"
          autoComplete="new-password"
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          error={passwordShort ? `Use at least ${PASSWORD_MIN} characters.` : fieldError(action.error, "password")}
        >
          <PasswordMeter password={password} />
        </TextField>
        <HandleField value={handle} onChange={setHandle} serverError={handleError(action.error)} />
        <TextField label="Display name (optional)" autoComplete="name" maxLength={50} value={displayName} onChange={(e) => setDisplayName(e.target.value)} error={fieldError(action.error, "displayName")} />
        <label className="flex cursor-pointer items-start gap-2 text-sm">
          <Checkbox checked={terms} onCheckedChange={(v) => setTerms(v === true)} className="mt-0.5" aria-label="Accept the terms" />
          <span>
            I accept the <a className="font-semibold text-primary hover:underline" href="/terms" target="_blank" rel="noreferrer">Terms of Service</a> and{" "}
            <a className="font-semibold text-primary hover:underline" href="/privacy" target="_blank" rel="noreferrer">Privacy Policy</a>.
          </span>
        </label>
        <SubmitButton pending={action.pending} disabled={!valid} className="w-full" size="lg">
          Create account
        </SubmitButton>
      </form>
    </AuthCard>
  )
}
