import type { VoteValue } from "@cutwire/api-client"

export interface VoteState {
  score: number
  viewerVote: VoteValue | null
}

/** What the server should store after the user clicks `clicked`: clicking the active vote clears it. */
export function nextVote(current: VoteValue | null, clicked: VoteValue): VoteValue | null {
  return current === clicked ? null : clicked
}

/** Pure optimistic reducer. `score` is up minus down (unweighted, the server value replaces it on refresh). */
export function applyVote(state: VoteState, clicked: VoteValue): VoteState {
  const next = nextVote(state.viewerVote, clicked)
  return { viewerVote: next, score: state.score - (state.viewerVote ?? 0) + (next ?? 0) }
}

export type VoteAction = { type: "click"; value: VoteValue } | { type: "rollback"; to: VoteState } | { type: "sync"; state: VoteState }

export function voteReducer(state: VoteState, action: VoteAction): VoteState {
  switch (action.type) {
    case "click":
      return applyVote(state, action.value)
    case "rollback":
      return action.to
    case "sync":
      return action.state
  }
}
