import CodeMirror from "components/CodeMirror";
import MuyaEditor from "components/Muya";
import Preview from "components/Preview";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { remote } from "services/remote";
import transport from "services/transport";
import './index.scss'
import ExportHtml from "services/exportHtml";
import { htmlToMarkdown } from "services/importHtml";
import { DEFAULT_TURNDOWN_CONFIG } from "components/Muya/lib/config";
import { getHtmlToc, getTOC } from "services/common";
import { setSpeechMode, setSpeechBaseline } from "services/speechPage";
import { setSpeechRing } from "services/speechRing";
import { collectDocumentDefinitions } from "components/Muya/lib/parser/speech";
import { computeTiming, formatClock } from "components/Muya/lib/parser/speechTiming";

const Editor: React.FC = () => {
    const [markdown, setMarkdown] = useState<string>();
    const markdownRef = useRef<string>();
    // The text as of the latest edit, set as the edit happens: `markdown` only reaches the host
    // (MarkdownChange) after React's next render. Flush reads it, see below.
    const latestRef = useRef<string>();
    const onMarkdownChange = useCallback((text: string) => {
        latestRef.current = text
        setMarkdown(text)
    }, [])
    const [cursor, setCursor] = useState<any>();
    const [options, setOptions] = useState<any>();
    const optionsRef = useRef<any>();
    const [searchOpen, setSearchOpen] = useState(0);
    const [searchArg, setSearchArg] = useState<{ value: string, opt: any }>();
    const muyaScrollTopRef = useRef(0);
    const codeMirrorScrollRef = useRef(0);

    const OnFileLoaded = useCallback(() => setTimeout(() => transport.postMessage('FileLoaded', { text: markdownRef.current }), 100), [])

    useEffect(() => {
        remote.getSettings().then(({ markdown, basePath, ...opt }: any) => {
            window.basePath = basePath
            setOptions(opt)
            setMarkdown(markdown)
            markdownRef.current = markdown
            latestRef.current = markdown
            OnFileLoaded();
        })
    }, [OnFileLoaded]);

    useEffect(() => {
        optionsRef.current = options
    }, [options])

    // Speech mode: the shortcuts work only in it, and the Speech card lists the words the document defines.
    const speechMode = !!options?.speechMode
    const definitionsRef = useRef<string>()
    useEffect(() => setSpeechMode(speechMode), [speechMode])
    // what the Speech ring shows (the same list as the Speech card, sent by the host with the settings)
    useEffect(() => setSpeechRing(options?.speechRing), [options?.speechRing])
    useEffect(() => {
        if (!speechMode) { definitionsRef.current = undefined; return }
        if (markdown === undefined) return
        const { defs } = collectDocumentDefinitions(markdown)
        const list = JSON.stringify(Array.from(defs.values()).map(d => ({ name: d.name, kind: d.kind, meaning: d.meaning })))
        if (list === definitionsRef.current) return
        definitionsRef.current = list
        transport.postMessageNoDiff('SpeechDefinitions', { defs: JSON.parse(list) })
    }, [speechMode, markdown])

    // How long the talk takes (docs/speech-marks-design.md, section 4): worked out here, where the marks are read, a moment
    // after the text stops changing, and sent to the Speech card when it is not what was sent last.
    const timingRef = useRef<string>()
    const speechWpm = options?.speechWpm
    const headingsSpoken = !!options?.speechHeadingsSpoken
    useEffect(() => {
        if (!speechMode || markdown === undefined) { timingRef.current = undefined; return }
        const handle = setTimeout(() => {
            const t = computeTiming(markdown, { wpm: speechWpm, headingsSpoken })
            setSpeechBaseline(t.wpm)
            const clock = (s: number) => formatClock(Math.abs(s))
            const payload = JSON.stringify({
                words: t.words,
                clock: formatClock(t.seconds),
                wpm: t.wpm,
                wpmInDocument: t.wpmInDocument,
                budget: t.budget,
                budgetClock: t.budget > 0 ? formatClock(t.budget) : '',
                light: t.light,
                over: t.over,
                overClock: clock(t.over),
                sections: t.sections.slice(0, 200).map(s => ({
                    title: s.title, level: s.level, words: s.words, clock: formatClock(s.seconds),
                    budget: s.budget, budgetClock: s.budget > 0 ? formatClock(s.budget) : '', light: s.light, over: s.over, overClock: clock(s.over)
                }))
            })
            if (payload === timingRef.current) return
            timingRef.current = payload
            transport.postMessageNoDiff('SpeechTiming', JSON.parse(payload))
        }, 300)
        return () => clearTimeout(handle)
    }, [speechMode, markdown, speechWpm, headingsSpoken])

    useEffect(() => {
        if (markdown != undefined && markdownRef.current != markdown) {
            transport.postMessage('MarkdownChange', { text: markdown });
            markdownRef.current = markdown
        }
    }, [markdown])

    useEffect(() => {
        transport.postMessage('CursorChange', { cursor })
    }, [cursor])

    useEffect(() => transport.addListener<IExportArgs>('Export', async ({ type, context, basePath, title, options }) => {
        const generateOption = { printOptimization: false, title, toc: getHtmlToc(getTOC(markdownRef.current ?? '').toc), ...options }
        const baseUrl = basePath ? `file:///${basePath.replaceAll('\\', '/')}/` : undefined
        const html = await new ExportHtml(markdownRef.current, { ...optionsRef.current, baseUrl }).generate(generateOption)
        if (type == 'print') {
            remote.printHTML({ html, context })
        } else {
            remote.exportCallback({ html, context })
        }
    }), []);

    useEffect(() => transport.addListener<{ type: string, text: string }>('ImportFile', ({ text }) => {
        onMarkdownChange(htmlToMarkdown(text, [], DEFAULT_TURNDOWN_CONFIG))
    }), [options, onMarkdownChange]);

    useEffect(() => transport.addListener<{ text: string, basePath: string }>('LoadFile', ({ text, basePath }) => {
        window.basePath = basePath
        setCursor(undefined)
        setMarkdown(text)
        markdownRef.current = text
        latestRef.current = text
        OnFileLoaded();
    }), [OnFileLoaded]);

    useEffect(() => transport.addListener<{ text: string, cursor: string, basePath: string }>('SetMarkdown', ({ text, cursor, basePath }) => {
        window.basePath = basePath
        setCursor(cursor)
        setTimeout(() => setMarkdown(text))
        markdownRef.current = text
        latestRef.current = text
    }), []);

    // Before the host switches tabs: answers with the text as of the latest edit. Messages arrive in
    // order, so every MarkdownChange sent before this answer has reached the host by then too.
    useEffect(() => transport.addListener<{ id: string }>('Flush', ({ id }) => {
        transport.postMessageNoDiff('Flushed', { id, text: latestRef.current })
    }), []);

    useEffect(() => transport.addListener<Record<string, unknown>>('SettingsChanged', (newOptions) => {
        for (const name in newOptions) {
            const value = newOptions[name];
            if (name.startsWith('search'))
                setSearchArg(old => old ? { ...old, opt: { ...old.opt, [name]: value } } : old)
        }
        setOptions((oldOptions: any) => ({ ...oldOptions, ...newOptions }))
    }), []);

    useEffect(() => transport.addListener<{ open: number }>('SearchOpenChange', ({ open }) => {
        setSearchOpen(open)
    }), []);

    if (!options) {
        return <></>
    }

    if (options.sourceCode) {
        const code = (
            <CodeMirror
                options={options}
                cursor={cursor}
                markdown={markdown ?? ''}
                searchOpen={searchOpen}
                searchArg={searchArg}
                scrollTopRef={codeMirrorScrollRef}
                onMarkdownChange={onMarkdownChange}
                onCursorChange={setCursor}
                onSearchArgChange={setSearchArg}
            />
        )
        if (!options.splitPreview) return code
        return (
            <>
                <div className="split-code">{code}</div>
                <Preview markdown={markdown ?? ''} options={options} />
            </>
        )
    } else {
        return (
            <MuyaEditor
                options={options}
                cursor={cursor}
                markdown={markdown ?? ''}
                searchOpen={searchOpen}
                searchArg={searchArg}
                scrollTopRef={muyaScrollTopRef}
                onMarkdownChange={onMarkdownChange}
                onCursorChange={setCursor}
                onSearchArgChange={setSearchArg}
            />
        )
    }
}

export default Editor;