import { useEffect, useRef } from "react"

export function useReveal<T extends HTMLElement>(index = 0) {
  const ref = useRef<T>(null)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    el.style.setProperty("--reveal-delay", `${Math.min(index, 8) * 60}ms`)
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches
    if (reduced || typeof IntersectionObserver === "undefined") {
      el.dataset.reveal = "in"
      return
    }
    const io = new IntersectionObserver(
      ([entry]) => {
        if (entry?.isIntersecting) {
          el.dataset.reveal = "in"
          io.disconnect()
        }
      },
      { threshold: 0.1 },
    )
    io.observe(el)
    return () => io.disconnect()
  }, [index])
  return ref
}
