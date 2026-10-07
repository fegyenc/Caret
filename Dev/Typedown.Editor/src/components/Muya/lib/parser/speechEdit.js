// Speech marks: what to write into a paragraph to add a mark (docs/speech-marks-design.md, section 5.3).
//
// Pure functions on the text of one paragraph and offsets in it, so the rules can be tested alone; the page
// (services/speechPage.ts) finds the selection, asks here what to write, and types it so Undo works.
// Rules: a single mark goes where the caret is; a pair wraps the selection, or the word (emphasis) or the sentence
// (the others) at the caret; nothing is ever cut: a mark, a change of the review, a link, an image, code or math
// that the selection only partly covers is either taken whole (marks, changes) or makes the edit refused.

import { criticRules } from './rules'
import { matchMark, lookupMark, CODE_SPAN, isEscaped } from './speech'

const PLACEHOLDER = '\u0001'
const LINK = /^!?\[[^\]\n]*\]\([^)\n]*\)/
const MATH = /^\$[^$\n]+\$/
const HTML = /^<\/?[A-Za-z][^>\n]*>/
const CLOSER = /^\{\/([A-Za-z][A-Za-z0-9-]*)\}/

// What to write for a mark: `spec` is { name, value?, text?, after? } (after: another spec written right after a
// pair, like the wait after a joke). -> { role, open, close, after, scope } or null when the word is not a mark.
export const buildMark = (spec, defs) => {
  const one = s => {
    const value = s.value ? ` ${s.value}` : ''
    const text = s.text ? `: ${s.text}` : ''
    const open = `{${s.name}${value}${text}}`
    const mark = matchMark(open, defs)
    return mark && mark.raw === open ? { open, mark } : null
  }
  const first = one(spec)
  if (!first) return null
  const result = {
    role: first.mark.role,
    name: first.mark.name,
    open: first.open,
    close: first.mark.role === 'pair' ? `{/${first.mark.name}}` : '',
    after: '',
    scope: first.mark.name === 'emphasis' ? 'word' : 'sentence'
  }
  if (spec.after) {
    const next = one(spec.after)
    if (!next || next.mark.role !== 'point') return null
    result.after = next.open
  }
  return result
}

// The parts of a paragraph that must not be cut: [{ start, end, kind }] with kind 'speech' (a mark, an opener or a
// closer: `pair` says which words open or close), 'critic', 'code', 'link', 'math' or 'html'.
export const atomicSpans = (text, defs) => {
  const spans = []
  let i = 0
  while (i < text.length) {
    const c = text[i]
    const rest = text.substring(i)
    let m = null
    let kind = null
    let extra = {}
    if (c === '`' && !isEscaped(text, i)) {
      // an escaped backtick is text, not the start of code
      m = CODE_SPAN.exec(rest)
      kind = 'code'
    } else if (c === '{') {
      for (const rule of criticRules) {
        const to = rule.exec.exec(rest)
        if (to) {
          m = to
          kind = 'critic'
          break
        }
      }
      if (!m) {
        const mark = matchMark(rest, defs)
        if (mark) {
          m = [mark.raw]
          kind = 'speech'
          extra = { role: mark.role, name: mark.name }
        } else {
          const closer = CLOSER.exec(rest)
          const entry = closer && lookupMark(closer[1], defs)
          if (entry && entry.role === 'pair') {
            m = closer
            kind = 'speech'
            extra = { role: 'closer', name: entry.name }
          }
        }
      }
    } else if (c === '[' || c === '!') {
      m = LINK.exec(rest)
      kind = 'link'
    } else if (c === '$') {
      m = MATH.exec(rest)
      kind = 'math'
    } else if (c === '<') {
      m = HTML.exec(rest)
      kind = 'html'
    }
    if (m) {
      spans.push({ start: i, end: i + m[0].length, kind, ...extra })
      i += m[0].length
    } else {
      i++
    }
  }
  return spans
}

const inside = (spans, pos) => spans.find(s => s.start < pos && pos < s.end)

