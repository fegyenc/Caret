// New since the fork: callouts, the alerts of GitHub and the callouts of Obsidian. A quote whose first line is a marker such as [!NOTE] is drawn
// as a box of its kind; the file stays plain Markdown (GitHub draws it the same way, other hosts show a quote). The same rule is in
// Services/Export/Callouts.cs, for the Word export.

// The five kinds of GitHub, and the names of Obsidian that mean the same.
const KINDS = Object.freeze({
  note: 'note',
  info: 'note',
  todo: 'note',
  abstract: 'note',
  summary: 'note',
  tldr: 'note',
  example: 'note',
  quote: 'note',
  cite: 'note',
  tip: 'tip',
  hint: 'tip',
  success: 'tip',
  check: 'tip',
  done: 'tip',
  question: 'tip',
  help: 'tip',
  faq: 'tip',
  important: 'important',
  warning: 'warning',
  attention: 'warning',
  caution: 'caution',
  danger: 'caution',
  error: 'caution',
  failure: 'caution',
  fail: 'caution',
  missing: 'caution',
  bug: 'caution'
})

// The title when the marker has none: the words GitHub shows.
export const CALLOUT_TITLES = Object.freeze({
  note: 'Note',
  tip: 'Tip',
  important: 'Important',
  warning: 'Warning',
  caution: 'Caution'
})

const MARKER = /^[ \t]*\[!([A-Za-z]+)\]([+-]?)[ \t]*(.*)$/

// The callout that a first line opens: { kind, title } (title is '' when the marker has none), or null when the line is not a marker of a
// known kind. `[!note]-` and `[!note]+` (Obsidian's folding) are callouts too; the fold itself is not drawn.
export function parseCallout (line) {
  if (typeof line !== 'string') return null
  const match = MARKER.exec(line.split('\n')[0])
  if (!match) return null
  const kind = KINDS[match[1].toLowerCase()]
  if (!kind) return null
  return { kind, title: match[3].trim() }
}

// The HTML export and the preview (services/exportHtml.js): a quote that opens with [!NOTE], [!WARNING]... becomes a callout, as GitHub draws it: the box gets the classes
// `markdown-alert markdown-alert-<kind>` (GitHub's own, styled in assets/styles/exportStyle.css) and the marker line becomes its title. The marker is
// the first text of the first paragraph, up to the end of its line; a title written after the marker replaces the default one.
export function decorateCallouts (container) {
  for (const quote of container.querySelectorAll('blockquote')) {
    const first = quote.firstElementChild
    if (!first || first.tagName !== 'P') continue
    const text = first.firstChild
    if (!text || text.nodeType !== Node.TEXT_NODE) continue
    const end = text.data.indexOf('\n')
    const callout = parseCallout(end < 0 ? text.data : text.data.slice(0, end))
    if (!callout) continue
    quote.classList.add('markdown-alert', `markdown-alert-${callout.kind}`)
    const title = document.createElement('p')
    title.className = 'markdown-alert-title'
    title.textContent = callout.title || CALLOUT_TITLES[callout.kind]
    // the marker line goes; the rest of the paragraph stays (a marker alone on its line leaves an empty paragraph, which goes too)
    text.data = end < 0 ? '' : text.data.slice(end + 1)
    if (first.firstChild && first.firstChild.nodeName === 'BR') first.firstChild.remove()
    if (!first.textContent.trim() && !first.querySelector('img')) first.remove()
    quote.insertBefore(title, quote.firstChild)
  }
}

