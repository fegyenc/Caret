import { groupLines, buildPath, yAt, follow, smooth, tAtY, nearestLine } from './scrollPath'

describe('teleprompter scroll path', () => {
  // three lines of a paragraph, 3 seconds each, 66 px apart; then a paragraph further down (a gap of 20 px more)
  const pieces = [
    { from: 0, to: 3, y: 33 },
    { from: 3, to: 6, y: 99 },
    { from: 6, to: 9, y: 165 },
    { from: 9, to: 12, y: 251 }
  ]

  test('pieces on the same line are one line, read from the first to the last second', () => {
    const lines = groupLines([
      { from: 0, to: 1, y: 33 },
      { from: 1, to: 2, y: 37 }, // a louder word sits a little lower
      { from: 2, to: 3, y: 30 },
      { from: 3, to: 6, y: 99 }
    ], 20)
    expect(lines).toEqual([{ from: 0, to: 3, y: 33 }, { from: 3, to: 6, y: 99 }])
  })

  test('rubbish pieces are left out', () => {
    expect(groupLines([{ from: NaN, to: 1, y: 3 }, { from: 0, to: 1, y: 3 }], 10)).toHaveLength(1)
  })

  test('the path is a function of time and holds still before the start and after the end', () => {
    const path = buildPath(groupLines(pieces, 20), 12)
    expect(path[0]).toEqual({ t: 0, y: 33 })
    for (let i = 1; i < path.length; i++) expect(path[i].t).toBeGreaterThan(path[i - 1].t)
    expect(yAt(path, -5)).toBe(33)
    expect(yAt(path, 0)).toBe(33)
    expect(yAt(path, 12)).toBe(251)
    expect(yAt(path, 99)).toBe(251)
  })

  test('the reading line glides from one line to the next: no jump', () => {
    const path = buildPath(groupLines(pieces, 20), 12)
    expect(yAt(path, 1.5)).toBeCloseTo(33, 6)
    expect(yAt(path, 4.5)).toBeCloseTo(99, 6)
    expect(yAt(path, 3)).toBeCloseTo(66, 6) // halfway between two lines at their boundary
    // the biggest step in 1/60 second anywhere is small, even across the gap between paragraphs
    let biggest = 0
    let previous = yAt(path, 0)
    for (let t = 1 / 60; t <= 12; t += 1 / 60) {
      const y = yAt(path, t)
      biggest = Math.max(biggest, Math.abs(y - previous))
      previous = y
    }
    expect(biggest).toBeLessThan(66 / 3 / 60 * 1.6 + 0.01)
  })

  test('it never goes back while the time goes on', () => {
    const path = buildPath(groupLines(pieces, 20), 12)
    let previous = -1
    for (let t = 0; t <= 12; t += 0.05) {
      const y = yAt(path, t)
      expect(y).toBeGreaterThanOrEqual(previous - 1e-9)
      previous = y
    }
  })

  test('two moments that are the same second leave one point', () => {
    const path = buildPath([{ from: 0, to: 0, y: 10 }, { from: 0, to: 0, y: 40 }], 5)
    expect(path.filter(k => k.t === 0)).toHaveLength(1)
    expect(yAt(path, 0)).toBe(40)
  })

  test('an empty page has an empty path', () => {
    expect(buildPath([], 10)).toEqual([])
    expect(yAt([], 3)).toBe(0)
  })

  test('smoothing spreads the gap between paragraphs, so the speed stays nearly the same', () => {
    // lines every 66 px, 4.5 s each, and 43 px more between the paragraphs (a margin)
    const lines = []
    let y = 33
    for (let p = 0; p < 6; p++) {
      for (let l = 0; l < 4; l++) { lines.push({ from: (p * 4 + l) * 4.5, to: (p * 4 + l + 1) * 4.5, y }); y += 66 }
      y += 43
    }
    const total = 24 * 4.5
    const raw = buildPath(lines, total)
    const soft = smooth(raw, 8)
    const peak = path => { let m = 0; for (let t = 5; t < total - 5; t += 0.25) m = Math.max(m, (yAt(path, t + 0.25) - yAt(path, t)) / 0.25); return m }
    expect(peak(soft)).toBeLessThan(peak(raw) * 0.88)
    // it stays near the true line (within half of the extra distance and a bit) and never goes back
    for (let t = 0; t <= total; t += 0.5) expect(Math.abs(yAt(soft, t) - yAt(raw, t))).toBeLessThan(45)
    let previous = -1
    for (let t = 0; t <= total; t += 0.25) { const v = yAt(soft, t); expect(v).toBeGreaterThanOrEqual(previous - 1e-9); previous = v }
    // the start and the end are where they were
    expect(yAt(soft, 0)).toBe(yAt(raw, 0))
    expect(yAt(soft, total)).toBe(yAt(raw, total))
  })

  test('a path too short to smooth is left as it is', () => {
    const path = [{ t: 0, y: 1 }, { t: 5, y: 9 }]
    expect(smooth(path, 6)).toBe(path)
  })

  test('the second at a height is the inverse of the height at a second', () => {
    const path = buildPath(groupLines(pieces, 20), 12)
    for (const t of [2, 3.7, 6, 8.2, 10]) expect(tAtY(path, yAt(path, t))).toBeCloseTo(t, 6)
    expect(tAtY(path, -100)).toBe(0)
    expect(tAtY(path, 9999)).toBe(12)
    expect(tAtY([], 5)).toBe(0)
  })

  test('a flat stretch of the path gives its first second', () => {
    expect(tAtY([{ t: 0, y: 10 }, { t: 4, y: 10 }, { t: 8, y: 50 }], 10)).toBe(0)
  })

  test('a click finds the nearest line, and nothing when it is far from every line', () => {
    const lines = [{ from: 0, to: 3, y: 33 }, { from: 3, to: 6, y: 99 }, { from: 9, to: 12, y: 251 }]
    expect(nearestLine(lines, 90, 40)).toEqual({ from: 3, to: 6, y: 99 })
    expect(nearestLine(lines, 150, 40)).toBeNull()
    expect(nearestLine([], 5, 40)).toBeNull()
  })

  test('the follower eases to the target, the same whatever the speed of the screen, and rests', () => {
    const run = fps => {
      let pos = 0
      for (let i = 0; i < fps * 0.5; i++) pos = follow(pos, 100, 1 / fps)
      return pos
    }
    expect(run(60)).toBeCloseTo(run(144), 3)
    expect(run(60)).toBeGreaterThan(90)
    expect(follow(99.99, 100, 1 / 60)).toBe(100)
  })
})
