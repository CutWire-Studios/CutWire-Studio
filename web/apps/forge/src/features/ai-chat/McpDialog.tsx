import { Modal } from "@/shared/ui/Modal"

export function McpDialog({ onClose }: { onClose: () => void }) {
  const url = `${location.origin}/mcp`
  return (
    <Modal title="Use Drift Forge from your AI app" onClose={onClose}>
      <div className="stack gap-3">
        <p className="meta">
          Connect an MCP-capable app such as Claude or Cursor to your Drift Forge library. You need to be signed in to your CutWire account.
        </p>
        <label className="field">
          <span>Server URL</span>
          <input className="input input-sm mono" readOnly value={url} onFocus={(e) => e.target.select()} />
        </label>
        <label className="field">
          <span>Claude Code</span>
          <input className="input input-sm mono" readOnly value={`claude mcp add --transport http drift-forge ${url}`} onFocus={(e) => e.target.select()} />
        </label>
        <label className="field">
          <span>Cursor and other apps (mcp.json)</span>
          <textarea
            className="input textarea mono"
            rows={5}
            readOnly
            value={JSON.stringify({ mcpServers: { "drift-forge": { url } } }, null, 2)}
            onFocus={(e) => e.target.select()}
          />
        </label>
      </div>
    </Modal>
  )
}
