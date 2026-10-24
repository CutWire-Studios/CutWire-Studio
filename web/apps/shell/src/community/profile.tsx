import { community, num } from "@cutwire/api-client"
import { Avatar, Badge, Button, EmptyState, Skeleton, Spinner } from "@cutwire/ui"
import { UserX } from "lucide-react"
import { Link, useParams } from "react-router-dom"
import { ItemGrid } from "./cards"
import { useAsync, useInfinite, useKinds } from "./data"
import { compact } from "./lib/text"

export function ProfilePage() {
  const { handle = "" } = useParams()
  const { data: kinds } = useKinds()
  const q = useAsync(() => community.user(handle, { limit: 1 }), [handle])
  const feed = useInfinite((cursor) => community.user(handle, { cursor, sort: "new" }).then((u) => u.items), handle)

  if (q.loading && !q.data) return <Skeleton className="h-40 w-full" />
  const u = q.data
  if (!u) {
    return (
      <EmptyState
        icon={<UserX className="size-6" aria-hidden />}
        title="Profile not found"
        action={
          <Button asChild variant="secondary">
            <Link to="/community/browse">Browse the community</Link>
          </Button>
        }
      />
    )
  }
  return (
    <div className="space-y-8">
      <header className="flex flex-wrap items-center gap-5">
        <Avatar src={u.avatarUrl ?? undefined} name={u.displayName} className="size-24 text-2xl" />
        <div className="min-w-0 space-y-1">
          <h1 className="text-headline-lg">{u.displayName}</h1>
          <p className="text-muted-foreground">@{u.handle}</p>
          <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <Badge>{u.trustLevel}</Badge>
            <span>{u.publishedItems} items</span>
            <span>{compact(num(u.totalDownloads))} downloads</span>
            <span>Joined {new Date(u.joinedAt).toLocaleDateString()}</span>
          </div>
        </div>
      </header>
      {u.bio && <p className="max-w-2xl whitespace-pre-line">{u.bio}</p>}
      {!feed.loading && feed.items.length === 0 && !feed.error ? (
        <EmptyState title="No published items yet" />
      ) : (
        <ItemGrid items={feed.items} kinds={kinds} loading={feed.loading} />
      )}
      <div ref={feed.sentinel} className="flex h-10 justify-center">
        {feed.loading && feed.items.length > 0 && <Spinner />}
      </div>
    </div>
  )
}
