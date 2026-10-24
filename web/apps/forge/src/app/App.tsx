import { lazy, Suspense } from "react"
import { createBrowserRouter, RouterProvider } from "react-router"
import { Home, ImportItem, NewDocument } from "@/features/home"
import { NotFound } from "./NotFound"
import { SyncDialogs } from "@/features/sync"
import { Toasts } from "@/shared/ui/toast"

const Editor = lazy(() => import("@/features/editor").then((m) => ({ default: m.Editor })))

const router = createBrowserRouter([
  { path: "/", element: <Home /> },
  { path: "/new", element: <NewDocument /> },
  { path: "/import", element: <ImportItem /> },
  {
    path: "/edit/:id",
    element: (
      <Suspense fallback={<main className="page empty-page" />}>
        <Editor />
      </Suspense>
    ),
  },
  { path: "*", element: <NotFound /> },
], { basename: "/forge" })

export function App() {
  return (
    <>
      <RouterProvider router={router} />
      <SyncDialogs />
      <Toasts />
    </>
  )
}
