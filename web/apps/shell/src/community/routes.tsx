import { Route } from "react-router-dom"
import { BrowsePage } from "./browse"
import { ItemDetailPage } from "./detail"
import { EditItemPage } from "./edit"
import { CommunityHome } from "./home"
import { CommunityLayout } from "./layout"
import { MyItemsPage } from "./me"
import { ProfilePage } from "./profile"
import { UploadPage } from "./upload/wizard"

/** Static segments come before `:kind/:slug` so they are never read as an item. */
export const communityRoutes = (
  <Route path="/community" element={<CommunityLayout />}>
    <Route index element={<CommunityHome />} />
    <Route path="browse" element={<BrowsePage />} />
    <Route path="upload" element={<UploadPage />} />
    <Route path="me" element={<MyItemsPage />} />
    <Route path="edit/:id" element={<EditItemPage />} />
    <Route path="u/:handle" element={<ProfilePage />} />
    <Route path=":kind/:slug" element={<ItemDetailPage />} />
  </Route>
)
