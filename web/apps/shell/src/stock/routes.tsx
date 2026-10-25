import { Route } from "react-router-dom"
import { DriftAssetsPage } from "./drift"
import { StockHome } from "./home"
import { ItemPage } from "./item"
import { StockLayout } from "./layout"
import { SearchPage } from "./search"
import { SfxPage } from "./sfx"

export const stockRoutes = (
  <Route path="/stock" element={<StockLayout />}>
    <Route index element={<StockHome />} />
    <Route path="search" element={<SearchPage />} />
    <Route path="item/:id" element={<ItemPage />} />
    <Route path="sfx" element={<SfxPage />} />
    <Route path="drift-assets" element={<DriftAssetsPage />} />
  </Route>
)
