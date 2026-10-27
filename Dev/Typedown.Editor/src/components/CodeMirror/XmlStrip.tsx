import React, { useEffect, useRef, useState } from "react";
import transport from "services/transport";
import './xml.scss'

export interface IXmlSchemaIssue {
    line: number
    position: number
    // the problem in plain words, and as the parser said it
    text: string
    raw: string
}

// What the host tells the editor about the check against a schema (MainWindow.Xml.cs, SchemaPayload): the strings are already in the language of the person.
export interface IXmlSchema {
    // none, valid, invalid, skipped, unusable, toobig
    state: string
    status?: string
    names?: string
    // the person chose the schema (and not the one the text names)
    chosen?: boolean
    button: string
    pickTip: string
    clearTip?: string
    issues?: IXmlSchemaIssue[]
}

// What the host tells the editor about the XML text (MainWindow.Xml.cs, "XmlView"): the strings are already in the language of the person.
export interface IXmlView {
    ok: boolean
    status: string
    line: number
    position: number
    note?: string | null
    signedNote?: string | null
    formatButton: string
    schema?: IXmlSchema | null
    // the structure of the document (the host's XmlOutline): the ticks of the document map; a row of the Outline list sends "XmlGoto" to go to its line
    outline?: IXmlOutlineItem[] | null
}

export interface IXmlOutlineItem {
    level: number
    label: string
    line: number
}

interface IXmlStrip {
    view: IXmlView | null | undefined
    // the readable view is beside the code: the strip is as wide as the code
    split?: boolean
    onJump: (line: number, position: number) => void
    // told the height of the strip whenever it changes (it has a second row when it says a problem, and wraps in a narrow pane)
    onHeight?: (height: number) => void
}

// The strip under the code of an XML document: whether the text is well-formed XML (and, when it is not, where: a click goes there), whether it
// follows its schema (a click goes to the next problem and says it in plain words), a note when the file carries a digital signature, and the
// Format button.
const XmlStrip: React.FC<IXmlStrip> = ({ view, split, onJump, onHeight }) => {
    const rootRef = useRef<HTMLDivElement>(null)
    const shownView = !!view
    useEffect(() => {
        const element = rootRef.current
        if (!element || !onHeight) return
        const report = () => onHeight(Math.ceil(element.getBoundingClientRect().height))
        report()
        if (typeof ResizeObserver === 'undefined') return
        const observer = new ResizeObserver(report)
        observer.observe(element)
        return () => observer.disconnect()
    }, [shownView, onHeight])
    const issues = view?.schema?.issues
    // the problem shown: -1 until the first click
    const [current, setCurrent] = useState(-1)
    useEffect(() => setCurrent(-1), [issues])
    if (!view) return null
    const ok = view.ok !== false
    const schema = view.schema
    const list = issues ?? []
    const shown = current >= 0 && current < list.length ? list[current] : null
    const nextIssue = () => {
        if (list.length === 0) return
        const index = (current + 1) % list.length
        setCurrent(index)
        onJump(list[index].line, list[index].position)
    }
    return (
        <div ref={rootRef} className={'xml-strip' + (split ? ' xml-strip-split' : '')}>
            <button
                className={'xml-strip-status ' + (ok ? 'ok' : 'bad')}
                disabled={ok}
                title={view.status}
                onClick={() => onJump(view.line, view.position)}
            >
                <span className="xml-strip-mark" aria-hidden="true">{ok ? '✓' : '✗'}</span>
                <span role="status" aria-live="polite">{view.status}</span>
            </button>
            {schema && schema.state !== 'none' ? (
                <button
                    className={'xml-strip-status xml-strip-schema ' + (schema.state === 'valid' ? 'ok' : schema.state === 'invalid' || schema.state === 'unusable' ? 'bad' : 'idle')}
                    disabled={schema.state !== 'invalid'}
                    title={schema.names ? schema.names + (schema.status ? ': ' + schema.status : '') : schema.status}
                    onClick={nextIssue}
                >
                    <span role="status" aria-live="polite">{schema.status}</span>
                </button>
            ) : null}
            {shown ? <span className="xml-strip-issue" title={shown.raw}>{shown.text}</span> : null}
            {view.note ? <span className="xml-strip-note">{view.note}</span> : null}
            {view.signedNote ? <span className="xml-strip-note warn">{view.signedNote}</span> : null}
            <span className="xml-strip-spacer" />
            {schema && schema.state !== 'none' ? (
                <button
                    className="xml-strip-icon"
                    title={schema.clearTip}
                    aria-label={schema.clearTip}
                    onClick={() => transport.postMessage('XmlSchemaClear', {})}
                >
                    {'×'}
                </button>
            ) : null}
            {schema ? (
                <button
                    className="xml-strip-button"
                    title={schema.pickTip}
                    onClick={() => transport.postMessage('XmlSchemaChoose', {})}
                >
                    {schema.button}
                </button>
            ) : null}
            <button
                className="xml-strip-button"
                disabled={!ok}
                onClick={() => transport.postMessage('XmlFormatRequest', {})}
            >
                {view.formatButton}
            </button>
        </div>
    )
}

export default XmlStrip
