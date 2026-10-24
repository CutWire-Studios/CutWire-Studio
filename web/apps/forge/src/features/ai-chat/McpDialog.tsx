import { Modal } from "@/shared/ui/Modal"
import { useSync } from "@/services/cloud"

function Snippet({ label, value, rows }: { label: string; value: string; rows?: number }) {
  return (
    <label className="field">
      <span>{label}</span>
      {rows ? (
        <textarea className="input textarea mono" rows={rows} readOnly value={value} onFocus={(e) => e.target.select()} />
      ) : (
        <input className="input input-sm mono" readOnly value={value} onFocus={(e) => e.target.select()} />
      )}
    </label>
  )
}

export function McpDialog({ onClose }: { onClose: () => void }) {
  const url = `${location.origin}/mcp`
  const signedIn = useSync((s) => s.signedIn)
  const json = JSON.stringify(
    { mcpServers: { "cutwire-forge": { type: "http", url, headers: { Authorization: "Bearer cwpat_…" } } } },
    null,
    2,
  )
  const oauthJson = JSON.stringify({ mcpServers: { "cutwire-forge": { type: "http", url } } }, null, 2)
  return (
    <Modal title="Use Drift Forge from your AI app" onClose={onClose}>
      <div className="stack gap-3">
        <p className="meta">
          Connect an MCP-capable app such as Claude or Cursor to your Drift Forge library. It can list, open, create and edit your
          effects, transitions and audio effects.
        </p>
        {!signedIn && (
          <p className="meta" role="note">
            <strong>Sign in first.</strong> The MCP server works on your cloud library, so documents must be synced. Sign in to sync
            your library.
          </p>
        )}
        <Snippet label="Server URL" value={url} />

        <h4>Sign in</h4>
        <p className="meta">
          Apps that support MCP OAuth (Claude Code, Claude Desktop, Cursor and others) open a CutWire sign-in window the first time
          they connect, so there is nothing to copy. If your app doesn't, create a personal access token at{" "}
          <a href="/account/tokens">/account/tokens</a> with the <code>forge:read</code> and <code>forge:write</code> scopes and send it as{" "}
          <code>Authorization: Bearer cwpat_…</code>.
        </p>

        <Snippet label="Claude Code" value={`claude mcp add --transport http cutwire-forge ${url}`} />
        <Snippet label="Claude Code with a token" value={`claude mcp add --transport http cutwire-forge ${url} --header "Authorization: Bearer cwpat_…"`} />
        <Snippet label="Claude Desktop and Cursor (config file, with a token)" value={json} rows={9} />
        <Snippet label="Apps with OAuth (no token)" value={oauthJson} rows={6} />

        <p className="meta">
          Edits your AI app makes show up live in an open editor, as one undo step, so you can always undo them. Only synced
          (signed-in) documents are visible to MCP; local-only documents never leave this browser.
        </p>
      </div>
    </Modal>
  )
}
