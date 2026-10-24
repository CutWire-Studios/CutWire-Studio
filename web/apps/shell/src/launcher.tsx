import { tilesByCategory, type TileEntry } from "@cutwire/apps"
import { AppIcon, Badge, cn, useReveal } from "@cutwire/ui"
import { ArrowUpRight } from "lucide-react"

function Tile({ tile, index }: { tile: TileEntry; index: number }) {
  const ref = useReveal<HTMLLIElement>(index)
  const soon = tile.status === "coming-soon"
  const body = (
    <>
      <span className="flex size-12 items-center justify-center rounded-full bg-primary text-primary-foreground">
        <AppIcon name={tile.icon} className="size-6" />
      </span>
      <span className="mt-5 flex items-center gap-2">
        <span className="text-headline-md !text-xl">{tile.name}</span>
        {soon && <Badge>Coming soon</Badge>}
        {tile.status === "beta" && <Badge tone="primary">Beta</Badge>}
      </span>
      <span className="mt-1 text-muted-foreground">{tile.tagline}</span>
      {!soon && (
        <ArrowUpRight
          className="absolute top-5 right-5 size-5 text-muted-foreground transition-all group-hover:translate-x-0.5 group-hover:-translate-y-0.5 group-hover:text-primary"
          aria-hidden
        />
      )}
    </>
  )
  const cls = cn("panel group relative flex h-full flex-col p-6", soon ? "cursor-not-allowed opacity-60" : "panel-hover")
  return (
    <li ref={ref} data-reveal="" className={`theme-${tile.accent}`}>
      {soon ? (
        <div className={cls} aria-disabled="true">
          {body}
        </div>
      ) : (
        <a href={tile.href} className={cls}>
          {body}
        </a>
      )}
    </li>
  )
}

export function Launcher() {
  let index = 0
  return (
    <>
      <section className="animate-rise py-12 text-center md:py-20">
        <p className="text-eyebrow text-primary uppercase">CutWire Studio</p>
        <h1 className="text-display-xl mt-3">Your creative studio</h1>
        <p className="text-body-lg mx-auto mt-5 max-w-2xl text-muted-foreground">
          Make effects, transitions, sounds and themes for Drift, share them with the community, and find free stock media. All in one place, free and open.
        </p>
      </section>
      {tilesByCategory().map((group) => (
        <section key={group.category} className="mt-12" aria-labelledby={`cat-${group.category}`}>
          <h2 id={`cat-${group.category}`} className="text-headline-md mb-5">
            {group.label}
          </h2>
          <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {group.tiles.map((t) => (
              <Tile key={t.id} tile={t} index={index++ % 6} />
            ))}
          </ul>
        </section>
      ))}
    </>
  )
}
