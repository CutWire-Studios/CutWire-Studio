import { account, type Profile } from "@cutwire/api-client"
import { Avatar, Button, Skeleton, Textarea, toast } from "@cutwire/ui"
import { useEffect, useRef, useState, type FormEvent } from "react"
import { useMe } from "../auth"
import { checkAvatarFile, checkAvatarSize, fieldError, handleError, hasUnmappedError, validateHandle } from "../lib"
import { HandleField } from "../sign-up"
import { FormError, Section, SubmitButton, TextField, useAction } from "../ui"

function readSize(file: File): Promise<{ w: number; h: number }> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file)
    const img = new Image()
    img.onload = () => {
      URL.revokeObjectURL(url)
      resolve({ w: img.naturalWidth, h: img.naturalHeight })
    }
    img.onerror = () => {
      URL.revokeObjectURL(url)
      reject(new Error("That file could not be read as an image."))
    }
    img.src = url
  })
}

function AvatarSection({ profile, onChange }: { profile: Profile; onChange: (p: Profile) => void }) {
  const { refresh } = useMe()
  const input = useRef<HTMLInputElement>(null)
  const action = useAction()
  const [clientError, setClientError] = useState<string | null>(null)

  const pick = async (file: File | undefined) => {
    if (!file) return
    setClientError(null)
    const err = checkAvatarFile(file)
    if (err) return setClientError(err)
    try {
      const { w, h } = await readSize(file)
      const sizeErr = checkAvatarSize(w, h)
      if (sizeErr) return setClientError(sizeErr)
    } catch (e) {
      return setClientError((e as Error).message)
    }
    await action.run(async () => {
      onChange(await account.uploadAvatar(file))
      await refresh()
      toast.success("Avatar updated")
    })
    if (input.current) input.current.value = ""
  }

  const remove = () =>
    void action.run(async () => {
      onChange(await account.deleteAvatar())
      await refresh()
      toast.success("Avatar removed")
    })

  return (
    <Section title="Avatar" description="WebP, PNG or JPEG, up to 256 KB and 512 x 512 pixels.">
      <div className="flex flex-wrap items-center gap-4">
        <Avatar name={profile.displayName || profile.handle} src={profile.avatarUrl ?? undefined} className="size-20 text-xl" />
        <div className="space-y-2">
          <input ref={input} type="file" accept="image/webp,image/png,image/jpeg" className="sr-only" id="avatar-file" onChange={(e) => void pick(e.target.files?.[0])} />
          <div className="flex gap-2">
            <Button variant="secondary" disabled={action.pending} onClick={() => input.current?.click()}>
              Upload image
            </Button>
            {profile.avatarUrl && (
              <Button variant="ghost" disabled={action.pending} onClick={remove}>
                Remove
              </Button>
            )}
          </div>
          <FormError message={clientError ?? action.message} />
        </div>
      </div>
    </Section>
  )
}

export function ProfileTab() {
  const { refresh } = useMe()
  const [profile, setProfile] = useState<Profile | null>(null)
  const [handle, setHandle] = useState("")
  const [displayName, setDisplayName] = useState("")
  const [bio, setBio] = useState("")
  const load = useAction()
  const save = useAction()

  const apply = (p: Profile) => {
    setProfile(p)
    setHandle(p.handle)
    setDisplayName(p.displayName)
    setBio(p.bio)
  }

  useEffect(() => {
    void load.run(async () => apply(await account.profile()))
  }, [])

  if (!profile) return load.error ? <FormError message={load.message} /> : <Skeleton className="h-64" />

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (validateHandle(handle)) return
    void save.run(async () => {
      apply(await account.updateProfile({ handle, displayName: displayName.trim(), bio }))
      await refresh()
      toast.success("Profile saved")
    })
  }

  return (
    <>
      <AvatarSection profile={profile} onChange={(p) => setProfile(p)} />
      <Section title="Profile">
        <form onSubmit={submit} className="space-y-4" noValidate>
          <FormError message={hasUnmappedError(save.error, "handle", "displayName", "bio") ? save.message : null} />
          <HandleField value={handle} onChange={setHandle} serverError={handleError(save.error)} />
          <TextField label="Display name" maxLength={50} value={displayName} onChange={(e) => setDisplayName(e.target.value)} error={fieldError(save.error, "displayName")} />
          <div className="space-y-1.5">
            <label htmlFor="bio" className="text-label-md block">Bio</label>
            <Textarea id="bio" maxLength={500} value={bio} onChange={(e) => setBio(e.target.value)} aria-invalid={fieldError(save.error, "bio") ? true : undefined} />
            {fieldError(save.error, "bio") && <p className="text-xs text-destructive">{fieldError(save.error, "bio")}</p>}
          </div>
          <SubmitButton pending={save.pending}>Save changes</SubmitButton>
        </form>
      </Section>
    </>
  )
}
