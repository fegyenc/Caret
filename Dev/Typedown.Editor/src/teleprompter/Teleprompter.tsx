// The teleprompter and the speaking clock (docs/speech-marks-design.md, step 2). A window of its own (a second screen is the real
// use) that shows the talk in big text, scrolls it at the planned pace (each paragraph passes in its planned time), draws the
// marks as large cues, counts down to the next pause, and keeps a clock: time spoken, time left, ahead or behind the plan, the
// section and its light. Start and stop on Space, next and back on the arrows and Page Up/Down (a presentation clicker sends
// those), speed on Up/Down; a step mode that does not scroll by itself (also the default when Windows asks for less motion).
// The plan and the clockwork are plain functions (plan.js, player.js) with their own tests; this file draws them and keeps the
// time. Still no network, no audio: the text comes from the main window, the time from the clock of the PC.
import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import './teleprompter.css'
import { buildPlan } from './plan'
import { locate, nextBlockStart, prevBlockStart, upcoming, clockState, blockAt } from './player'
import { formatClock } from '../components/Muya/lib/parser/speechTiming'
import { paceStyle, SWATCHES } from '../components/Muya/lib/parser/speech'

type Script = {
    markdown: string, wpm: number, headingsSpoken: boolean, styles?: Record<string, { color?: string, icon?: string }>,
    labels?: Record<string, string>, title?: string
}

const DEFAULT_LABELS: Record<string, string> = {
    start: 'Start', stop: 'Stop', elapsed: 'Spoken', left: 'Left', over: 'over', planned: 'Planned', ahead: 'Ahead', behind: 'Behind', onPlan: 'On plan',
    pause: 'Pause', audience: 'Audience', pauseIn: 'Pause in', audienceIn: 'Audience in', now: 'Now', auto: 'Automatic', step: 'Step by step',
    next: 'Next', back: 'Back', mirror: 'Mirror', dark: 'Dark', light: 'Light', fullscreen: 'Full screen', clock: 'Clock', close: 'Close',
    speed: 'Speed', size: 'Text size', end: 'End of the talk', empty: 'Nothing to read yet.', waiting: 'Waiting for the text…',
    help: 'Space start/stop · ← → or Page Up/Down back/next · ↑ ↓ speed · S step mode · M mirror · D dark/light · [ ] size · C clock · F full screen · R restart · Esc stop',
    section: 'Section'
}

type Prefs = { size: number, mirror: boolean, dark: boolean, mode: 'auto' | 'step', clock: boolean, speed: number }
const loadPrefs = (): Prefs => {
    const reduced = !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const base: Prefs = { size: 48, mirror: false, dark: true, mode: reduced ? 'step' : 'auto', clock: true, speed: 1 }
    try {
        const saved = JSON.parse(window.localStorage.getItem('caret.teleprompter') || '{}')
        return { ...base, ...saved }
    } catch (e) {
        return base
    }
}
const savePrefs = (p: Prefs) => { try { window.localStorage.setItem('caret.teleprompter', JSON.stringify(p)) } catch (e) { /* storage may be off */ } }

const host = () => (window as any).chrome && (window as any).chrome.webview
const send = (name: string, args?: any) => { const h = host(); if (h) h.postMessage(JSON.stringify({ name, args })) }

const READING_LINE = 0.35 // the line being read is this far down the window

