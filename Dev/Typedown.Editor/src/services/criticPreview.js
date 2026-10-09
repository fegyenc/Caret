// The split preview's "changes view" (docs/live-review-design.md, step L1): the text of a review as ReviewDiff writes it, in CriticMarkup,
// turned into tags the preview can draw in colour: additions, deletions, highlights, the small stamp that follows a change and the notes.
// Code is left alone (a fenced block and a code span are never read for marks), as everywhere else.

const FENCE = /^ {0,3}(`{3,}|~{3,})/

// `{>>@Name 2026-10-09<<}`: who and when, drawn small. Any other `{>>note<<}` is a note.
const STAMP = /\{>>@([^<]*?) (\d{4}-\d{2}-\d{2})<<\}/g
const NOTE = /\{>>([\s\S]*?)<<\}/g

const marks = (line) => line
    .replace(/\{~~([\s\S]*?)~>([\s\S]*?)~~\}/g, '<del class="caret-del">$1</del><ins class="caret-add">$2</ins>')
    .replace(/\{\+\+([\s\S]*?)\+\+\}/g, '<ins class="caret-add">$1</ins>')
    .replace(/\{--([\s\S]*?)--\}/g, '<del class="caret-del">$1</del>')
    .replace(/\{==([\s\S]*?)==\}/g, '<mark class="caret-hl">$1</mark>')
    .replace(STAMP, '<span class="caret-stamp">$1 · $2</span>')
    .replace(NOTE, '<span class="caret-note">$1</span>')

// A line outside fenced code, without the marks inside its code spans.
const lineToHtml = (line) => {
    const spans = []
    const hidden = line.replace(/(`+)(?!`)([\s\S]*?[^`])\1(?!`)/g, (m) => { spans.push(m); return `\uE000${spans.length - 1}\uE001` })
    return marks(hidden).replace(/\uE000(\d+)\uE001/g, (m, i) => spans[Number(i)])
}

export const criticToHtml = (text) => {
    let fence = null
    return String(text ?? '').split('\n').map((line) => {
        const m = FENCE.exec(line)
        if (fence) {
            if (m && m[1][0] === fence[0] && m[1].length >= fence.length) fence = null
            return line
        }
        if (m) { fence = m[1]; return line }
        return lineToHtml(line)
    }).join('\n')
}

// The colours of the changes in the preview, light and dark (the preview's own page does not know the editor's theme sheet).
export const changesCss = (dark) => `
    ins.caret-add { text-decoration: none; background: ${dark ? 'rgba(46,160,67,.28)' : '#d4f4dd'}; color: ${dark ? '#8ddb8c' : '#14532d'}; border-radius: 2px; padding: 0 1px; }
    del.caret-del { background: ${dark ? 'rgba(248,81,73,.25)' : '#fde0e0'}; color: ${dark ? '#ffa198' : '#7f1d1d'}; text-decoration: line-through; border-radius: 2px; padding: 0 1px; }
    mark.caret-hl { background: ${dark ? 'rgba(210,153,34,.35)' : '#fff3b0'}; color: inherit; border-radius: 2px; }
    span.caret-stamp { font-size: .7em; opacity: .55; margin-left: .3em; white-space: nowrap; }
    span.caret-note { background: ${dark ? 'rgba(210,153,34,.25)' : '#fff8d6'}; font-size: .85em; border-radius: 2px; padding: 0 3px; }`