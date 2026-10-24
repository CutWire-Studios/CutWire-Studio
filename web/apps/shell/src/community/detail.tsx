import { ApiError, community, num, type ItemDetail, type ItemMedia } from "@cutwire/api-client"
import { Avatar, Badge, Button, Card, EmptyState, Skeleton, Tooltip } from "@cutwire/ui"
import { AlertTriangle, Download, ExternalLink, Hammer, Pencil, Rocket } from "lucide-react"
import { useState } from "react"
import { Link, useParams } from "react-router-dom"
import { useMe } from "../account/auth"
import { StripPreview } from "./cards"
import { kindName, useAsync, useKinds } from "./data"
import { compact, formatBytes, paragraphs } from "./lib/text"
import { ReportDialog } from "./report-dialog"
import { VoteButtons } from "./vote-buttons"

export function hasForgeJson(meta: unknown): boolean {
  if (!meta || typeof meta !== "object") return false
  const m = meta as Record<string, unknown>
  if (m.hasForgeJson === true || m.forgeJson === true) return true
  return Array.isArray(m.files) && m.files.some((f) => typeof f === "string" && f.endsWith("forge.json"))
}

function Description({ text }: { text: string }) {
  return (
    <div className="space-y-3 leading-relaxed">
      {paragraphs(text).map((segs, i) => (
        <p key={i} className="break-words whitespace-pre-line">
          {segs.map((s, j) =>
            s.type === "link" ? (
              <a key={j} href={s.href} target="_blank" rel="noopener noreferrer nofollow ugc" className="text-primary underline underline-offset-2">
                {s.text}
              </a>
            ) : (
              <span key={j}>{s.text}</span>
            ),
          )}
        </p>
      ))}
    </div>
  )
}

const ROLE_LABELS: Record<string, string> = { thumbnail: "Thumbnail", "preview-strip": "Preview strip", screenshot: "Screenshot" }
const ROLE_ORDER = ["screenshot", "thumbnail", "preview-strip"]

