import {
  ApiError,
  community,
  type AuditEntry,
  type ModAction,
  type ModQueueEntry,
  type ModQueueType,
  type ModUser,
} from "@cutwire/api-client"
import { Avatar, Badge, Button, Checkbox, EmptyState, Input, Select, Spinner, Tabs, TabsContent, TabsList, TabsTrigger, cn, toast } from "@cutwire/ui"
import { useCallback, useEffect, useState } from "react"
import { Link, useSearchParams } from "react-router-dom"
import { RequireAuth, useMe } from "../account/auth"
import { Thumb } from "./cards"
import { ConfirmDialog } from "./confirm-dialog"
import { useAsync, useInfinite, useKinds, kindName } from "./data"
import { isModerator } from "./roles"
import { REASON_LABELS } from "./report-dialog"
import { summarizeReports } from "./lib/mod"

export function ModPage() {
  return (
    <RequireAuth>
      <ModGate />
    </RequireAuth>
  )
}

function ModGate() {
  const { me } = useMe()
  const [params, setParams] = useSearchParams()
  if (!isModerator(me)) {
    return <EmptyState title="Moderators only" description="You need the moderator or admin role to open the console." />
  }
  const tab = params.get("tab") ?? "reported"
  return (
    <div className="space-y-6">
      <h1 className="text-headline-lg">Moderation</h1>
      <Tabs value={tab} onValueChange={(v) => setParams({ tab: v })}>
        <TabsList>
          {["reported", "hidden", "flagged", "users", "audit"].map((t) => (
            <TabsTrigger key={t} value={t} className="capitalize">
              {t}
            </TabsTrigger>
          ))}
        </TabsList>
        {(["reported", "hidden", "flagged"] as const).map((t) => (
          <TabsContent key={t} value={t}>
            <Queue type={t} />
          </TabsContent>
        ))}
        <TabsContent value="users">
          <Users />
        </TabsContent>
        <TabsContent value="audit">
          <Audit />
        </TabsContent>
      </Tabs>
    </div>
  )
}

interface Pending {
  action: ModAction
  ids: string[]
}

const ACTION_LABEL: Record<ModAction, string> = { restore: "Restore", remove: "Remove now", "schedule-purge": "Schedule purge" }