// Moves an edge of the edit out of a mark or a review change (taking it whole); null when it is inside
// something else (a link, code, math, a tag), which the edit does not touch.
const settle = (spans, pos, side) => {
  const span = inside(spans, pos)
  if (!span) return pos
  if (span.kind !== 'speech' && span.kind !== 'critic') return null
  return side === 'start' ? span.start : span.end
}

// Is every pair in `inner` opened and closed in it?
const balanced = (inner, defs) => {
  const stack = []
  for (const span of atomicSpans(inner, defs)) {
    if (span.kind !== 'speech') continue
    if (span.role === 'pair') {
      stack.push(span.name)
    } else if (span.role === 'closer') {
      if (stack.pop() !== span.name) return false
    }
  }
  return stack.length === 0
}

const WORD = /[\p{L}\p{N}_'’-]/u

// The text with its marks and other parts that must not be cut replaced by a character that is no word, no space
// and no end of a sentence, so the rules below can look for words and sentences without seeing into them.
const masked = (text, spans) => {
  let out = ''
  let at = 0
  for (const s of spans) {
    out += text.substring(at, s.start) + PLACEHOLDER.repeat(s.end - s.start)
    at = s.end
  }
  return out + text.substring(at)
}

const wordAround = (mask, pos) => {
  let start = pos
  let end = pos
  while (start > 0 && WORD.test(mask[start - 1])) start--
  while (end < mask.length && WORD.test(mask[end])) end++
  return start === end ? null : [start, end]
}

const sentenceAround = (mask, pos) => {
  const ranges = []
  const re = /[.!?…]+["')\]»”’]*(?=\s|$)/g
  let from = 0
  const push = to => {
    const raw = mask.substring(from, to)
    const lead = raw.length - raw.trimStart().length
    const body = raw.trim()
    if (body) ranges.push([from + lead, from + lead + body.length])
    from = to
  }
  let m
  while ((m = re.exec(mask))) push(m.index + m[0].length)
  push(mask.length)
  const inRange = ranges.find(([s, e]) => s <= pos && pos <= e)
  if (inRange) return inRange
  // in the white space between two sentences: the one before
  const before = ranges.filter(([, e]) => e < pos)
  return before.length ? before[before.length - 1] : ranges[0] || null
}

// What to write to add `mark` (from buildMark) to `text`, with the selection at [start, end] (start === end for a
// caret). -> { ok: true, start, end, replacement } (the text from start to end is replaced) or { ok: false, reason }
// where reason is 'nothing' (no word or sentence there, or an empty selection) or 'unsafe' (it would cut a link, code,
// math or a pair).
export const planEdit = (text, start, end, mark, defs) => {
  const spans = atomicSpans(text, defs)

  if (mark.role === 'point') {
    const pos = settle(spans, end, 'end')
    if (pos === null) return { ok: false, reason: 'unsafe' }
    const prev = text[pos - 1]
    const next = text[pos]
    const midWord = prev !== undefined && next !== undefined && WORD.test(prev) && WORD.test(next)
    const lead = !midWord && prev !== undefined && !/\s/.test(prev) && prev !== '}' ? ' ' : ''
    const trail = !midWord && next !== undefined && WORD.test(next) && !lead ? ' ' : ''
    return { ok: true, start: pos, end: pos, replacement: `${lead}${mark.open}${trail}` }
  }

  let from
  let to
  if (start === end) {
    const mask = masked(text, spans)
    const found = mark.scope === 'word' ? wordAround(mask, end) : sentenceAround(mask, end)
    if (!found) return { ok: false, reason: 'nothing' }
    ;[from, to] = found
  } else {
    from = settle(spans, start, 'start')
    to = settle(spans, end, 'end')
    if (from === null || to === null) return { ok: false, reason: 'unsafe' }
    // the spaces at the edges of a selection stay outside the mark
    const raw = text.substring(from, to)
    from += raw.length - raw.trimStart().length
    to -= raw.length - raw.trimEnd().length
    if (from >= to) return { ok: false, reason: 'nothing' }
  }
  const inner = text.substring(from, to)
  if (!balanced(inner, defs)) return { ok: false, reason: 'unsafe' }
  return { ok: true, start: from, end: to, replacement: `${mark.open}${inner}${mark.close}${mark.after}` }
}

// The marks of a document's own words need their `{define ...}` lines. `lines` are the definitions a mark to be written
// depends on; the ones the document does not have yet are added to a copy of `defs`, so the mark can be built and
// planned before its definition is in the text. Returns that copy and the lines that were missing.
export const withDefinitions = (defs, lines) => {
  const merged = new Map(defs || [])
  const missing = []
  for (const line of lines || []) {
    const mark = matchMark(line, undefined)
    if (!mark || mark.role !== 'define' || merged.has(mark.name)) continue
    merged.set(mark.name, mark.def)
    missing.push(line)
  }
  return { defs: merged, missing }
}

// Where a definition line goes in a document: at the end of the paragraph of definitions that starts it, or as a
// paragraph of its own before the first text (after a front matter block). -> { line, ch, insert }: write `insert` at
// that line and column.
export const planDefinition = (markdown, line) => {
  const lines = markdown.split('\n')
  let start = 0
  if (lines[0] === '---') {
    const end = lines.findIndex((l, i) => i > 0 && (l === '---' || l === '...'))
    if (end > 0) start = end + 1
  }
  let first = start
  while (first < lines.length && !lines[first].trim()) first++
  const isDefinition = l => /^\{define\s/i.test(l)
  if (first < lines.length && isDefinition(lines[first])) {
    let last = first
    while (last + 1 < lines.length && lines[last + 1].trim() && isDefinition(lines[last + 1])) last++
    // only when the paragraph holds nothing but definitions
    if (last + 1 >= lines.length || !lines[last + 1].trim()) return { line: last, ch: lines[last].length, insert: `\n${line}` }
  }
  if (first >= lines.length) return { line: lines.length - 1, ch: lines[lines.length - 1].length, insert: `${lines[lines.length - 1] ? '\n' : ''}${line}\n` }
  return { line: first, ch: 0, insert: `${line}\n\n` }
}

// A recipe is several marks written in one click: a template like `{pause 5s}{soft}{emphasis}{text}{/emphasis}{/soft}{wait}`
// in which `{text}` stands for the selected text (or the sentence at the caret). It may hold only marks (the words of
// Caret and those the document defines), every pair closed, no definitions and no settings, and `{text}` at most once.
// -> { before, after } or null.
const RECIPE_TEXT = '{text}'
export const splitRecipe = (template, defs) => {
  const at = template.indexOf(RECIPE_TEXT)
  if (at !== template.lastIndexOf(RECIPE_TEXT)) return null
  const before = at < 0 ? template : template.substring(0, at)
  const after = at < 0 ? '' : template.substring(at + RECIPE_TEXT.length)
  const marks = (before + after)
  const spans = atomicSpans(marks, defs).filter(s => s.kind === 'speech')
  // every brace belongs to a mark
  let rest = ''
  let from = 0
  for (const s of spans) {
    rest += marks.substring(from, s.start)
    from = s.end
  }
  rest += marks.substring(from)
  if (/[{}]/.test(rest)) return null
  const stack = []
  for (const s of spans) {
    if (s.role === 'define' || s.name === 'wpm' || s.name === 'budget') return null
    if (s.role === 'pair') stack.push(s.name)
    else if (s.role === 'closer' && stack.pop() !== s.name) return null
  }
  return stack.length === 0 && marks.trim() ? { before, after, hasText: at >= 0 } : null
}

// What to write for a recipe, in the shape buildMark gives: a pair around the text when the template has {text}, else a
// single piece of text at the caret.
export const buildRecipe = (template, defs) => {
  const parts = splitRecipe(template, defs)
  if (!parts) return null
  return parts.hasText
    ? { role: 'pair', name: 'recipe', open: parts.before, close: parts.after, after: '', scope: 'sentence' }
    : { role: 'point', name: 'recipe', open: template, close: '', after: '', scope: 'sentence' }
}

// What the Speech card asks for: a recipe (`spec.template`) or a single mark.
export const buildAny = (spec, defs) => (spec && spec.template ? buildRecipe(spec.template, defs) : buildMark(spec, defs))
