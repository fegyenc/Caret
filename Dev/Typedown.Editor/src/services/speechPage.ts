// Speech marks, the page's half of the Speech card and the shortcuts (docs/speech-marks-design.md, section 5.3).
// The host asks for a mark with window.__caretSpeech.insert({ name, value?, text?, after? }); this finds what is
// selected, asks the rules (Muya/lib/parser/speechEdit.js, tested alone) what to write, and types it so Undo works
// as for any typing. Works in the editor and in the Code/Split source pane.
//
// The selection helpers are the ones of the review script (MainWindow.Review.cs): the editor draws its paragraphs
// again at times, so a place is the paragraph's id and offsets in its text (the marks are text in the page too, only
// hidden), and a selection is widened over the hidden marks of **bold** and the like.
import { buildMark, planEdit, planDefinition, withDefinitions } from 'components/Muya/lib/parser/speechEdit'
import { collectDefinitions, stripMarkdown } from 'components/Muya/lib/parser/speech'

type Status = 'ok' | 'nofocus' | 'nothing' | 'unsafe' | 'several' | 'unknown'

const state: { editor: any, on: boolean } = { editor: undefined, on: false }

export const setSpeechEditor = (editor: any) => { state.editor = editor }
export const setSpeechMode = (on: boolean) => { state.on = on }

const elementOf = (node: Node | null): Element | null => node && node.nodeType === 1 ? node as Element : node && node.parentElement
const editable = (node: Node | null) => elementOf(node)?.closest('[contenteditable="true"]') ?? null
const blockOf = (node: Node | null) => elementOf(node)?.closest('.ag-paragraph') as HTMLElement | null
const contentOf = (block: HTMLElement) => (block.querySelector('.ag-paragraph-content') as HTMLElement | null) ?? block
const sourcePane = () => (document.querySelector('.CodeMirror') as any)?.CodeMirror

const offsetOf = (content: Element, node: Node, offset: number) => {
    const r = document.createRange()
    r.selectNodeContents(content)
    r.setEnd(node, offset)
    return r.toString().length
}

const pointAt = (content: Element, offset: number) => {
    const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT)
    let seen = 0
    let last: Text | null = null
    while (walker.nextNode()) {
        last = walker.currentNode as Text
        if (offset <= seen + last.length) return { node: last as Node, offset: offset - seen }
        seen += last.length
    }
    return last ? { node: last as Node, offset: last.length } : { node: content as Node, offset: 0 }
}

// The marks of **bold**, *italic*, ~~del~~ are in the page as text hidden by a class (ag-hide): a selection that looks
// like "very important" ends before the hidden closing **. Wrapping that would leave the ** half inside the mark, so
// the selection is widened over the hidden marks at its edges (the ones that show, with the caret inside, are left).
const isMark = (n: Node | null): n is HTMLElement => !!n && n.nodeType === 1 && (n as HTMLElement).classList.contains('ag-remove')
const hiddenMark = (n: Node | null) => isMark(n) && n.classList.contains('ag-hide')
const formatted = (n: Node | null) => !!n && n.nodeType === 1 && (n as HTMLElement).classList.contains('ag-inline-rule')
const after = (node: Node, at: number): Node | null => {
    if (node.nodeType === 3) { if (at < (node as Text).length) return null } else if (at < node.childNodes.length) return node.childNodes[at]
    for (let n: Node | null = node; n; n = n.parentNode) if (n.nextSibling) return n.nextSibling
    return null
}
const before = (node: Node, at: number): Node | null => {
    if (node.nodeType === 3) { if (at > 0) return null } else if (at > 0) return node.childNodes[at - 1]
    for (let n: Node | null = node; n; n = n.parentNode) if (n.previousSibling) return n.previousSibling
    return null
}
const widen = (range: Range) => {
    let next: Node | null
    let prev: Node | null
    while ((next = after(range.endContainer, range.endOffset)) && hiddenMark(next) && formatted(next.previousSibling)) range.setEndAfter(next)
    while ((prev = before(range.startContainer, range.startOffset)) && hiddenMark(prev) && formatted(prev.nextSibling)) range.setStartBefore(prev)
}
const covers = (range: Range, node: Node) => {
    const size = node.nodeType === 3 ? (node as Text).length : node.childNodes.length
    return range.comparePoint(node, 0) === 0 && range.comparePoint(node, size) === 0
}
// Bold, italic and strikethrough can be inside a mark or be taken whole with their marks; anything else the selection
// touches (a link, an image, code, math) cannot be cut. The speech marks and the review marks are text in a line.
const cutsFormatting = (range: Range, block: HTMLElement) => {
    const list = block.querySelectorAll('.ag-inline-rule')
    for (let i = 0; i < list.length; i++) {
        const e = list[i] as HTMLElement
        if (e.classList.contains('ag-critic') || e.classList.contains('ag-speech') || !range.intersectsNode(e)) continue
        if (e.tagName !== 'STRONG' && e.tagName !== 'EM' && e.tagName !== 'DEL') continue
        if (e.contains(range.startContainer) && e.contains(range.endContainer)) continue
        const open = e.previousSibling
        const close = e.nextSibling
        if (!(isMark(open) && isMark(close) && covers(range, open) && covers(range, close))) return true
    }
    return false
}

// Zero-width spaces are the editor's own markers, not text: the rules see the text without them.
const ZWSP = String.fromCharCode(0x200b)
const ZW = new RegExp(ZWSP, 'g')
const toClean = (raw: string, at: number) => at - (raw.substring(0, at).match(ZW)?.length ?? 0)
const toRaw = (raw: string, at: number) => {
    let seen = 0
    for (let i = 0; i < raw.length; i++) {
        if (seen === at) return i
        if (raw[i] !== ZWSP) seen++
    }
    return raw.length
}

