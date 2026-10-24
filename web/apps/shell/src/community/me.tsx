import { community, type ItemCard } from "@cutwire/api-client"
import { Badge, Button, EmptyState, Spinner, Tabs, TabsList, TabsTrigger, toast } from "@cutwire/ui"
import { FilePlus2, Pencil, Trash2 } from "lucide-react"
import { useState } from "react"
import { Link, useSearchParams } from "react-router-dom"
import { RequireAuth } from "../account/auth"
import { Thumb } from "./cards"
import { ConfirmDialog } from "./confirm-dialog"
import { useInfinite, useKinds, kindName } from "./data"

const STATUSES = ["draft", "published", "hidden", "removed"] as const
type Status = (typeof STATUSES)[number]

export function MyItemsPage() {
  return (
    <RequireAuth>
      <Dashboard />
    </RequireAuth>
  )
}

function Dashboard() {
  const [params, setParams] = useSearchParams()
  const status = (STATUSES.includes(params.get("status") as Status) ? params.get("status") : "published") as Status
  const { data: kinds } = useKinds()
  const feed = useInfinite((cursor) => community.myItems({ status, cursor }), status)
  const [toDelete, setToDelete] = useState<ItemCard | null>(null)

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-headline-lg">My items</h1>
        <Button asChild>
          <Link to="/community/upload">
            <FilePlus2 className="size-4" aria-hidden /> New upload
          </Link>
        </Button>
      </div>
      <Tabs value={status} onValueChange={(v) => setParams({ status: v })}>
        <TabsList>
          {STATUSES.map((s) => (
            <TabsTrigger key={s} value={s} className="capitalize">
              {s}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      {feed.error && <EmptyState title="Could not load your items" description={feed.error.message} action={<Button onClick={feed.reload}>Try again</Button>} />}
      {!feed.loading && !feed.error && feed.items.length === 0 && <EmptyState title={`No ${status} items`} description={status === "draft" ? "Drafts you start uploading show up here." : undefined} />}

      <ul className="space-y-3">
        {feed.items.map((i) => (
          <li key={i.id} className="panel flex flex-wrap items-center gap-4 p-3">
            <Thumb src={i.thumbnailUrl} alt="" className="w-40 shrink-0" />
            <div className="min-w-0 flex-1 space-y-1">
              <div className="flex flex-wrap items-center gap-2">
                <Link to={`/community/${i.kind}/${i.slug}`} className="truncate font-semibold hover:underline">
                  {i.title}
                </Link>
                <Badge tone="primary">{kindName(kinds, i.kind)}</Badge>
                <Badge>{i.status}</Badge>
              </div>
              <p className="truncate text-sm text-muted-foreground">{i.summary}</p>
              {(i.status === "hidden" || i.status === "removed") && (
                <p className="text-sm text-destructive">{i.visibilityReason ?? "Hidden by moderation. Open the item for details."}</p>
              )}
            </div>
            <div className="flex items-center gap-1">
              {i.status !== "removed" && (
                <Button asChild variant="secondary" size="sm">
                  <Link to={i.status === "draft" ? `/community/upload?item=${i.id}` : `/community/edit/${i.id}`}>
                    <Pencil className="size-4" aria-hidden /> {i.status === "draft" ? "Continue" : "Edit"}
                  </Link>
                </Button>
              )}
              {i.status !== "removed" && (
                <Button variant="ghost" size="sm" onClick={() => setToDelete(i)}>
                  <Trash2 className="size-4" aria-hidden /> Delete
                </Button>
              )}
            </div>
          </li>
        ))}
      </ul>
      <div ref={feed.sentinel} className="flex h-10 justify-center">
        {feed.loading && <Spinner />}
      </div>

      <ConfirmDialog
        open={!!toDelete}
        destructive
        title="Delete this item?"
        description={toDelete ? `"${toDelete.title}" will be removed from the community. This cannot be undone.` : undefined}
        confirmLabel="Delete"
        onClose={() => setToDelete(null)}
        onConfirm={async () => {
          if (!toDelete) return
          await community.remove(toDelete.id)
          toast.success("Item deleted.")
          feed.reload()
        }}
      />
    </div>
  )
}
