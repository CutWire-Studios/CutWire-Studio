import { Route } from "react-router-dom"
import { ConsentPage, DevicePage } from "./consent"
import { PendingProfilePage } from "./pending-profile"
import { SignInPage } from "./sign-in"
import { SignUpPage } from "./sign-up"
import { AppsTab, DangerTab } from "./settings/apps-danger"
import { SettingsLayout } from "./settings/layout"
import { ProfileTab } from "./settings/profile"
import { SecurityTab } from "./settings/security"
import { TokensTab } from "./settings/tokens"
import { ConfirmEmailChangePage, ForgotPasswordPage, ResetPasswordPage, VerifyEmailPage } from "./token-flows"

export const accountRoutes = (
  <Route path="/account">
    <Route path="sign-in" element={<SignInPage />} />
    <Route path="sign-up" element={<SignUpPage />} />
    <Route path="verify-email" element={<VerifyEmailPage />} />
    <Route path="forgot-password" element={<ForgotPasswordPage />} />
    <Route path="reset-password" element={<ResetPasswordPage />} />
    <Route path="confirm-email-change" element={<ConfirmEmailChangePage />} />
    <Route path="pending-profile" element={<PendingProfilePage />} />
    <Route path="consent" element={<ConsentPage />} />
    <Route path="device" element={<DevicePage />} />
    <Route element={<SettingsLayout />}>
      <Route index element={<ProfileTab />} />
      <Route path="security" element={<SecurityTab />} />
      <Route path="tokens" element={<TokensTab />} />
      <Route path="apps" element={<AppsTab />} />
      <Route path="danger" element={<DangerTab />} />
    </Route>
  </Route>
)
