import type { ComponentType } from "react"
import { PageStateProvider } from "./page-state"
import type { Page, Section } from "./lib/layout"
import { sectionComponents } from "./sections"

const components = sectionComponents as unknown as Record<string, ComponentType<{ section: Section }> | undefined>

export function MarketplacePage({ page }: { page: Page }) {
  return (
    <PageStateProvider key={page.id}>
      <div className="space-y-8">
        {page.sections.map((section) => {
          const Component = components[section.type]
          return Component ? <Component key={section.id} section={section} /> : null
        })}
      </div>
    </PageStateProvider>
  )
}