function Gallery({ media, title }: { media: ItemMedia[]; title: string }) {
  const sorted = [...media].sort((a, b) => ROLE_ORDER.indexOf(a.role) - ROLE_ORDER.indexOf(b.role))
  const [active, setActive] = useState(0)
  const cur = sorted[active]
  const [hover, setHover] = useState(false)
  if (!cur) return <div className="aspect-video rounded-xl bg-muted" />
  const isStrip = cur.role === "preview-strip"
  return (
    <div className="space-y-3">
      <div
        className="relative overflow-hidden rounded-xl bg-muted"
        style={{ aspectRatio: isStrip ? undefined : "16 / 9" }}
        onPointerEnter={() => setHover(true)}
        onPointerLeave={() => setHover(false)}
      >
        <img src={cur.url} alt={`${title} ${ROLE_LABELS[cur.role] ?? cur.role}`} className={isStrip ? "block w-full" : "size-full object-cover"} />
        {isStrip && <p className="sr-only">Animated preview plays on hover.</p>}
        {isStrip && hover && <div className="absolute inset-0"><StripPreview src={cur.url} active /></div>}
      </div>
      {sorted.length > 1 && (
        <ul className="flex gap-2 overflow-x-auto" aria-label="Media">
          {sorted.map((m, i) => (
            <li key={m.role}>
              <button
                type="button"
                onClick={() => setActive(i)}
                aria-label={ROLE_LABELS[m.role] ?? m.role}
                aria-current={i === active}
                className={`block h-14 w-24 cursor-pointer overflow-hidden rounded-lg border-2 ${i === active ? "border-primary" : "border-transparent opacity-70 hover:opacity-100"}`}
              >
                <img src={m.url} alt="" className="size-full object-cover" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export function ItemDetailPage() {
  const { kind = "", slug = "" } = useParams()
  const { data: kinds } = useKinds()
  const { me } = useMe()
  const q = useAsync(() => community.getBySlug(kind, slug), [kind, slug])
  const item = q.data

  if (q.loading && !item) {
    return (
      <div className="grid gap-8 lg:grid-cols-[1fr_22rem]">
        <Skeleton className="aspect-video w-full rounded-xl" />
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }
  if (!item) {
    const notFound = q.error instanceof ApiError && q.error.status === 404
    return (
      <EmptyState
        title={notFound ? "Item not found" : "Could not load this item"}
        description={notFound ? "It may have been removed, or the link is wrong." : q.error?.message}
        action={
          <Button asChild variant="secondary">
            <Link to="/community/browse">Browse the community</Link>
          </Button>
        }
      />
    )
  }
  return <Detail item={item} kinds={kinds} signedInHandle={me?.handle} />
}

function Detail({ item, kinds, signedInHandle }: { item: ItemDetail; kinds?: Parameters<typeof kindName>[0]; signedInHandle?: string }) {
  const own = signedInHandle === item.owner.handle
  const current = item.versions.find((v) => v.isCurrent) ?? item.versions[0]
  const forge = (item.kind === "effect" || item.kind === "transition") && hasForgeJson(item.kindMeta)
  const hidden = item.status === "hidden" || item.status === "removed"

  return (
    <div className="space-y-6">
      {own && hidden && (
        <div role="alert" className="flex items-start gap-3 rounded-xl border border-destructive/40 bg-destructive/10 p-4 text-sm">
          <AlertTriangle className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden />
          <div className="space-y-1">
            <p className="font-semibold">This item is {item.status} and not visible to others.</p>
            {item.visibilityReason && <p>Reason: {item.visibilityReason}</p>}
            {item.purgeAt && <p>It will be permanently deleted on {new Date(item.purgeAt).toLocaleDateString()} unless a moderator restores it.</p>}
          </div>
        </div>
      )}
      {own && item.status === "draft" && (
        <div role="status" className="flex items-center justify-between gap-3 rounded-xl border border-border bg-muted p-4 text-sm">
          <span>This is an unpublished draft.</span>
          <Button asChild size="sm">
            <Link to={`/community/upload?item=${item.id}`}>Continue editing</Link>
          </Button>
        </div>
      )}

      <div className="grid gap-8 lg:grid-cols-[1fr_22rem]">
        <div className="min-w-0 space-y-6">
          <Gallery media={item.media} title={item.title} />
          <div className="space-y-3">
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone="primary">{kindName(kinds, item.kind)}</Badge>
              <Badge>{item.category}</Badge>
              {item.tags.map((t) => (
                <Link key={t} to={`/community/browse?kind=${item.kind}&tags=${encodeURIComponent(t)}`}>
                  <Badge className="hover:bg-border">{t}</Badge>
                </Link>
              ))}
            </div>
            <h1 className="text-headline-lg break-words">{item.title}</h1>
            <p className="text-body-lg text-muted-foreground">{item.summary}</p>
          </div>
          <Description text={item.description} />

          <section className="space-y-3" aria-labelledby="versions">
            <h2 id="versions" className="text-headline-md">
              Versions
            </h2>
            {item.versions.length === 0 && <p className="text-muted-foreground">No versions yet.</p>}
            <ul className="space-y-2">
              {item.versions.map((v) => (
                <li key={v.id} className="panel space-y-1 p-4">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-semibold">v{v.version}</span>
                    {v.isCurrent && <Badge tone="primary">Current</Badge>}
                    <span className="text-sm text-muted-foreground">
                      {new Date(v.createdAt).toLocaleDateString()} · {formatBytes(num(v.size))}
                      {v.minAppVersion && ` · Drift ${v.minAppVersion}+`}
                    </span>
                    <Button asChild variant="ghost" size="sm" className="ml-auto">
                      <a href={v.downloadUrl} download>
                        <Download className="size-4" aria-hidden /> Download
                      </a>
                    </Button>
                  </div>
                  {v.changelog && <p className="text-sm whitespace-pre-line text-muted-foreground">{v.changelog}</p>}
                </li>
              ))}
            </ul>
          </section>
        </div>

        <aside className="space-y-4 lg:sticky lg:top-24 lg:self-start">
          <Card className="space-y-4">
            <div className="flex flex-col gap-2">
              <Button asChild size="lg">
                <a href={community.downloadUrl(item.id)} download>
                  <Download className="size-5" aria-hidden /> Download{current ? ` v${current.version}` : ""}
                </a>
              </Button>
              <Tooltip content="Coming with the next Drift release">
                <span className="block">
                  <Button variant="secondary" size="lg" disabled className="w-full">
                    <Rocket className="size-5" aria-hidden /> Open in Drift
                  </Button>
                </span>
              </Tooltip>
              {forge && (
                <Button asChild variant="secondary" size="lg">
                  <a href={`/forge/import?item=${item.id}`}>
                    <Hammer className="size-5" aria-hidden /> Open in Forge
                  </a>
                </Button>
              )}
              {own && (
                <Button asChild variant="ghost">
                  <Link to={item.status === "draft" ? `/community/upload?item=${item.id}` : `/community/edit/${item.id}`}>
                    <Pencil className="size-4" aria-hidden /> Edit
                  </Link>
                </Button>
              )}
            </div>
            <div className="flex items-center justify-between gap-3">
              <VoteButtons itemId={item.id} ownerHandle={item.owner.handle} score={item.score} viewerVote={item.viewerVote} />
              <ReportDialog itemId={item.id} />
            </div>
            <dl className="grid grid-cols-2 gap-y-2 border-t border-border pt-4 text-sm">
              <dt className="text-muted-foreground">Downloads</dt>
              <dd className="text-right">{compact(num(item.downloads))}</dd>
              <dt className="text-muted-foreground">License</dt>
              <dd className="text-right">{item.license}</dd>
              {current && (
                <>
                  <dt className="text-muted-foreground">Version</dt>
                  <dd className="text-right">{current.version}</dd>
                  <dt className="text-muted-foreground">Min Drift version</dt>
                  <dd className="text-right">{current.minAppVersion || "Any"}</dd>
                </>
              )}
              {item.publishedAt && (
                <>
                  <dt className="text-muted-foreground">Published</dt>
                  <dd className="text-right">{new Date(item.publishedAt).toLocaleDateString()}</dd>
                </>
              )}
            </dl>
          </Card>

          <Card>
            <Link to={`/community/u/${item.owner.handle}`} className="flex items-center gap-3">
              <Avatar src={item.owner.avatarUrl ?? undefined} name={item.owner.displayName} className="size-12" />
              <div className="min-w-0">
                <p className="truncate font-semibold">{item.owner.displayName}</p>
                <p className="flex items-center gap-1 truncate text-sm text-muted-foreground">
                  @{item.owner.handle} <ExternalLink className="size-3" aria-hidden />
                </p>
              </div>
            </Link>
          </Card>
        </aside>
      </div>
    </div>
  )
}
