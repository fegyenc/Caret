import React from "react";
import transport from "services/transport";
import './xml.scss'

// What the host tells the editor about the XML text (MainWindow.Xml.cs, "XmlView"): the strings are already in the language of the person.
export interface IXmlView {
    ok: boolean
    status: string
    line: number
    position: number
    note?: string | null
    signedNote?: string | null
    formatButton: string
}

interface IXmlStrip {
    view: IXmlView | null | undefined
    // the readable view is beside the code: the strip is as wide as the code
    split?: boolean
    onJump: (line: number, position: number) => void
}

// The strip under the code of an XML document: whether the text is well-formed XML (and, when it is not, where: a click goes there), a note
// when the file carries a digital signature, and the Format button.
const XmlStrip: React.FC<IXmlStrip> = ({ view, split, onJump }) => {
    if (!view) return null
    const ok = view.ok !== false
    return (
        <div className={'xml-strip' + (split ? ' xml-strip-split' : '')}>
            <button
                className={'xml-strip-status ' + (ok ? 'ok' : 'bad')}
                disabled={ok}
                title={view.status}
                onClick={() => onJump(view.line, view.position)}
            >
                <span className="xml-strip-mark" aria-hidden="true">{ok ? '✓' : '✗'}</span>
                <span role="status" aria-live="polite">{view.status}</span>
            </button>
            {view.note ? <span className="xml-strip-note">{view.note}</span> : null}
            {view.signedNote ? <span className="xml-strip-note warn">{view.signedNote}</span> : null}
            <span className="xml-strip-spacer" />
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
