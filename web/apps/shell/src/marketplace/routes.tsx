import { Navigate, Route, useLocation } from "react-router-dom"
import { ItemPage } from "./item-page"
import { legacyStockTarget } from "./lib/layout"
import { MarketplaceView } from "./view"

function LegacyStockRedirect() {
  const { pathname, search } = useLocation()
  return <Navigate to={legacyStockTarget(pathname, search)} replace />
}

export const marketplaceRoutes = (
  <>
    <Route path="/marketplace" element={<MarketplaceView />} />
    <Route path="/marketplace/item/:source/:id" element={<ItemPage />} />
    <Route path="/marketplace/:page" element={<MarketplaceView />} />
    <Route path="/stock/*" element={<LegacyStockRedirect />} />
  </>
)
