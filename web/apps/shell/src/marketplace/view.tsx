import { Button, EmptyState, cn } from "@cutwire/ui"
import { Link, useParams } from "react-router-dom"
import { MarketplaceProvider, useMarketplace } from "./manifest"
import { DOWNLOADS_PAGE, pagePath, pageTitle } from "./lib/layout"
import { MarketplacePage } from "./page"
import { Downloads } from "./sections"
import { useDownloads } from "./lib/downloads"

const tab = (isActive: boolean) =>
  cn("text-label-md rounded-full px-4 py-2 whitespace-nowrap transition-colors hover:bg-muted", isActive && "bg-muted text-primary")

function View() {
  const { pages } = useMarketplace()
  const { page: pageId } = useParams()
  const downloads = useDownloads()
  const lang = navigator.language || "en"
  const current = pageId ?? pages[0]?.id
  const active = current === DOWNLOADS_PAGE ? null : pages.find((p) => p.id === current)
  const pending = downloads.filter((d) => d.status !== "ready" && d.status !== "failed").length

  return (
    <div className="space-y-8">
      <nav aria-label="Marketplace" className="-mx-1 flex items-center gap-1 overflow-x-auto px-1 pb-1">
        {pages.map((p, i) => (
          <Link key={p.id} to={i === 0 ? "/marketplace" : pagePath(p.id)} aria-current={p.id === current ? "page" : undefined} className={tab(p.id === current)}>
            {pageTitle(p, lang)}
          </Link>
        ))}
        <Link to={pagePath(DOWNLOADS_PAGE)} aria-current={current === DOWNLOADS_PAGE ? "page" : undefined} className={tab(current === DOWNLOADS_PAGE)}>
          Downloads{pending > 0 && ` (${pending})`}
        </Link>
      </nav>
      {current === DOWNLOADS_PAGE ? (
        <Downloads />
      ) : active ? (
        <MarketplacePage page={active} />
      ) : (
        <EmptyState
          title={pages.length ? "Page not found" : "Nothing to show"}
          description={pages.length ? "This marketplace page does not exist." : "The marketplace has no pages right now."}
          action={pages.length ? <Button asChild><Link to="/marketplace">Back to the marketplace</Link></Button> : undefined}
        />
      )}
    </div>
  )
}

export function MarketplaceView() {
  return (
    <MarketplaceProvider>
      <View />
    </MarketplaceProvider>
  )
}
