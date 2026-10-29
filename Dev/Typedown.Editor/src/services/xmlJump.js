// New since the fork: from a value of the readable view of an XML document to the place in the code where it is written. A click on a list item or a
// table cell of the readable view gives a target (the value, or the name of an element that holds others) and its place among the items of the same
// text in the view; this finds that text in the XML source. Rules only, no AI.
//
// What is searched for, in this order:
//  - a value: the whole content of an element (>value<), of an attribute (="value") or of a CDATA section; if the view prints only a part of a value
//    (a date without its time), the value as a word of its own (not inside a longer word or number, and not a tag name);
//  - a name (an element that holds other elements, shown with its name): the start tag <name> or <prefix:name>.
// The characters of the view are read as the source writes them: & < > " ' as entities too, a run of white space as any white space.
//
// When the text is written several times, the item that is the k-th of P items of that text in the view goes to the k-th of S places in the source;
// when the view lists more items than the source has places (a summary that repeats what the list of all the fields says), the places are used again
// in turn, so that a list of all the fields that comes after the summary lands in the right place.

const MAX_TEXT = 300
const WORD ='[\\p{L}\\p{N}]'

function regexOf(text) {
    let out = ''
    let space = false
    for (const c of text.trim()) {
        if (/\s/.test(c)) {
            space = true
            continue
        }
        if (space) out += '\\s+'
        space = false
        if (c === '&') out += '(?:&amp;|&#38;|&#x26;)'
        else if (c === '<') out += '(?:&lt;|&#60;|&#x3C;)'
        else if (c === '>') out += '(?:&gt;|>)'
        else if (c === '"') out += '(?:&quot;|")'
        else if (c === "'") out += "(?:&apos;|')"
        else out += c.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    }
    return out
}

function spansOf(source, pattern) {
    const found = []
    const regex = new RegExp(pattern, 'gu')
    let m
    while ((m = regex.exec(source)) !== null) {
        if (m[0].length === 0) {
            regex.lastIndex++
            continue
        }
        found.push({ from: m.index, to: m.index + m[0].length })
    }
    return found
}

// The places in the source of a target {kind: 'value' | 'name', text}: [{from, to}] as offsets, in the order of the text.
export function findSpans(source, target) {
    const text = (target?.text ?? '').trim()
    if (!source || !text) return []
    const body = regexOf(text)
    if (target.kind === 'name') return spansOf(source, `(?<=<(?:[\\w.\\-]+:)?)${body}(?=[\\s/>])`)
    const whole = spansOf(source, `(?<=>\\s*)${body}(?=\\s*<)|(?<==\\s*["'])${body}(?=["'])|(?<=<!\\[CDATA\\[)${body}(?=\\]\\]>)`)
    if (whole.length > 0) return whole
    // (after the part: not a letter or a digit, except the T of a date followed by its time: 2026-10-20T10:30)
    return spansOf(source, `(?<!${WORD}|[<\\/])${body}(?![\\p{N}])(?!(?!T\\d)[\\p{L}])`)
}

// Which of the `found` places the item goes to: the item is the k-th (1 on) of `peers` items of the same text in the view; -1 when there is none.
export function pickPlace(k, peers, found) {
    if (found <= 0) return -1
    const n = Math.max(1, k)
    return peers <= found ? Math.min(n, found) - 1 : (n - 1) % found
}

// The place of an item in the source, or null.
export function resolveJump(source, target, k, peers) {
    const spans = findSpans(source, target)
    const at = pickPlace(k, peers, spans.length)
    if (at >= 0) return spans[at]
    // The summaries of the formats print a code with its name ("3 — Destinatário"): the source has the code only. It is looked for when it is
    // written once (a code such as 0 or 1 is written all over the file, and going to the first one would be going to the wrong place).
    const code = target?.kind === 'value' ? /^(.+?)\s+—\s+/.exec(target.text) : null
    if (!code) return null
    const codes = findSpans(source, { kind: 'value', text: code[1] })
    return codes.length === 1 ? codes[0] : null
}

// The target of an element of the readable view (a list item, a table cell or a heading), given what the page can read of it: its tag name, its own
// text (the text of a nested list left out) and the text of the bold word that opens a list item ("sku" in "sku: X-1"). Null when there is nothing to look for.
export function targetOf(tag, ownText, boldText) {
    const text = (ownText ?? '').replace(/\s+/g, ' ').trim()
    // (an item that holds a table has the text of the whole table as its own: that is not a value to look for)
    if (!text || text.length > MAX_TEXT) return null
    if (tag === 'td') return { kind: 'value', text }
    if (/^h[1-6]$/.test(tag)) return { kind: 'name', text: text.split(' ')[0] }
    const bold = (boldText ?? '').replace(/\s+/g, ' ').trim()
    if (bold && text.startsWith(bold)) {
        const rest = text.slice(bold.length)
        // "name: value" with the colon after the bold word (the generic view) or inside it (the summaries of the formats)
        const value = bold.endsWith(':') ? /^\s*(.+)$/.exec(rest) : /^\s*:\s*(.+)$/.exec(rest)
        if (value) return { kind: 'value', text: value[1].trim() }
        // a name with something after it ("order (id=A1)"): an element that holds others
        return { kind: 'name', text: bold }
    }
    return { kind: 'value', text }
}
