import { useCallback, useState } from "react"
import { KIND_INFO } from "@cutwire/forge-core/doc/kinds"
import { isTrackParam } from "@cutwire/forge-core/doc/params"
import type { Kind } from "@cutwire/forge-core/doc/types"
import { paramSlot } from "@/services/preview/bindings"
import { ASPECTS, getEngine, type Aspect } from "@/services/preview/engine"
import { SAMPLES } from "@/services/preview/media"
import { useDoc } from "@/state/editor"
import { fmt } from "@/shared/ui/widgets"
import { MusicPicker } from "./MusicPicker"
import { SourcePicker } from "./SourcePicker"
import { useEngineStatus } from "./useEngine"

const CLOCK = {
  time: { label: "Time", show: (position: number) => `${fmt(position)}s` },
  progress: { label: "Progress", show: (position: number) => `${Math.round(position * 100)}%` },
}

const TRANSITION_LENGTHS = [0.5, 1, 1.5, 2, 3]

/** Depth and face come from sidecars on the baked samples; any other clip falls back to Drift's own. */
function SampleNote() {
  const usesDepth = useDoc((d) => d.nodes.some((n) => n.type === "depth"))
  const usesFace = useDoc((d) => d.nodes.some((n) => n.type === "face"))
  const clipId = useEngineStatus((s) => s.sources[0])
  const clip = SAMPLES.find((m) => m.id === clipId)
  const missing = [usesDepth && !clip?.depth && "depth", usesFace && !clip?.face && "face"].filter((s): s is string => typeof s === "string")
  if (!missing.length) return null
  return <p className="meta small">This sample has no {missing.join(" or ")} data; Drift will use the clip's own.</p>
}

export function PreviewPanel({ kind, expanded, onToggleExpand }: { kind: Kind; expanded: boolean; onToggleExpand: () => void }) {
  const engine = getEngine()
  const canvasRef = useCallback((c: HTMLCanvasElement | null) => engine.setMainCanvas(c), [engine])
  const playing = useEngineStatus((s) => s.playing)
  const position = useEngineStatus((s) => s.position)
  const duration = useEngineStatus((s) => s.duration)
  const fps = useEngineStatus((s) => s.fps)
  const error = useEngineStatus((s) => s.error)
  const [aspect, setAspect] = useState<Aspect>(engine.aspect)
  const [transDur, setTransDur] = useState(engine.transitionDuration)
  const inputs = useDoc((d) => d.params).filter(isTrackParam)
  const usesMask = useDoc((d) => d.nodes.some((n) => n.type === "clip_mask"))
  const usesAudio = useDoc((d) => d.nodes.some((n) => n.type === "audio"))
  const clock = CLOCK[KIND_INFO[kind].clock]

  return (
    <section className={`preview${expanded ? " expanded" : ""}`}>
      <div className="preview-stage" style={{ aspectRatio: String(ASPECTS[aspect]) }}>
        <canvas ref={canvasRef} className="preview-canvas" />
        {error && (
          <div className="preview-error" role="alert">
            <strong>Can't show this yet</strong>
            <pre>{error}</pre>
          </div>
        )}
      </div>
      <div className="preview-controls">
        <button
          type="button"
          className="icon-btn"
          aria-label={playing ? "Pause" : "Play"}
          onClick={() => engine.setPlaying(!playing)}
        >
          {playing ? (
            <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
              <rect x="6" y="5" width="4" height="14" rx="1" fill="currentColor" />
              <rect x="14" y="5" width="4" height="14" rx="1" fill="currentColor" />
            </svg>
          ) : (
            <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
              <path d="M7 5l12 7-12 7z" fill="currentColor" />
            </svg>
          )}
        </button>
        <input
          className="scrubber"
          type="range"
          min={0}
          max={duration}
          step={0.001}
          value={position}
          aria-label={clock.label}
          onChange={(e) => {
            engine.setPlaying(false)
            engine.seek(Number(e.target.value))
          }}
        />
        <span className="preview-time">{clock.show(position)}</span>
        <button type="button" className="icon-btn" aria-label={expanded ? "Shrink preview" : "Enlarge preview"} onClick={onToggleExpand}>
          <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
            {expanded ? (
              <path d="M9 4v5H4M15 4v5h5M9 20v-5H4M15 20v-5h5" stroke="currentColor" strokeWidth="2" fill="none" />
            ) : (
              <path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" stroke="currentColor" strokeWidth="2" fill="none" />
            )}
          </svg>
        </button>
      </div>
      <div className="preview-sources">
        <SourcePicker slot={0} label={kind === "effect" ? "Preview on" : "From"} />
        {kind === "transition" && <SourcePicker slot={1} label="To" />}
        {usesMask && <SourcePicker slot="mask" label="Clip mask" />}
        {inputs.map((p) =>
          p.type === "audio" ? (
            <MusicPicker key={p.identifier} label={p.displayName} follower={engine.followerFor(p.identifier)} />
          ) : (
            <SourcePicker key={p.identifier} slot={paramSlot(p.identifier)} label={p.displayName} mattes={p.type === "trackMask"} />
          ),
        )}
        {usesAudio && <MusicPicker label="Music" follower={engine.audio} />}
        <label className="source-picker narrow">
          <span>Shape</span>
          <select
            className="input input-sm"
            value={aspect}
            onChange={(e) => {
              const a = e.target.value as Aspect
              engine.setAspect(a)
              setAspect(a)
            }}
          >
            <option value="16:9">16:9</option>
            <option value="9:16">9:16</option>
            <option value="1:1">1:1</option>
            <option value="4:5">4:5</option>
          </select>
        </label>
        {kind === "transition" && (
          <label className="source-picker narrow">
            <span>Length</span>
            <select
              className="input input-sm"
              value={transDur}
              onChange={(e) => {
                engine.setTransitionDuration(Number(e.target.value))
                setTransDur(engine.transitionDuration)
              }}
            >
              {TRANSITION_LENGTHS.map((s) => (
                <option key={s} value={s}>
                  {s}s
                </option>
              ))}
            </select>
          </label>
        )}
        <span className="meta small fps">{fps} fps</span>
      </div>
      <SampleNote />
    </section>
  )
}
