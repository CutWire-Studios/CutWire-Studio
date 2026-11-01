import { tilesByCategory, type TileEntry } from "@cutwire/apps"
import { AppIcon, Badge, cn, useReveal } from "@cutwire/ui"
import { ArrowUpRight } from "lucide-react"
import type { CSSProperties } from "react"

function Tile({ tile, index }: { tile: TileEntry; index: number }) {
  const ref = useReveal<HTMLLIElement>(index)
  const soon = tile.status === "coming-soon"
  const body = (
    <>
      <span className="flex size-12 items-center justify-center rounded-full bg-primary text-primary-foreground">
        <AppIcon name={tile.icon} className="size-6" />
      </span>
      <span className="mt-5 flex items-center gap-2">
        <span className="text-headline-md !text-xl text-white">{tile.name}</span>
        {soon && <Badge>Coming soon</Badge>}
        {tile.status === "beta" && <Badge tone="primary">Beta</Badge>}
      </span>
      <span className="mt-1 text-white/70">{tile.tagline}</span>
      {!soon && (
        <ArrowUpRight
          className="absolute top-5 right-5 size-5 text-white/70 transition-transform group-hover:translate-x-0.5 group-hover:-translate-y-0.5"
          aria-hidden
        />
      )}
    </>
  )
  const cls = cn("launcher-frost group relative flex h-full flex-col rounded-2xl p-6", soon ? "cursor-not-allowed opacity-60" : "launcher-frost-hover")
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
      <section className="sticky top-0 z-0 overflow-hidden">
        {/* Photo: Philip Oroni / Unsplash License — curved black and orange layers */}
        <img
          src="/images/hero-layers.jpg"
          alt="Curved abstract layers fading from black into orange"
          className="absolute inset-0 h-full w-full object-cover object-center"
          width={1920}
          height={1080}
          fetchPriority="high"
        />
        <div className="relative px-4 pt-36 pb-36 md:px-6 md:pt-44 md:pb-44">
          <div className="mx-auto w-full max-w-6xl text-center">
            <h1 className="launcher-title">
              <span className="launcher-heading launcher-in block" style={{ "--in": 0 } as CSSProperties}>
                Your creative
              </span>
              <span className="launcher-heading launcher-heading-light launcher-in block" style={{ "--in": 1 } as CSSProperties}>
                studio.
              </span>
            </h1>
            <p className="launcher-lede launcher-in mx-auto mt-8 max-w-3xl text-xl leading-relaxed text-pretty md:text-2xl" style={{ "--in": 2 } as CSSProperties}>
              Make effects, transitions, sounds and themes for Drift, share them with the community, and find free stock media. All in one place, free and open.
            </p>
          </div>
        </div>
      </section>

      <div className="launcher-continue relative z-10 -mt-8 overflow-hidden rounded-t-[2rem]">
        <div className="launcher-continue-photo pointer-events-none absolute inset-0" aria-hidden />
        <div className="launcher-continue-wash pointer-events-none absolute inset-0" aria-hidden />
        <div className="container-cutwire relative py-12 pb-section md:py-16">
          {tilesByCategory().map((group) => (
            <section key={group.category} className="mt-12 first:mt-0" aria-labelledby={`cat-${group.category}`}>
              <h2 id={`cat-${group.category}`} className="text-headline-md mb-5 text-white">
                {group.label}
              </h2>
              <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                {group.tiles.map((t) => (
                  <Tile key={t.id} tile={t} index={index++ % 6} />
                ))}
              </ul>
            </section>
          ))}
        </div>
      </div>
    </>
  )
}