function Queue({ type }: { type: ModQueueType }) {
  const { data: kinds } = useKinds()
  const feed = useInfinite<ModQueueEntry>(
    (cursor) => community.mod.queue(type, cursor).then((p) => ({ items: p.entries, nextCursor: p.nextCursor })),
    type,
    (e) => e.item.id,
  )
  const [cursor, setCursor] = useState(0)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [pending, setPending] = useState<Pending | null>(null)
  const entries = feed.items

  useEffect(() => {
    setCursor(0)
    setSelected(new Set())
  }, [type])

  const currentId = entries[cursor]?.item.id

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const t = e.target as HTMLElement | null
      if (pending || e.metaKey || e.ctrlKey || e.altKey) return
      if (t && (t.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(t.tagName))) return
      if (document.querySelector("[role=dialog]")) return
      if (e.key === "j") setCursor((c) => Math.min(c + 1, entries.length - 1))
      else if (e.key === "k") setCursor((c) => Math.max(c - 1, 0))
      else if ((e.key === "r" || e.key === "x") && currentId) setPending({ action: e.key === "r" ? "restore" : "remove", ids: [currentId] })
      else if (e.key === " " && currentId) {
        e.preventDefault()
        setSelected((s) => {
          const n = new Set(s)
          if (n.has(currentId)) n.delete(currentId)
          else n.add(currentId)
          return n
        })
      } else return
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [entries.length, currentId, pending])

  useEffect(() => {
    document.getElementById(`mod-${currentId}`)?.scrollIntoView({ block: "nearest" })
  }, [currentId])

  const run = useCallback(
    async (reason: string) => {
      if (!pending) return
      try {
        if (pending.ids.length === 1) await community.mod.itemAction(pending.ids[0]!, pending.action, reason)
        else await community.mod.bulk(pending.ids, pending.action, reason)
      } catch (e) {
        throw new Error(e instanceof ApiError ? e.detail ?? e.title : "The action failed.")
      }
      toast.success(`${ACTION_LABEL[pending.action]}: done for ${pending.ids.length} item${pending.ids.length === 1 ? "" : "s"}.`)
      setSelected(new Set())
      feed.reload()
    },
    [pending, feed],
  )

  const reportAction = async (kind: "dismiss" | "uphold", reportId: string) => {
    try {
      if (kind === "dismiss") await community.mod.dismissReport(reportId)
      else await community.mod.upholdReport(reportId)
      feed.reload()
    } catch (e) {
      toast.error(e instanceof ApiError ? e.detail ?? e.title : "The action failed.")
    }
  }

  const resolveFlag = async (id: string) => {
    try {
      await community.mod.resolveFlag(id)
      feed.reload()
    } catch (e) {
      toast.error(e instanceof ApiError ? e.detail ?? e.title : "The action failed.")
    }
  }

  return (
    <div className="space-y-4 pt-4">
      <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
        <span>
          Shortcuts: <kbd>j</kbd>/<kbd>k</kbd> move, <kbd>space</kbd> select, <kbd>r</kbd> restore, <kbd>x</kbd> remove
        </span>
        {selected.size > 0 && (
          <span className="ml-auto flex items-center gap-2">
            <strong className="text-foreground">{selected.size} selected</strong>
            {(Object.keys(ACTION_LABEL) as ModAction[]).map((a) => (
              <Button key={a} size="sm" variant={a === "remove" ? "destructive" : "secondary"} onClick={() => setPending({ action: a, ids: [...selected] })}>
                {ACTION_LABEL[a]}
              </Button>
            ))}
          </span>
        )}
      </div>

      {feed.error && <EmptyState title="Could not load the queue" description={feed.error.message} action={<Button onClick={feed.reload}>Try again</Button>} />}
      {!feed.loading && !feed.error && entries.length === 0 && <EmptyState title="Queue is clear" description="Nothing needs attention here." />}

      <ul className="space-y-3">
        {entries.map((e, i) => {
          const reports = summarizeReports(e.reports)
          const active = i === cursor
          return (
            <li
              key={e.item.id}
              id={`mod-${e.item.id}`}
              aria-current={active}
              onClick={() => setCursor(i)}
              className={cn("panel flex flex-wrap gap-4 p-3", active && "ring-2 ring-primary")}
            >
              <Checkbox
                aria-label={`Select ${e.item.title}`}
                checked={selected.has(e.item.id)}
                onCheckedChange={(c) =>
                  setSelected((s) => {
                    const n = new Set(s)
                    if (c) n.add(e.item.id)
                    else n.delete(e.item.id)
                    return n
                  })
                }
              />
              <Thumb src={e.item.thumbnailUrl} alt="" className="w-44 shrink-0" />
              <div className="min-w-0 flex-1 space-y-2">
                <div className="flex flex-wrap items-center gap-2">
                  <Link to={`/community/${e.item.kind}/${e.item.slug}`} className="font-semibold hover:underline">
                    {e.item.title}
                  </Link>
                  <Badge tone="primary">{kindName(kinds, e.item.kind)}</Badge>
                  <Badge>{e.item.status}</Badge>
                  <Link to={`/community/u/${e.item.owner.handle}`} className="text-sm text-muted-foreground hover:underline">
                    @{e.item.owner.handle}
                  </Link>
                </div>
                {e.visibilityReason && <p className="text-sm">Reason: {e.visibilityReason}</p>}
                {e.purgeAt && <p className="text-sm text-muted-foreground">Purge scheduled {new Date(e.purgeAt).toLocaleDateString()}</p>}
                {reports.length > 0 && (
                  <p className="flex flex-wrap gap-1.5 text-sm">
                    {reports.map((r) => (
                      <Badge key={r.reason}>
                        {REASON_LABELS[r.reason as keyof typeof REASON_LABELS] ?? r.reason} x{r.count}
                      </Badge>
                    ))}
                  </p>
                )}
                {type === "reported" && (
                  <ul className="space-y-1 text-sm text-muted-foreground">
                    {e.reports.map((r) => (
                      <li key={r.id} className="flex flex-wrap items-center gap-2">
                        <span>
                          {REASON_LABELS[r.reason] ?? r.reason}
                          {r.note ? `: ${r.note}` : ""}
                        </span>
                        <Button variant="ghost" size="sm" onClick={() => void reportAction("dismiss", r.id)}>
                          Dismiss
                        </Button>
                        <Button variant="ghost" size="sm" onClick={() => void reportAction("uphold", r.id)}>
                          Uphold
                        </Button>
                      </li>
                    ))}
                  </ul>
                )}
                {e.flags.length > 0 && (
                  <ul className="space-y-1 text-sm">
                    {e.flags.map((f) => (
                      <li key={f.id} className="flex flex-wrap items-center gap-2">
                        <Badge>{f.code}</Badge>
                        {f.detail && <span className="text-muted-foreground">{f.detail}</span>}
                        <Button variant="ghost" size="sm" onClick={() => void resolveFlag(f.id)}>
                          Resolve
                        </Button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              <div className="flex flex-col gap-1">
                {(Object.keys(ACTION_LABEL) as ModAction[]).map((a) => (
                  <Button key={a} size="sm" variant={a === "remove" ? "destructive" : "secondary"} onClick={() => setPending({ action: a, ids: [e.item.id] })}>
                    {ACTION_LABEL[a]}
                  </Button>
                ))}
              </div>
            </li>
          )
        })}
      </ul>
      <div ref={feed.sentinel} className="flex h-10 justify-center">
        {feed.loading && <Spinner />}
      </div>

      <ConfirmDialog
        open={!!pending}
        destructive={pending?.action === "remove"}
        title={pending ? `${ACTION_LABEL[pending.action]} ${pending.ids.length} item${pending.ids.length === 1 ? "" : "s"}?` : ""}
        description="The reason is saved in the audit log."
        confirmLabel={pending ? ACTION_LABEL[pending.action] : "Confirm"}
        reason="required"
        onClose={() => setPending(null)}
        onConfirm={run}
      />
    </div>
  )
}

const TRUST = ["T0", "T1", "T2", "T3"]
const ROLES = ["moderator", "admin"]

type UserDialog = { kind: "ban" | "unban" | "trust" | "roles"; user: ModUser }

function Users() {
  const { me } = useMe()
  const [q, setQ] = useState("")
  const [submitted, setSubmitted] = useState("")
  const users = useAsync(() => (submitted ? community.mod.users(submitted) : Promise.resolve([] as ModUser[])), [submitted])
  const [dialog, setDialog] = useState<UserDialog | null>(null)
  const [level, setLevel] = useState("T1")
  const [roles, setRoles] = useState<string[]>([])
  const [until, setUntil] = useState("")
  const isAdmin = !!me?.roles.some((r) => r.toLowerCase() === "admin")

  const open = (d: UserDialog) => {
    setLevel(d.user.trustLevel)
    setRoles(d.user.roles.map((r) => r.toLowerCase()))
    setUntil("")
    setDialog(d)
  }

  const confirm = async (reason: string) => {
    if (!dialog) return
    const { user, kind } = dialog
    try {
      if (kind === "ban") await community.mod.ban(user.id, { reason, until: until ? new Date(until).toISOString() : null })
      else if (kind === "unban") await community.mod.unban(user.id, reason)
      else if (kind === "trust") await community.mod.trust(user.id, level, reason)
      else await community.mod.setRoles(user.id, roles, reason)
    } catch (e) {
      throw new Error(e instanceof ApiError ? e.detail ?? e.title : "The action failed.")
    }
    toast.success("Done.")
    users.reload()
  }

  return (
    <div className="space-y-4 pt-4">
      <form
        role="search"
        className="flex max-w-xl gap-2"
        onSubmit={(e) => {
          e.preventDefault()
          setSubmitted(q.trim())
        }}
      >
        <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search by handle, name or email" aria-label="Search users" />
        <Button type="submit">Search</Button>
      </form>
      {users.error && <p role="alert" className="text-sm text-destructive">{users.error.message}</p>}
      {users.loading && <Spinner />}
      {!users.loading && submitted && users.data?.length === 0 && <EmptyState title="No users found" />}
      <ul className="space-y-2">
        {users.data?.map((u) => (
          <li key={u.id} className="panel flex flex-wrap items-center gap-3 p-3">
            <Avatar src={u.avatarUrl ?? undefined} name={u.displayName} />
            <div className="min-w-0 flex-1">
              <Link to={`/community/u/${u.handle}`} className="font-semibold hover:underline">
                {u.displayName} <span className="font-normal text-muted-foreground">@{u.handle}</span>
              </Link>
              <p className="flex flex-wrap gap-1.5 pt-1">
                <Badge>{u.trustLevel}</Badge>
                {u.roles.map((r) => (
                  <Badge key={r} tone="primary">
                    {r}
                  </Badge>
                ))}
                {u.banned && <Badge className="bg-destructive text-destructive-foreground">banned{u.bannedUntil ? ` until ${new Date(u.bannedUntil).toLocaleDateString()}` : ""}</Badge>}
              </p>
            </div>
            <div className="flex flex-wrap gap-1">
              {u.banned ? (
                <Button size="sm" variant="secondary" onClick={() => open({ kind: "unban", user: u })}>
                  Unban
                </Button>
              ) : (
                <Button size="sm" variant="destructive" onClick={() => open({ kind: "ban", user: u })}>
                  Ban
                </Button>
              )}
              <Button size="sm" variant="secondary" onClick={() => open({ kind: "trust", user: u })}>
                Trust level
              </Button>
              {isAdmin && (
                <Button size="sm" variant="secondary" onClick={() => open({ kind: "roles", user: u })}>
                  Roles
                </Button>
              )}
            </div>
          </li>
        ))}
      </ul>

      <ConfirmDialog
        open={!!dialog}
        destructive={dialog?.kind === "ban"}
        title={dialog ? { ban: "Ban user", unban: "Unban user", trust: "Set trust level", roles: "Set roles" }[dialog.kind] + `: @${dialog.user.handle}` : ""}
        description={dialog?.kind === "ban" ? "Banning hides all of their items. Leave the date empty for a permanent ban." : "The reason is saved in the audit log."}
        confirmLabel="Confirm"
        reason="required"
        onClose={() => setDialog(null)}
        onConfirm={confirm}
      >
        {dialog?.kind === "ban" && (
          <label className="block space-y-1">
            <span className="text-label-md">Ban until (optional)</span>
            <Input type="date" value={until} onChange={(e) => setUntil(e.target.value)} />
          </label>
        )}
        {dialog?.kind === "trust" && <Select value={level} onValueChange={setLevel} options={TRUST.map((t) => ({ value: t, label: t }))} />}
        {dialog?.kind === "roles" && (
          <div className="space-y-2">
            {ROLES.map((r) => (
              <label key={r} className="flex items-center gap-2 text-sm capitalize">
                <Checkbox checked={roles.includes(r)} onCheckedChange={(c) => setRoles((cur) => (c ? [...cur, r] : cur.filter((x) => x !== r)))} />
                {r}
              </label>
            ))}
          </div>
        )}
      </ConfirmDialog>
    </div>
  )
}

function Audit() {
  const feed = useInfinite<AuditEntry>(
    (cursor) => community.mod.audit(cursor).then((p) => ({ items: p.entries, nextCursor: p.nextCursor })),
    "audit",
  )
  return (
    <div className="space-y-4 pt-4">
      {feed.error && <EmptyState title="Could not load the audit log" description={feed.error.message} />}
      {!feed.loading && !feed.error && feed.items.length === 0 && <EmptyState title="No audit entries yet" />}
      {feed.items.length > 0 && (
        <div className="panel overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-border text-muted-foreground">
              <tr>
                {["When", "Actor", "Action", "Target", "Reason"].map((h) => (
                  <th key={h} scope="col" className="px-3 py-2 font-medium">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {feed.items.map((a) => (
                <tr key={a.id} className="border-b border-border last:border-0">
                  <td className="px-3 py-2 whitespace-nowrap">{new Date(a.at).toLocaleString()}</td>
                  <td className="px-3 py-2">{a.auto ? <Badge>auto</Badge> : (a.actor ?? "-")}</td>
                  <td className="px-3 py-2">{a.action}</td>
                  <td className="px-3 py-2">
                    {a.targetType} <span className="text-muted-foreground">{a.target}</span>
                  </td>
                  <td className="px-3 py-2">{a.reason ?? ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <div ref={feed.sentinel} className="flex h-10 justify-center">
        {feed.loading && <Spinner />}
      </div>
    </div>
  )
}
