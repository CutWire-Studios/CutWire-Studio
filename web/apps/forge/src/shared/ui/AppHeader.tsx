import { logo, AppHeader as StudioHeader } from "@cutwire/ui"
import type { ReactNode } from "react"
import { Link } from "react-router"
import driftIcon from "@/shared/assets/drift-icon.png"
import "./AppHeader.css"

export function Brand() {
  return (
    <div className="brand">
      <a className="mark" href="/" aria-label="CutWire Studio">
        <img src={logo} alt="" />
      </a>
      <span className="divider" aria-hidden="true" />
      <Link className="drift" to="/">
        <img src={driftIcon} alt="" width="28" height="28" />
        <span>
          Drift <b>Forge</b>
        </span>
      </Link>
    </div>
  )
}

export function AppHeader({ children }: { children?: ReactNode }) {
  return (
    <>
      <StudioHeader appId="forge" account={children ? () => children : undefined} />
      <div className="header-spacer" aria-hidden="true" />
    </>
  )
}
