import React, { useCallback, useEffect, useRef, useState } from "react";
import ExportHtml from "services/exportHtml";
import { criticToHtml, changesCss } from "services/criticPreview";
import { getHtmlToc, getTOC } from "services/common";
import transport from "services/transport";
import { targetOf } from "services/xmlJump";
import './index.scss'

interface IPreview {
    markdown: string
    options: any
    // Live review (Review > Track changes): the review text of the baseline against the document. When there is one, the preview draws
    // it, with the additions and deletions in colour, instead of the plain document.
    changes?: string | null
    // The stamp that follows each change of this review ({>>@Name day<<}): what tells them from marks that were in the text.
    stamp?: string | null
    // The stamp to show for them instead ({>>@Name day<<}).
    shown?: string | null
    // The day each change was first seen, in the order of the text, and the start of its stamp ({>>@Name ).
    days?: string[] | null
    by?: string | null
    // The readable view of an XML document: a click on a list item or a table cell goes to where its value is written in the code (services/xmlJump).
    // `target` is what to look for, `ordinal` which of `peers` items of the same text in the view the click was on.
    onJump?: (target: { kind: string, text: string }, ordinal: number, peers: number) => void
    jumpTip?: string | null
}

// What a click on an element of the readable view of an XML document looks for.
const JUMP_ITEMS = 'li, td, h1, h2, h3, h4, h5, h6'

function describe(el: Element) {
    // the text of the element itself: the lists nested in it are other items
    let own = ''
    el.childNodes.forEach(node => {
        if (node.nodeType === Node.ELEMENT_NODE && /^(UL|OL)$/.test((node as Element).tagName)) return
        own += node.textContent ?? ''
    })
    const first = el.firstElementChild
    const bold = first && (first.tagName === 'STRONG' || first.tagName === 'B') ? first.textContent : ''
    return targetOf(el.tagName.toLowerCase(), own, bold)
}

// Read-only rendered preview for the split view (code on the left, this on the right). Rendered
// with the same ExportHtml the HTML export uses, so the preview matches what an export produces.
// It lives in a sandboxed iframe without allow-scripts: nothing in the document can run, and
// allow-same-origin only lets this component read the frame's scroll size for scroll sync.
// ExportHtml emits a light-themed page; the background is made transparent so the editor's own
// background shows through, and in dark mode the text and code/table/quote colors are overridden
// so it stays readable. Colors are chosen from the theme name rather than read from the editor
// page, whose theme stylesheet loads asynchronously after a switch. Re-renders on ThemeChanged.
const DARK_TEXT = '#e3e6ec'

const Preview: React.FC<IPreview> = ({ markdown, options, changes, stamp, shown, days, by, onJump, jumpTip }) => {
    const [html, setHtml] = useState('')
    const [themeVersion, setThemeVersion] = useState(0)
    const frameRef = useRef<HTMLIFrameElement>(null)

    useEffect(() => transport.addListener('ThemeChanged', () => setThemeVersion(v => v + 1)), [])

    const syncScroll = useCallback(() => {
        const frame = frameRef.current?.contentWindow
        if (!frame) return
        const max = document.documentElement.scrollHeight - window.innerHeight
        const ratio = max > 0 ? window.scrollY / max : 0
        const frameMax = frame.document.documentElement.scrollHeight - frame.innerHeight
        frame.scrollTo(0, ratio * Math.max(frameMax, 0))
    }, [])

    useEffect(() => {
        let cancelled = false
        const timer = setTimeout(async () => {
            const baseUrl = window.basePath ? `file:///${window.basePath.replaceAll('\\', '/')}/` : undefined
            const dark = window.actualTheme === 'dark'
            const themeCss = `
                html, body, .markdown-body { background: transparent !important; }
                .markdown-body { max-width: none; padding: 24px 32px;
                    font-size: ${options?.fontSize ?? 16}px; line-height: ${options?.lineHeight ?? 1.6}; }
                ${dark ? `
                .markdown-body { color: ${DARK_TEXT} !important; }
                .markdown-body pre, .markdown-body code, .markdown-body tt { background: rgba(127,127,127,.18) !important; }
                code[class*="language-"], pre[class*="language-"] { color: inherit !important; text-shadow: none !important; }
                .markdown-body table tr, .markdown-body table tr:nth-child(2n) { background: transparent !important; }
                .markdown-body table td, .markdown-body table th { border-color: rgba(127,127,127,.35) !important; }
                .markdown-body blockquote { color: inherit !important; opacity: .8; border-left-color: rgba(127,127,127,.4) !important; }
                .markdown-body hr { background: rgba(127,127,127,.3) !important; }
                .markdown-body h1, .markdown-body h2 { border-bottom-color: rgba(127,127,127,.3) !important; }` : ''}
                ${onJump ? '.markdown-body li, .markdown-body td { cursor: pointer; }' : ''}
                ${changes != null ? changesCss(dark) : ''}`
            const source = changes != null ? criticToHtml(changes, stamp ?? undefined, shown ?? undefined, days ?? undefined, by ?? undefined) : markdown
            const page = await new ExportHtml(source, { ...options, baseUrl }).generate({
                printOptimization: false,
                title: '',
                toc: getHtmlToc(getTOC(source ?? '').toc),
                extraCss: themeCss,
            })
            if (!cancelled) setHtml(page)
        }, 250)
        return () => {
            cancelled = true
            clearTimeout(timer)
        }
    }, [markdown, options, themeVersion, changes, stamp, shown, days, by, onJump])

    // The Review panel asks to show change number i: scrolled to the middle of the pane and lit for a moment.
    useEffect(() => {
        (window as any).__caretTrackPreview = (i: number) => {
            const doc = frameRef.current?.contentDocument
            const found = doc?.querySelectorAll(`[data-change="${i}"]`)
            if (!doc || !found || found.length === 0) return false
            found[0].scrollIntoView({ block: 'center' })
            found.forEach(el => {
                (el as HTMLElement).style.outline = '2px solid #e0a800'
                setTimeout(() => { (el as HTMLElement).style.outline = '' }, 1600)
            })
            return true
        }
        return () => { delete (window as any).__caretTrackPreview }
    }, [])

    useEffect(() => {
        addEventListener('scroll', syncScroll)
        return () => removeEventListener('scroll', syncScroll)
    }, [syncScroll])

    // The page of the preview is made again at each change: its clicks are listened to once it has loaded.
    const listenToClicks = useCallback(() => {
        syncScroll()
        const doc = frameRef.current?.contentDocument
        if (!doc || !onJump) return
        if (jumpTip) doc.querySelectorAll('li, td').forEach(el => { (el as HTMLElement).title = jumpTip })
        doc.addEventListener('click', (e) => {
            const target = e.target as Element | null
            // a selection being made (to copy a word) is not a click
            if (!target || target.closest('a') || (doc.getSelection()?.toString() ?? '') !== '') return
            const item = target.closest(JUMP_ITEMS)
            if (!item) return
            const looked = describe(item)
            if (!looked) return
            const same = Array.from(doc.querySelectorAll(JUMP_ITEMS)).filter(other => {
                const found = describe(other)
                return found && found.kind === looked.kind && found.text === looked.text
            })
            onJump(looked, same.indexOf(item) + 1, same.length)
        })
    }, [syncScroll, onJump, jumpTip])

    return (
        <iframe
            ref={frameRef}
            className="split-preview"
            title="Preview"
            sandbox="allow-same-origin"
            srcDoc={html}
            onLoad={listenToClicks}
        />
    )
}

export default Preview
