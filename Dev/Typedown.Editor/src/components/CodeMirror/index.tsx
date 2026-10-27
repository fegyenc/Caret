import { MAX_SNIPPETS, snippetOf } from "services/searchSnippet";
import React, { useCallback, useEffect, useRef, useState } from "react";
import transport from "services/transport";
import { matchString } from 'services/common'
import { UnControlled as CodeMirror } from 'react-codemirror2';
import 'codemirror/lib/codemirror.css';
import 'codemirror/addon/fold/foldgutter.css';
import { getTOC } from "services/common";
import XmlStrip, { IXmlView } from "./XmlStrip";
require('codemirror/mode/markdown/markdown');
// an XML document (a file opened for editing) is shown as code: tags coloured, folded by element, closed and matched as they are typed
require('codemirror/mode/xml/xml');
require('codemirror/addon/fold/foldcode');
require('codemirror/addon/fold/foldgutter');
require('codemirror/addon/fold/xml-fold');
require('codemirror/addon/edit/closetag');
require('codemirror/addon/edit/matchtags');
require('codemirror/addon/edit/matchbrackets');

interface ICodeMirrorEditor {
    markdown: string
    cursor: any
    options: any
    searchOpen: number
    searchArg: { value: string, opt: any } | undefined
    // the document is an XML file: its own mode, folding and strip; the host's check of the text (MainWindow.Xml.cs) and whether the readable view is beside it
    xml?: boolean
    xmlView?: IXmlView | null
    splitPreview?: boolean
    scrollTopRef: React.MutableRefObject<number>
    onMarkdownChange: (markdown: string) => void
    onCursorChange: (cursor: any) => void
    onSearchArgChange: (arg: { value: string, opt: any } | undefined) => void
}

const STANDAR_Y = 320

