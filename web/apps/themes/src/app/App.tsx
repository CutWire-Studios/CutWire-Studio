import { AppHeader, Tabs, TabsContent, TabsList, TabsTrigger, Toaster } from "@cutwire/ui"
import { ContrastPanel } from "@/features/contrast/ContrastPanel"
import { Inspector } from "@/features/inspector/Inspector"
import { Navigator } from "@/features/navigator/Navigator"
import { PreviewStage } from "@/features/preview/PreviewStage"
import { StartScreen } from "@/features/start/StartScreen"
import { IssuesPanel } from "@/features/toolbar/IssuesPanel"
import { Toolbar } from "@/features/toolbar/Toolbar"
import { ComputedProvider, useComputed } from "@/state/computed"
import { useUi, type SideTab } from "@/state/ui"
import { useAutosave } from "./useAutosave"
import { useShortcuts } from "./useShortcuts"

function SidePanel() {
  const { validation } = useComputed()
  const issues = validation.errors.length
  const tab = useUi((s) => s.sideTab)
  const setTab = useUi((s) => s.setSideTab)
  return (
    <Tabs value={tab} onValueChange={(v) => setTab(v as SideTab)} className="flex h-full min-h-0 flex-col">
      <TabsList className="m-2 self-start">
        <TabsTrigger value="inspector">Inspector</TabsTrigger>
        <TabsTrigger value="contrast">Contrast</TabsTrigger>
        <TabsTrigger value="issues">Issues{issues > 0 ? ` (${issues})` : ""}</TabsTrigger>
      </TabsList>
      <TabsContent value="inspector" className="mt-0 min-h-0 flex-1 overflow-y-auto">
        <Inspector />
      </TabsContent>
      <TabsContent value="contrast" className="mt-0 min-h-0 flex-1 overflow-y-auto">
        <ContrastPanel />
      </TabsContent>
      <TabsContent value="issues" className="mt-0 min-h-0 flex-1 overflow-y-auto">
        <IssuesPanel />
      </TabsContent>
    </Tabs>
  )
}

function Editor() {
  useShortcuts(true)
  useAutosave(true)
  return (
    <div className="flex h-dvh flex-col pt-[68px]">
      <Toolbar />
      <div className="grid min-h-0 flex-1 grid-cols-[300px_minmax(0,1fr)_340px]">
        <aside className="min-h-0 border-r border-border bg-surface">
          <Navigator />
        </aside>
        <main className="min-h-0 min-w-0">
          <PreviewStage />
        </main>
        <aside className="min-h-0 border-l border-border bg-surface">
          <SidePanel />
        </aside>
      </div>
    </div>
  )
}

export function App() {
  const view = useUi((s) => s.view)
  return (
    <div className="theme-themes">
      <AppHeader appId="themes" />
      {view === "start" ? (
        <StartScreen />
      ) : (
        <ComputedProvider>
          <Editor />
        </ComputedProvider>
      )}
      <Toaster />
    </div>
  )
}
