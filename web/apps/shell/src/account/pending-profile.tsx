import { account, type PendingProfile } from "@cutwire/api-client"
import { Spinner } from "@cutwire/ui"
import { useEffect, useState, type FormEvent } from "react"
import { useSearchParams } from "react-router-dom"
import { fieldError, hasUnmappedError, sanitizeReturn, validateHandle } from "./lib"
import { HandleField } from "./sign-up"
import { AuthCard, FormError, SubmitButton, TextField, useAction } from "./ui"

export function PendingProfilePage() {
  const [params] = useSearchParams()
  const ret = sanitizeReturn(params.get("return"))
  const [info, setInfo] = useState<PendingProfile | null | undefined>(undefined)
  const [handle, setHandle] = useState("")
  const [displayName, setDisplayName] = useState("")
  const action = useAction()

  useEffect(() => {
    account.pendingProfile().then(
      (p) => {
        setInfo(p)
        setHandle(p.suggestedHandle.toLowerCase())
        setDisplayName(p.suggestedDisplayName)
      },
      () => setInfo(null),
    )
  }, [])

  if (info === undefined) return <Spinner className="mx-auto mt-24" />
  if (info === null)
    return (
      <AuthCard title="Nothing to finish" description="There is no pending sign-up on this browser. Start again from the sign-in page.">
        <a className="font-semibold text-primary hover:underline" href="/account/sign-in">Go to sign in</a>
      </AuthCard>
    )

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (validateHandle(handle)) return
    void action.run(async () => {
      await account.completeProfile({ handle, displayName: displayName.trim() || undefined })
      window.location.assign(ret)
    })
  }

  return (
    <AuthCard title="Finish setting up" description={`Signed in with ${info.provider} as ${info.email}. Pick a handle to complete your profile.`}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <FormError message={hasUnmappedError(action.error, "handle", "displayName") ? action.message : null} />
        <HandleField value={handle} onChange={setHandle} serverError={fieldError(action.error, "handle")} />
        <TextField label="Display name" maxLength={50} value={displayName} onChange={(e) => setDisplayName(e.target.value)} error={fieldError(action.error, "displayName")} />
        <SubmitButton pending={action.pending} disabled={!!validateHandle(handle)} className="w-full" size="lg">
          Continue
        </SubmitButton>
      </form>
    </AuthCard>
  )
}
