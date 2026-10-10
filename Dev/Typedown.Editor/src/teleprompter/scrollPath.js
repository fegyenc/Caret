// The teleprompter's scroll path: where the page must be at every second of the planned speech so that the text moves
// smoothly. The page is measured once (every line of the text, with the seconds the plan gives it) and the path is a
// line through the middle of each text line, drawn in time: between two lines the page glides, it never jumps, and
// nothing is measured while it moves. No DOM, no timers: plain functions, tested alone; Teleprompter.tsx measures and draws.
//
// A piece is { from, to, y }: the seconds a line (or a part of one) is read, and the height of its middle on the page.

// Pieces that lie on the same line of the page (their middles are closer than `tolerance`: a louder or softer word sits a
// little higher or lower than its neighbours) are one line, read from the first second to the last. -> [{ from, to, y }]
export const groupLines = (pieces, tolerance) => {
  const sorted = pieces.filter(p => isFinite(p.from) && isFinite(p.to) && isFinite(p.y)).sort((a, b) => a.from - b.from || a.y - b.y)
  const lines = []
  for (const p of sorted) {
    const last = lines[lines.length - 1]
    if (last && Math.abs(p.y - last.y) < tolerance) {
      last.to = Math.max(last.to, p.to)
    } else {
      lines.push({ from: p.from, to: p.to, y: p.y })
    }
  }
  return lines
}

// The key points of the path: the middle of each line in time, at the height of the middle of the line, with the start and the
// end of the talk held still. -> [{ t, y }] with `t` rising.
export const buildPath = (lines, total) => {
  if (!lines.length) return []
  const keys = lines.map(l => ({ t: (l.from + l.to) / 2, y: l.y }))
  const path = []
  const push = key => {
    const last = path[path.length - 1]
    if (last && key.t <= last.t) {
      // the same moment (a heading of no time): the later one wins, so the path stays a function of time
      last.y = key.y
      return
    }
    path.push(key)
  }
  if (keys[0].t > 0) push({ t: 0, y: keys[0].y })
  keys.forEach(push)
  const end = Math.max(total, path[path.length - 1].t)
  if (end > path[path.length - 1].t) push({ t: end, y: path[path.length - 1].y })
  return path
}

// The height of the reading line at second `t`: straight lines between the key points, held before the first and after the last.
export const yAt = (path, t) => {
  if (!path.length) return 0
  if (t <= path[0].t) return path[0].y
  const last = path[path.length - 1]
  if (t >= last.t) return last.y
  let lo = 0
  let hi = path.length - 1
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1
    if (path[mid].t <= t) lo = mid
    else hi = mid
  }
  const a = path[lo]
  const b = path[hi]
  return a.y + (b.y - a.y) * ((t - a.t) / (b.t - a.t))
}

// The inverse of yAt: the second at which the reading line is at height `y` (the first such second where the path stands still).
// The wheel moves the page by a distance; this says which moment of the talk that is. Clamped to the ends of the path.
export const tAtY = (path, y) => {
  if (!path.length) return 0
  if (y <= path[0].y) return path[0].t
  const last = path[path.length - 1]
  if (y >= last.y) return last.t
  let lo = 0
  let hi = path.length - 1
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1
    if (path[mid].y < y) lo = mid
    else hi = mid
  }
  const a = path[lo]
  const b = path[hi]
  if (b.y === a.y) return a.t
  return a.t + (b.t - a.t) * ((y - a.y) / (b.y - a.y))
}

// The line of the text nearest to height `y` (a click), if it is within `tolerance` of it: { from, to, y } or null.
// A click between paragraphs, far from any line, moves nothing.
export const nearestLine = (lines, y, tolerance) => {
  let best = null
  let distance = Infinity
  for (const line of lines) {
    const d = Math.abs(line.y - y)
    if (d < distance) { best = line; distance = d }
  }
  return best && distance <= tolerance ? best : null
}

// The path with its corners rubbed off: every point is the average of the path over `window` seconds around it. The distance
// between two paragraphs (their margin, a heading) is more than between two lines, and straight lines between the middles of
// lines would make the page hurry there; this spreads it over a few seconds, so the speed stays nearly the same all through the
// talk, and the reading line is never further from its line than half of that distance. The start and the end stay where they are.
export const smooth = (path, window = 6, step = 0.25) => {
  if (path.length < 3 || window <= 0) return path
  const first = path[0].t
  const last = path[path.length - 1].t
  const half = window / 2
  const out = []
  for (let t = first; t < last + step / 2; t += step) {
    const at = Math.min(t, last)
    let sum = 0
    let n = 0
    for (let u = at - half; u <= at + half + 1e-9; u += step) {
      sum += yAt(path, u)
      n++
    }
    out.push({ t: at, y: sum / n })
  }
  if (out[out.length - 1].t < last) out.push({ t: last, y: path[path.length - 1].y })
  // the first and the last point stay exactly where the path starts and ends
  out[0].y = path[0].y
  out[out.length - 1].y = path[path.length - 1].y
  return out
}

// One step of the follower that draws the page: it moves from `pos` toward `target` and eases out, whatever the speed of the
// screen (`dt` in seconds, `tau` the time it takes to cover most of the way). A tiny rest is closed at once, so the page stops.
export const follow = (pos, target, dt, tau = 0.18) => {
  const gap = target - pos
  if (Math.abs(gap) < 0.05) return target
  return pos + gap * (1 - Math.exp(-Math.max(dt, 0) / tau))
}
