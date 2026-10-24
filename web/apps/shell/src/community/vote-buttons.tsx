import { ApiError, community, num, type VoteValue } from "@cutwire/api-client"
import { Tooltip, cn, toast } from "@cutwire/ui"
import { ThumbsDown, ThumbsUp } from "lucide-react"
import { useEffect, useReducer } from "react"
import { useLocation, useNavigate } from "react-router-dom"
import { useMe } from "../account/auth"
import { withReturn } from "../account/lib"
import { nextVote, voteReducer, type VoteState } from "./lib/vote"

export function VoteButtons({
  itemId,
  ownerHandle,
  score,
  viewerVote,
  onChange,
}: {
  itemId: string
  ownerHandle: string
  score: number | string
  viewerVote?: VoteValue | null
  onChange?: (s: VoteState) => void
}) {
  const { me } = useMe()
  const navigate = useNavigate()
  const loc = useLocation()
  const initial: VoteState = { score: num(score), viewerVote: viewerVote ?? null }
  const [state, dispatch] = useReducer(voteReducer, initial)

  useEffect(() => dispatch({ type: "sync", state: { score: num(score), viewerVote: viewerVote ?? null } }), [itemId, score, viewerVote])

  const own = me?.handle === ownerHandle
  const unverified = !!me && !me.emailVerified
  const disabledReason = own ? "You can't vote on your own item" : unverified ? "Verify your email to vote" : null

  const click = async (value: VoteValue) => {
    if (!me) {
      navigate(withReturn("/account/sign-in", loc.pathname + loc.search))
      return
    }
    if (disabledReason) return
    const before = state
    const target = nextVote(before.viewerVote, value)
    dispatch({ type: "click", value })
    try {
      if (target === null) await community.clearVote(itemId)
      else await community.vote(itemId, target)
      onChange?.(voteReducer(before, { type: "click", value }))
    } catch (e) {
      dispatch({ type: "rollback", to: before })
      toast.error(e instanceof ApiError ? e.detail ?? e.title : "Could not save your vote.")
    }
  }

  const btn = (value: VoteValue, label: string, Icon: typeof ThumbsUp) => {
    const active = state.viewerVote === value
    const b = (
      <button
        type="button"
        aria-label={label}
        aria-pressed={active}
        aria-disabled={disabledReason ? true : undefined}
        onClick={() => void click(value)}
        className={cn(
          "inline-flex size-10 cursor-pointer items-center justify-center rounded-full border border-border transition-colors hover:bg-muted aria-disabled:cursor-not-allowed aria-disabled:opacity-50",
          active && "border-primary bg-primary text-primary-foreground hover:bg-primary",
        )}
      >
        <Icon className="size-4" aria-hidden />
      </button>
    )
    return disabledReason ? <Tooltip content={disabledReason}>{b}</Tooltip> : <Tooltip content={me ? label : "Sign in to vote"}>{b}</Tooltip>
  }

  return (
    <div className="inline-flex items-center gap-2" role="group" aria-label="Vote">
      {btn(1, "Upvote", ThumbsUp)}
      <span className="min-w-8 text-center font-semibold tabular-nums" aria-live="polite">
        {state.score}
      </span>
      {btn(-1, "Downvote", ThumbsDown)}
    </div>
  )
}