const CodeMirrorEditor: React.FC<ICodeMirrorEditor> = (props) => {
    const [editor, setEditor] = useState<any>();
    const [marginTop, setMarginTop] = useState(0);
    // the height of the strip of an XML document, which the text is kept clear of
    const [stripHeight, setStripHeight] = useState(36);
    const matchsRef = useRef<any[]>([]);
    const matchIndexRef = useRef(0);
    const searchValueRef = useRef('');
    // "3 of 12" in the host's Find panel, as the visual editor reports it
    const reportSearchResult = useCallback(() => {
        const count = matchsRef.current.length;
        // the first matches with the text around them, for the inspector pane's Find tab
        const text: string = editor?.getValue() ?? '';
        const matches = matchsRef.current.slice(0, MAX_SNIPPETS)
            .map(({ index, match }) => snippetOf(text, index, index + match.length));
        transport.postMessageNoDiff('SearchResult', {
            count,
            index: count == 0 ? -1 : matchIndexRef.current,
            value: searchValueRef.current,
            matches
        });
    }, [editor]);
    const markdownRef = useRef('');
    const searchArgRef = useRef<any>();
    const cursorRef = useRef<any>();

    const relativeScroll = useCallback((delta: number) => {
        window.scrollBy(0, delta)
    }, [])

    const scrollToElement = useCallback((selector) => {
        if (editor == null) {
            return;
        }
        const anchor = document.querySelector(selector)
        if (anchor) {
            const { y } = anchor.getBoundingClientRect()
            relativeScroll(y - STANDAR_Y)
        }
    }, [editor, relativeScroll])

    const scrollToElementIfInvisible = useCallback((selector) => {
        const anchor = document.querySelector(selector)
        if (anchor) {
            const { y } = anchor.getBoundingClientRect()
            if (y < 0 || y > window.innerHeight) {
                scrollToElement(selector)
            }
        }
    }, [scrollToElement])

    const setMatchSelection = useCallback((match) => {
        if (match) {
            const head = editor.posFromIndex(match.index);
            const anchor = editor.posFromIndex(match.index + match.match.length);
            editor.setSelection(anchor, head);
        }
    }, [editor])

    const search = useCallback(({ value, opt }) => {
        const lastMatch = matchsRef.current[matchIndexRef.current];
        matchsRef.current.forEach(({ marker }) => marker.clear())
        searchValueRef.current = value ?? '';
        if ((value == null || value.length == 0)) {
            matchsRef.current = [];
            matchIndexRef.current = -1;
            if (lastMatch?.index >= 0) {
                const head = editor.posFromIndex(lastMatch.index);
                editor.setSelection(head, head);
            }
            reportSearchResult();
            return;
        }
        let startIndex = 0
        if (opt.selection) {
            const { anchor, head } = opt.selection;
            startIndex = Math.min(editor.indexFromPos(anchor), editor.indexFromPos(head))
        }
        const matchs = matchString(editor.getValue(), value, opt)
        for (matchIndexRef.current = 0;
            matchIndexRef.current < matchs.length && matchs[matchIndexRef.current].index < startIndex;
            matchIndexRef.current++
        );
        // the caret is behind the last match: the search goes on at the first one
        if (matchIndexRef.current >= matchs.length) matchIndexRef.current = 0
        setMatchSelection(matchs[matchIndexRef.current])
        matchsRef.current = matchs.map(({ index, match, subMatches }, i) => ({
            marker: editor.markText(
                editor.posFromIndex(index),
                editor.posFromIndex(index + match.length),
                { className: i == matchIndexRef.current ? 'ag-highlight' : 'ag-selection' }
            ),
            index,
            match,
            subMatches
        }))
        reportSearchResult();
    }, [editor, setMatchSelection, reportSearchResult])

    const find = useCallback(({ action, index }) => {
        if (action == 'goto') {
            // the match the Find tab of the inspector pane points at
            if (!(index >= 0) || index >= matchsRef.current.length) return;
            matchIndexRef.current = index;
        } else if (action == 'next') {
            matchIndexRef.current++;
        } else {
            matchIndexRef.current--;
        }
        if (matchIndexRef.current < 0) {
            matchIndexRef.current = matchsRef.current.length - 1;
        }
        if (matchIndexRef.current >= matchsRef.current.length) {
            matchIndexRef.current = 0;
        }
        setMatchSelection(matchsRef.current[matchIndexRef.current])
        matchsRef.current.forEach((item, i) => {
            item.marker.clear()
            item.marker = editor.markText(
                editor.posFromIndex(item.index),
                editor.posFromIndex(item.index + item.match.length),
                { className: i == matchIndexRef.current ? 'ag-highlight' : 'ag-selection' }
            )
        })
        reportSearchResult();
    }, [editor, setMatchSelection, reportSearchResult])

    const replaceOne = useCallback((match, replaceValue, offset = 0) => {
        const text = editor.getValue();
        const start = match.index + offset;
        const end = start + match.match.length;
        const value = text.substring(0, start) + replaceValue + text.substring(end);
        editor.setValue(value);
        return replaceValue.length - match.match.length;
    }, [editor])

    const replace = useCallback(({ value, opt }) => {
        if (opt.isSingle) {
            if (!matchsRef.current[matchIndexRef.current])
                return;
            const offset = replaceOne(matchsRef.current[matchIndexRef.current], value)
            matchsRef.current = matchsRef.current.map((e, i) => {
                if (i < matchIndexRef.current) return e;
                e.index += offset;
                return e;
            }).filter((e, i) => i != matchIndexRef.current)
            matchIndexRef.current--;
            find({ action: 'next' })
        } else {
            if (!matchsRef.current)
                return;
            // setValue puts the caret at the start of the text: it is kept where it was, moved by what the replacements
            // before it added or took away, so that the search after the replacement starts near it
            const caret = Math.min(editor.indexFromPos(editor.getCursor('anchor')), editor.indexFromPos(editor.getCursor('head')));
            let caretAfter = caret;
            let caretMapped = false;
            let offset = 0;
            for (const match of matchsRef.current) {
                const startAfter = match.index + offset
                const change = replaceOne(match, value, offset)
                offset += change
                if (caretMapped) continue
                if (match.index + match.match.length <= caret) {
                    caretAfter = caret + offset              // behind this replacement: moved by all the changes up to here
                } else if (match.index < caret) {
                    caretAfter = startAfter                   // inside it: at the start of what replaced it
                    caretMapped = true
                }
            }
            // the replacement can contain the term again: look at the new text and count what is there
            const at = editor.posFromIndex(Math.max(0, caretAfter));
            search({
                value: searchValueRef.current,
                opt: { ...opt, selection: { anchor: at, head: at } }
            });
        }
    }, [editor, find, replaceOne, search])

    useEffect(() => transport.addListener<{ text: string }>('Paste', ({ text }) => {
        editor?.replaceSelection(text)
    }), [editor]);

    useEffect(() => transport.addListener('DeleteSelection', () => {
        editor?.replaceSelection('')
    }), [editor]);

    // The inspector pane's Review list: selects and shows the `nth` place (0 for the first) where `text` stands.
    useEffect(() => transport.addListener<{ text: string, nth: number }>('ScrollToText', ({ text, nth }) => {
        if (!editor || !text) return;
        const value = editor.getValue();
        let at = -1;
        let from = 0;
        for (let i = 0; i <= nth; i++) {
            at = value.indexOf(text, from);
            if (at < 0) return;
            from = at + text.length;
        }
        const head = editor.posFromIndex(at);
        editor.setSelection(head, editor.posFromIndex(at + text.length));
        editor.scrollIntoView(head, 120);
    }), [editor]);

    useEffect(() => transport.addListener('Copy', () => {
        document.execCommand('copy')
    }), []);

    useEffect(() => transport.addListener('Cut', () => {
        document.execCommand('cut')
    }), []);

    useEffect(() => transport.addListener('SelectAll', () => {
        editor?.execCommand('selectAll')
    }), [editor]);

    useEffect(() => {
        searchArgRef.current = props.searchArg;
    }, [props.searchArg])

    useEffect(() => {
        markdownRef.current = ''
    }, [editor])

    useEffect(() => {
        if (markdownRef.current != props.markdown && editor) {
            markdownRef.current = props.markdown
            const { anchor, head } = cursorRef.current ?? {}
            editor.setValue(markdownRef.current)
            if (anchor && head)
                editor.setSelection(anchor, head, { scroll: true })
            window.scrollTo(window.scrollX, props.scrollTopRef.current)
            if (searchArgRef.current?.value != "" && searchArgRef.current?.opt?.selection) {
                const { value, opt } = searchArgRef.current
                const selection = opt.selection.head ? opt.selection : undefined
                search({ value, opt: { ...opt, selection } })
            }
        }
    }, [editor, props.markdown, props.scrollTopRef, search])

    useEffect(() => {
        if (props.searchArg && editor) {
            const { value, opt } = props.searchArg
            // The host sends the term and the options only (no selection): the first match shown is the one at or after
            // the caret. (Reading `opt.selection.head` of nothing threw and took the whole source pane down.)
            const selection = opt?.selection?.head
                ? opt.selection
                : { anchor: editor.getCursor('anchor'), head: editor.getCursor('head') }
            search({ value, opt: { ...opt, selection } })
        }
    }, [editor, props.searchArg, search])

    useEffect(() => {
        const { anchor, focus: head } = props.cursor ?? {}
        cursorRef.current = { anchor, head }
    }, [editor, props.cursor])

    const handleCodeMirrorState = useCallback((value: string) => {
        const wordCount = { character: value.length, word: value.split(' ').length }
        // the headings of a note: an XML document has none
        const toc: any[] = props.xml ? [] : getTOC(value).toc
        const state = { wordCount, toc, cur: toc[0] }
        transport.postMessage('StateChange', { state, codeMirror: true })
    }, [props.xml])

    const handleCodeMirrorContent = useCallback((cm: any, data: any, value: string) => {
        handleCodeMirrorState(value)
        markdownRef.current = value;
        props.onMarkdownChange(value)
    }, [handleCodeMirrorState, props])

    const handleCodeMirrorSelection = useCallback((cm: any, data: any) => {
        const { anchor, head } = data.ranges[0]
        cursorRef.current = { anchor, head }
        props.onCursorChange({ anchor, focus: head })
        const selectionText = cm.getSelection();
        transport.postMessage('CodeMirrorSelectionChange', { cursor: cursorRef.current, selectionText })
    }, [props])

    useEffect(() => {
        setMarginTop(currentMarginTop => {
            const newMarginTop = { 0: 0, 1: 105, 2: 145 }[props.searchOpen] ?? 0;   // the height of the Find panel (the source pane has no toolbar above it, so more than the visual editor): Find only, Find and Replace
            relativeScroll(newMarginTop - currentMarginTop)
            return newMarginTop;
        })
    }, [props.searchOpen, relativeScroll])

    useEffect(() => transport.addListener<{ open: number }>('SearchOpenChange', ({ open }) => {
        if (open == 0) {
            matchsRef.current.forEach(({ marker }) => marker.clear())
            matchsRef.current = []
            props.onSearchArgChange(undefined)
        }
    }), [editor, props, relativeScroll]);

    useEffect(() => transport.addListener<{ value: string, opt: any }>('Search', (arg) => {
        props.onSearchArgChange(arg)
        setTimeout(() => scrollToElementIfInvisible('.ag-highlight'), 0)
    }), [editor, props, scrollToElementIfInvisible, search]);

    useEffect(() => transport.addListener<{ action: string, index?: number }>('Find', (arg) => {
        find(arg)
        setTimeout(() => scrollToElementIfInvisible('.ag-highlight'), 0)
    }), [editor, find, scrollToElementIfInvisible]);

    useEffect(() => transport.addListener<{ value: string, opt: any }>('Replace', (arg) => {
        replace(arg)
    }), [replace]);

    useEffect(() => {
        editor?.focus()
    }, [editor])

    // Edit > Format XML: the formatted text replaces the whole text in one step, so that one Undo brings the old text back
    useEffect(() => transport.addListener<{ text: string }>('ReplaceAllText', ({ text }) => {
        if (!editor || typeof text !== 'string') return;
        const cursor = editor.getCursor();
        const last = editor.lastLine();
        editor.replaceRange(text, { line: 0, ch: 0 }, { line: last, ch: editor.getLine(last).length });
        const line = Math.min(cursor.line, editor.lastLine());
        editor.setCursor({ line, ch: Math.min(cursor.ch, editor.getLine(line).length) });
    }), [editor]);

    // The line the check found a problem at is tinted; the mark follows the line while the text above it changes.
    const errorLineRef = useRef<any>(null)
    useEffect(() => {
        if (!editor) return
        if (errorLineRef.current) {
            editor.removeLineClass(errorLineRef.current, 'background', 'xml-error-line')
            errorLineRef.current = null
        }
        const view = props.xmlView
        if (props.xml && view && view.ok === false && view.line > 0 && view.line <= editor.lineCount())
            errorLineRef.current = editor.addLineClass(view.line - 1, 'background', 'xml-error-line')
    }, [editor, props.xml, props.xmlView])

    // The problems the schema check found: a dot in the gutter of each line that has one, with the problems of the line in plain words as its title
    // (the dot stays with its line while the text above changes, until the next check).
    useEffect(() => {
        if (!editor || !props.xml) return
        editor.clearGutter('xml-issues')
        const byLine = new Map<number, string[]>()
        for (const issue of props.xmlView?.schema?.issues ?? []) {
            if (!(issue.line > 0) || issue.line > editor.lineCount()) continue
            byLine.set(issue.line - 1, [...(byLine.get(issue.line - 1) ?? []), issue.text])
        }
        byLine.forEach((texts, line) => {
            const dot = document.createElement('div')
            dot.className = 'xml-issue-dot'
            dot.textContent = '\u25CF'
            dot.title = texts.join('\n')
            editor.setGutterMarker(line, 'xml-issues', dot)
        })
    }, [editor, props.xml, props.xmlView])

    const jumpTo = useCallback((line: number, position: number, margin = 160) => {
        if (!editor || !(line > 0)) return
        const at = { line: Math.min(line - 1, editor.lastLine()), ch: Math.max(0, position - 1) }
        editor.focus()
        editor.setCursor(at)
        editor.scrollIntoView(at, margin)
    }, [editor])

    // A row of the Outline list (the sidebar's or the inspector's) of an XML document: the text goes to the line of that element.
    useEffect(() => transport.addListener<{ line: number }>('XmlGoto', ({ line }) => jumpTo(line, 1, Math.round(window.innerHeight / 3))), [jumpTo])

    useEffect(() => {
        const onscroll = () => {
            props.scrollTopRef.current = window.scrollY
        }
        addEventListener('scroll', onscroll);
        return () => removeEventListener('scroll', onscroll)
    }, [props.scrollTopRef])

    return (
        <div
            className="cm-s-one-dark"
            style={{
                boxSizing: 'border-box',
                paddingTop: marginTop,
                paddingLeft: 14,
                paddingRight: 14,
                paddingBottom: props.xml ? stripHeight + 8 : 0,   // the strip is at the bottom (more rows when it says a problem or wraps)
                fontSize: props.options?.fontSize,
                lineHeight: props.options?.lineHeight
            }}>
            <CodeMirror
                key={props.xml ? 'xml' : 'markdown'}
                ref={(ref: any) => ref && setEditor(ref.editor)}
                options={props.xml ? {
                    theme: 'one-dark',
                    mode: 'application/xml',
                    lineNumbers: true,
                    lineWrapping: true,
                    foldGutter: true,
                    gutters: ['xml-issues', 'CodeMirror-linenumbers', 'CodeMirror-foldgutter'],
                    matchTags: { bothTags: true },
                    autoCloseTags: true,
                    matchBrackets: true,
                    indentUnit: 2,
                    tabSize: 2,
                    indentWithTabs: false,
                    spellcheck: false   // tags and names are not prose
                } : {
                    theme: 'one-dark',
                    mode: 'markdown',
                    lineNumbers: true,
                    lineWrapping: true
                }}
                onChange={handleCodeMirrorContent}
                onSelection={handleCodeMirrorSelection}
            />
            {props.xml ? <XmlStrip view={props.xmlView} split={props.splitPreview} onJump={jumpTo} onHeight={setStripHeight} /> : null}
        </div>
    )
}

export default CodeMirrorEditor