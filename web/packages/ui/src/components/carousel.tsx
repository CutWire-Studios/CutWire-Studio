import { ChevronLeft, ChevronRight } from "lucide-react"
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react"
import { cn } from "../cn"
import { IconButton } from "./button"

/** Horizontal scroll-snap row with prev/next buttons. Children are the slides; `slideClassName` sets their width. */
export function Carousel({
  title,
  action,
  children,
  className,
  slideClassName = "w-64",
}: {
  title?: ReactNode
  action?: ReactNode
  children: ReactNode[] | ReactNode
  className?: string
  slideClassName?: string
}) {
  const rail = useRef<HTMLUListElement>(null)
  const [edges, setEdges] = useState({ start: true, end: true })

  const measure = useCallback(() => {
    const el = rail.current
    if (!el) return
    setEdges({ start: el.scrollLeft <= 2, end: el.scrollLeft + el.clientWidth >= el.scrollWidth - 2 })
  }, [])

  useEffect(() => {
    measure()
    const el = rail.current
    if (!el || typeof ResizeObserver === "undefined") return
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [measure, children])

  const scrollBy = (dir: 1 | -1) => rail.current?.scrollBy({ left: dir * rail.current.clientWidth * 0.85, behavior: "smooth" })
  const slides = Array.isArray(children) ? children : [children]

  return (
    <section className={cn("space-y-3", className)}>
      <header className="flex items-center justify-between gap-3">
        {title && <h2 className="text-headline-md">{title}</h2>}
        <div className="ml-auto flex items-center gap-1">
          {action}
          <IconButton label="Scroll left" className="size-9" disabled={edges.start} onClick={() => scrollBy(-1)}>
            <ChevronLeft className="size-4" aria-hidden />
          </IconButton>
          <IconButton label="Scroll right" className="size-9" disabled={edges.end} onClick={() => scrollBy(1)}>
            <ChevronRight className="size-4" aria-hidden />
          </IconButton>
        </div>
      </header>
      <ul ref={rail} onScroll={measure} className="-mx-1 flex snap-x snap-mandatory gap-4 overflow-x-auto scroll-smooth px-1 pb-2 [scrollbar-width:none]">
        {slides.map((s, i) => (
          <li key={i} className={cn("shrink-0 snap-start", slideClassName)}>
            {s}
          </li>
        ))}
      </ul>
    </section>
  )
}