export default function Teleprompter ({ clockOnly }: { clockOnly: boolean }) {
    const [script, setScript] = useState<Script | null>(null)
    const [prefs, setPrefs] = useState<Prefs>(loadPrefs)
    const [, setTick] = useState(0)
    const scroller = useRef<HTMLDivElement>(null)
    const refs = useRef<Record<string, HTMLElement | null>>({})
    // the time: `elapsed` is what has really been spoken, `place` the place in the planned speech
    const clock = useRef({ running: false, elapsed: 0, place: 0, last: 0, speed: 1, mode: 'auto' as 'auto' | 'step' })
    const prefsRef = useRef(prefs)
    prefsRef.current = prefs
    const labels = useMemo(() => ({ ...DEFAULT_LABELS, ...(script?.labels ?? {}) }), [script])
    const plan = useMemo(() => script ? buildPlan(script.markdown, { wpm: script.wpm, headingsSpoken: script.headingsSpoken }) : null, [script])
    const planRef = useRef<any>(null)
    planRef.current = plan

    useEffect(() => { savePrefs(prefs); clock.current.speed = prefs.speed; clock.current.mode = prefs.mode }, [prefs])

    // the text comes from the main window; it is asked for when this page is ready, and sent again when the text changes
    useEffect(() => {
        const h = host()
        const onMessage = ({ data }: { data: string }) => {
            try {
                const { name, args } = JSON.parse(data)
                if (name === 'Script') setScript(args)
            } catch (e) { /* not ours */ }
        }
        if (h) h.addEventListener('message', onMessage)
        send('TeleprompterReady')
        return () => { if (h) h.removeEventListener('message', onMessage) }
    }, [])

    useEffect(() => { document.title = `${script?.title ?? ''} ${clockOnly ? labels.clock : 'Teleprompter'}`.trim() }, [script, clockOnly, labels])
    useEffect(() => { document.body.className = prefs.dark ? 'tp-dark' : 'tp-light' }, [prefs.dark])

    // --- scrolling: the reading line moves through the text with the planned time ---
    const scrollTo = useCallback((place: number, smooth: boolean) => {
        const box = scroller.current
        const p = planRef.current
        if (!box || !p || clockOnly) return
        const at = locate(p, place)
        if (!at) return
        const index = at.block.segments.indexOf(at.segment)
        const el = refs.current[`${at.block.index}:${index}`]
        if (!el) return
        const origin = box.getBoundingClientRect().top - box.scrollTop
        let y: number
        const rects = Array.from(el.getClientRects())
        if (at.segment.type === 'words' && rects.length) {
            const total = rects.reduce((n, r) => n + r.width, 0) || 1
            let seen = 0
            const wanted = at.within * total
            y = rects[rects.length - 1].bottom - origin
            for (const r of rects) {
                if (wanted <= seen + r.width) { y = r.top + r.height * ((wanted - seen) / (r.width || 1)) - origin; break }
                seen += r.width
            }
        } else {
            const r = el.getBoundingClientRect()
            y = r.top + r.height / 2 - origin
        }
        const top = Math.max(0, y - box.clientHeight * READING_LINE)
        if (smooth && !(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches)) box.scrollTo({ top, behavior: 'smooth' })
        else box.scrollTop = top
    }, [clockOnly])

    // --- the clock ---
    useEffect(() => {
        let raf = 0
        let shown = 0
        const frame = (ts: number) => {
            const c = clock.current
            const p = planRef.current
            const dt = c.last ? Math.min(0.25, (ts - c.last) / 1000) : 0
            c.last = ts
            if (c.running && p) {
                c.elapsed += dt
                if (c.mode === 'auto') {
                    c.place = Math.min(p.total, c.place + dt * c.speed)
                    scrollTo(c.place, false)
                    if (c.place >= p.total) c.running = false
                }
            }
            if (ts - shown > 100) { shown = ts; setTick(n => n + 1) }
            raf = window.requestAnimationFrame(frame)
        }
        raf = window.requestAnimationFrame(frame)
        return () => window.cancelAnimationFrame(raf)
    }, [scrollTo])

    // a new text: the place is kept, as far as the new talk goes
    useEffect(() => {
        if (!plan) return
        clock.current.place = Math.min(clock.current.place, plan.total)
        window.requestAnimationFrame(() => scrollTo(clock.current.place, false))
    }, [plan, scrollTo, prefs.size])

    const jump = useCallback((place: number) => {
        const p = planRef.current
        if (!p) return
        clock.current.place = Math.min(p.total, Math.max(0, place))
        scrollTo(clock.current.place, clock.current.mode === 'step' || !clock.current.running)
    }, [scrollTo])

    const toggle = useCallback(() => {
        const c = clock.current
        const p = planRef.current
        if (!p) return
        if (!c.running && c.mode === 'auto' && c.place >= p.total) { c.place = 0; c.elapsed = 0; scrollTo(0, false) }
        c.running = !c.running
    }, [scrollTo])

    const restart = useCallback(() => {
        const c = clock.current
        c.running = false
        c.elapsed = 0
        c.place = 0
        scrollTo(0, false)
    }, [scrollTo])

    const change = useCallback((patch: Partial<Prefs>) => setPrefs(p => ({ ...p, ...patch })), [])

    useEffect(() => {
        const onKey = (e: KeyboardEvent) => {
            if (e.ctrlKey || e.altKey || e.metaKey) return
            const c = clock.current
            const p = planRef.current
            const key = e.key
            let handled = true
            if (key === ' ' || key === 'Spacebar') toggle()
            else if (key === 'ArrowRight' || key === 'PageDown') p && jump(nextBlockStart(p, c.place))
            else if (key === 'ArrowLeft' || key === 'PageUp') p && jump(prevBlockStart(p, c.place))
            else if (key === 'ArrowUp') change({ speed: Math.min(2, Math.round((prefsRef.current.speed + 0.05) * 100) / 100) })
            else if (key === 'ArrowDown') change({ speed: Math.max(0.5, Math.round((prefsRef.current.speed - 0.05) * 100) / 100) })
            else if (key === 'Home' || key === 'r' || key === 'R') restart()
            else if (key === 's' || key === 'S') change({ mode: prefsRef.current.mode === 'auto' ? 'step' : 'auto' })
            else if (key === 'm' || key === 'M') change({ mirror: !prefsRef.current.mirror })
            else if (key === 'd' || key === 'D') change({ dark: !prefsRef.current.dark })
            else if (key === 'c' || key === 'C') change({ clock: !prefsRef.current.clock })
            else if (key === '[') change({ size: Math.max(20, prefsRef.current.size - 4) })
            else if (key === ']') change({ size: Math.min(160, prefsRef.current.size + 4) })
            else if (key === 'f' || key === 'F' || key === 'F11') send('Fullscreen')
            else if (key === 'Escape') { if (c.running) c.running = false; else send('Escape') }
            else handled = false
            if (handled) e.preventDefault()
        }
        window.addEventListener('keydown', onKey)
        return () => window.removeEventListener('keydown', onKey)
    }, [toggle, jump, restart, change])

    // --- drawing ---
    const c = clock.current
    const state = plan ? clockState(plan, c.elapsed, c.place) : null
    const next = plan ? upcoming(plan, c.place) : null
    const here = plan ? blockAt(plan, c.place) : null
    const styleOf = (name: string) => script?.styles?.[name]

    const renderSegment = (block: any, segment: any, j: number) => {
        const key = `${block.index}:${j}`
        const ref = (el: HTMLElement | null) => { refs.current[key] = el }
        switch (segment.type) {
            case 'title':
                return <span key={j} ref={ref}>{segment.text}</span>
            case 'words': {
                const classes = ['tp-words']
                const style: React.CSSProperties = {}
                for (const s of segment.styles) {
                    if (s.kind === 'pace') {
                        Object.assign(style, paceStyle({ speed: s.speed, perWord: s.perWord }))
                        classes.push(s.speed < 1 ? 'tp-slower' : 'tp-faster')
                    }
                    if (s.user) {
                        const look = styleOf(s.name)
                        classes.push('tp-user')
                        if (look && look.color && SWATCHES.includes(look.color)) classes.push(`tp-c-${look.color}`)
                    } else if (['loud', 'soft', 'emphasis', 'tone'].includes(s.name)) {
                        classes.push(`tp-${s.name}`)
                    }
                }
                return <span key={j} ref={ref} className={classes.join(' ')} style={style}>{segment.text}</span>
            }
            case 'pause': {
                const active = next && next.active && next.remaining !== undefined && c.place >= block.start + segment.at && c.place < block.start + segment.at + segment.seconds
                const secs = active ? (next as any).remaining : segment.seconds
                const word = segment.audience ? labels.audience : labels.pause
                return (
                    <span key={j} ref={ref} className={`tp-pause${segment.audience ? ' tp-audience' : ''}${active ? ' tp-active' : ''}`}>
                        {word} {formatClock(Math.ceil(secs - 1e-6))}{segment.note ? ` · ${segment.note}` : ''}
                    </span>
                )
            }
            case 'cue':
                return <span key={j} ref={ref} className="tp-cue">▶ {segment.text}</span>
            case 'open': {
                if (segment.name === 'emphasis' || segment.name === 'loud' || segment.name === 'soft') return null
                const look = segment.user ? styleOf(segment.name) : undefined
                const text = segment.name === 'tone' ? segment.text : `${look && look.icon ? look.icon + ' ' : ''}${segment.name}`
                return <span key={j} ref={ref} className="tp-label">{text}</span>
            }
            default:
                return null
        }
    }

    const nextText = next
        ? (next.active
            ? `${next.audience ? labels.audience : labels.pause} ${formatClock(Math.ceil((next.remaining as number) - 1e-6))}`
            : `${next.audience ? labels.audienceIn : labels.pauseIn} ${formatClock(Math.ceil((next.in as number) - 1e-6))}`)
        : ''

    const aheadText = state
        ? (Math.abs(state.ahead) < 1 ? labels.onPlan : `${state.ahead > 0 ? labels.ahead : labels.behind} ${formatClock(Math.abs(state.ahead))}`)
        : ''
    const light = state ? state.light : ''
    const lightDot = light === 'green' ? '#2e9d57' : light === 'amber' ? '#d98e04' : light === 'red' ? '#e5484d' : 'transparent'

    const panel = state && plan && (
        <div className={`tp-clock${clockOnly ? ' tp-clock-only' : ''}`} role="timer" aria-label={labels.clock}>
            <button type="button" className="tp-button tp-primary" onClick={toggle} aria-label={c.running ? labels.stop : labels.start}>{c.running ? '❚❚' : '▶'}</button>
            <div className="tp-time">
                <span className="tp-big">{formatClock(state.elapsed)}</span>
                <span className="tp-small">{labels.elapsed} · {labels.planned} {formatClock(state.total)}</span>
            </div>
            <div className="tp-time">
                <span className={`tp-big${state.remaining < 0 ? ' tp-over' : ''}`}>{state.remaining < 0 ? '+' : ''}{formatClock(Math.abs(state.remaining))}</span>
                <span className="tp-small">{state.remaining < 0 ? labels.over : labels.left}</span>
            </div>
            <div className="tp-time tp-grow">
                <span className="tp-big tp-light"><i style={{ background: lightDot }} aria-hidden="true" />{aheadText}</span>
                <span className="tp-small">{state.section ? `${state.section.title || labels.section} · ${formatClock(state.section.left)}${state.section.budget > 0 ? ` / ${formatClock(state.section.budget)}` : ''}` : ''}</span>
            </div>
            <div className="tp-time">
                <span className="tp-big">{nextText}</span>
                <span className="tp-small">{prefs.mode === 'auto' ? `${labels.speed} ${Math.round(prefs.speed * 100)} %` : labels.step}</span>
            </div>
            {clockOnly && (
                <div className="tp-keys">
                    <button type="button" className="tp-button" onClick={() => plan && jump(prevBlockStart(plan, c.place))} aria-label={labels.back}>‹</button>
                    <button type="button" className="tp-button" onClick={() => plan && jump(nextBlockStart(plan, c.place))} aria-label={labels.next}>›</button>
                    <button type="button" className="tp-button" onClick={restart} aria-label="R">↺</button>
                </div>
            )}
        </div>
    )

    if (!plan) return <div className="tp-message">{labels.waiting}</div>
    if (!plan.blocks.length) return <div className="tp-message">{labels.empty}</div>

    return (
        <div className="tp-root" style={{ ['--tp-size' as any]: `${prefs.size}px` }}>
            {!clockOnly && (
                <div ref={scroller} className={`tp-scroll${prefs.mirror ? ' tp-mirror' : ''}`}>
                    <div className="tp-page">
                        {plan.blocks.map((block: any) => {
                            const past = block.end <= c.place && block.end > block.start
                            const classes = `tp-block ${block.kind === 'heading' ? `tp-h tp-h${Math.min(block.level, 3)}` : 'tp-p'}${past ? ' tp-past' : ''}${here && here.index === block.index ? ' tp-here' : ''}`
                            const parts = block.segments.map((s: any, j: number) => renderSegment(block, s, j))
                            return block.kind === 'heading'
                                ? <h2 key={block.index} className={classes}>{parts}</h2>
                                : <p key={block.index} className={classes}>{parts}</p>
                        })}
                        <p className="tp-block tp-p tp-endmark">{labels.end}</p>
                    </div>
                </div>
            )}
            {!clockOnly && <div className="tp-line" aria-hidden="true" style={{ top: `${READING_LINE * 100}%` }} />}
            {!clockOnly && nextText && <div className={`tp-next${next && next.active ? ' tp-active' : ''}`} aria-live="off">{nextText}</div>}
            {prefs.clock && panel}
            {clockOnly && !prefs.clock && panel}
            {!clockOnly && (
                <div className="tp-toolbar">
                    <button type="button" className="tp-button" onClick={() => change({ mode: prefs.mode === 'auto' ? 'step' : 'auto' })} aria-pressed={prefs.mode === 'step'}>{prefs.mode === 'auto' ? labels.auto : labels.step}</button>
                    <button type="button" className="tp-button" onClick={() => change({ size: Math.max(20, prefs.size - 4) })} aria-label={`${labels.size} −`}>A−</button>
                    <button type="button" className="tp-button" onClick={() => change({ size: Math.min(160, prefs.size + 4) })} aria-label={`${labels.size} +`}>A+</button>
                    <button type="button" className="tp-button" onClick={() => change({ mirror: !prefs.mirror })} aria-pressed={prefs.mirror}>{labels.mirror}</button>
                    <button type="button" className="tp-button" onClick={() => change({ dark: !prefs.dark })}>{prefs.dark ? labels.light : labels.dark}</button>
                    <button type="button" className="tp-button" onClick={() => change({ clock: !prefs.clock })} aria-pressed={prefs.clock}>{labels.clock}</button>
                    <button type="button" className="tp-button" onClick={() => send('Fullscreen')}>{labels.fullscreen}</button>
                    <span className="tp-help">{labels.help}</span>
                </div>
            )}
        </div>
    )
}