const insertInEditor = (spec: any): Status => {
    const sel = window.getSelection()
    if (!sel || sel.rangeCount === 0) return 'nofocus'
    const range = sel.getRangeAt(0).cloneRange()
    if (!editable(range.startContainer) || !editable(range.endContainer)) return 'nofocus'
    const first = blockOf(range.startContainer)
    const last = blockOf(range.endContainer)
    if (!first || !last) return 'nofocus'
    // a word of the user's own library is built with its definition, which the document may not have yet
    const { defs, missing } = withDefinitions(state.editor?.options?.speechDefs, spec.definitions)
    const mark = buildMark(spec, defs)
    if (!mark) return 'unknown'
    let status: Status
    if (first !== last) {
        status = mark.role === 'point' ? insertPointAtEnd(range, last, mark, defs) : 'several'
    } else {
        if (!range.collapsed) widen(range)
        status = mark.role === 'pair' && !range.collapsed && cutsFormatting(range, first) ? 'unsafe' : typeInto(first, range, mark, defs)
    }
    if (status === 'ok') addDefinitionsInEditor(missing)
    return status
}

// The `{define ...}` lines the document is missing for a mark just written: one paragraph of definitions at its top. The
// text is set again with the caret where it was (moved down by the lines that were added above it).
const addDefinitionsInEditor = (lines: string[]) => {
    const editor = state.editor
    if (!editor) return
    for (const line of lines) {
        const { markdown, cursor } = editor.getMarkdownAndCursor()
        const plan = planDefinition(markdown, line)
        const offset = markdown.split('\n').slice(0, plan.line).reduce((n: number, l: string) => n + l.length + 1, 0) + plan.ch
        const added = (plan.insert.match(/\n/g) ?? []).length
        // a caret on the line the text is written into, or below it, moves down with the text that is pushed down
        const moved = (p: { line: number, ch: number }) =>
            p.line > plan.line || (p.line === plan.line && p.ch >= plan.ch && plan.insert.endsWith('\n')) ? { ...p, line: p.line + added } : p
        const next = markdown.substring(0, offset) + plan.insert + markdown.substring(offset)
        editor.setMarkdown(next, cursor && cursor.anchor && cursor.focus ? { anchor: moved(cursor.anchor), focus: moved(cursor.focus) } : undefined)
    }
}

// A point mark after a selection over several paragraphs: at its end.
const insertPointAtEnd = (range: Range, last: HTMLElement, mark: any, defs: any): Status => {
    const tail = range.cloneRange()
    tail.collapse(false)
    return typeInto(last, tail, mark, defs)
}

const typeInto = (block: HTMLElement, range: Range, mark: any, defs: any): Status => {
    const content = contentOf(block)
    const raw = content.textContent ?? ''
    const start = offsetOf(content, range.startContainer, range.startOffset)
    const end = offsetOf(content, range.endContainer, range.endOffset)
    const text = raw.replace(ZW, '')
    const plan = planEdit(text, toClean(raw, start), toClean(raw, end), mark, defs)
    if (!plan.ok) return plan.reason as Status
    const from = pointAt(content, toRaw(raw, plan.start))
    const to = pointAt(content, toRaw(raw, plan.end))
    const target = document.createRange()
    target.setStart(from.node, from.offset)
    target.setEnd(to.node, to.offset)
    const root = editable(target.startContainer) as HTMLElement | null
    if (!root) return 'nofocus'
    root.focus()
    const sel = window.getSelection()!
    sel.removeAllRanges()
    sel.addRange(target)
    return document.execCommand('insertText', false, plan.replacement) ? 'ok' : 'nofocus'
}

const insertInCode = (cm: any, spec: any): Status => {
    const { defs, missing } = withDefinitions(collectDefinitions(cm.getValue().split('\n')).defs, spec.definitions)
    const mark = buildMark(spec, defs)
    if (!mark) return 'unknown'
    const from = cm.getCursor('from')
    const to = cm.getCursor('to')
    if (from.line !== to.line && mark.role !== 'point') return 'several'
    // a mark after a selection over several lines goes at its end; otherwise over the selection on its line
    const line = from.line !== to.line ? to.line : from.line
    const start = from.line !== to.line ? to.ch : from.ch
    const plan = planEdit(cm.getLine(line), start, to.ch, mark, defs)
    if (!plan.ok) return plan.reason as Status
    cm.replaceRange(plan.replacement, { line, ch: plan.start }, { line, ch: plan.end })
    // the definitions the text is missing go to the top; the caret follows the text it was in
    for (const definition of missing) {
        const at = planDefinition(cm.getValue(), definition)
        cm.replaceRange(at.insert, { line: at.line, ch: at.ch })
    }
    cm.focus()
    return 'ok'
}

const insert = (spec: any): Status => {
    try {
        const cm = sourcePane()
        return cm ? insertInCode(cm, spec) : insertInEditor(spec)
    } catch (err) {
        console.log(err)
        return 'nofocus'
    }
}

// Direct keys, by physical key so they work on any layout (never Ctrl+Alt: that is AltGr on many keyboards). Only in
// Speech mode; the page tells the host nothing: it is all done here.
const SHORTCUTS: Record<string, any> = {
    Period: { name: 'pause' },
    Comma: { name: 'beat' },
    KeyE: { name: 'emphasis' }
}

const shortcut = (code: string): boolean => {
    if (!state.on || !SHORTCUTS[code]) return false
    insert(SHORTCUTS[code])
    return true
}

;(window as any).__caretSpeech = {
    insert: (json: string) => insert(JSON.parse(json)),
    shortcut,
    // the document without its speech marks (Edit > Remove all speech marks)
    strip: (markdown: string) => stripMarkdown(markdown)
}
