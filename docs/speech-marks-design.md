# Speech marks: design

Status: proposal, 2026-10-07. Nothing in this document is built yet. Phase 1 is built only after the owner confirms it.

## 1. What it is for

A speaker prepares a talk as text. Text does not say where to go fast, where to slow down, where to wait, where the joke is, or how a sentence should sound. A transcript made by speech-to-text has the same gap: it keeps the words and loses the humour, the irony and the feel.

Speech marks put those decisions into the text itself, as plain characters in the `.md` file. Three things follow from that:

1. The speaker can see and plan the delivery while writing.
2. Caret can do arithmetic on the marks (how long the talk takes, where the pace changes), offline.
3. The marked text can be pasted into any AI assistant by the speaker, who then explains how they want to deliver the talk. Caret never talks to an AI.

It follows the review function (CriticMarkup marks, built in small steps, used only on request): same layers, same way of working.

## 2. Rules that do not change

| Rule | What it means here |
|---|---|
| No AI, no network, no keys | Every number comes from counting words and adding seconds. Nothing is "understood". The marks are text; the speaker pastes them to an AI themselves. |
| Only on request | Speech mode is switched on by the user and is off by default. Opening a file never turns it on, even if the file contains marks. Nothing is inserted, changed or removed unless the user presses a command. |
| Plain text survives | The `.md` file is the only storage. No sidecar is needed to read the marks. Any editor, git, email or chat shows them as readable text. |
| Four languages | Every UI string exists in English, French, Spanish and Polish (`Strings/*/AppResources.resw`); `LocalizationTests` already checks the files agree. The mark keywords in the file are always English (section 3.5). |
| Undo works | Inserting or removing marks is typed into the page the way the review comment is, so Ctrl+Z works. |
| Tests with every step | jest for the editor, `Dev/Caret.ConverterTests` for the .NET rules, a `CHANGES.md` entry, and a check in the real app with the guarded UI script. |

## 3. The syntax

### 3.1 Choice: curly braces with a word

```
{pause 2s}              a pause of two seconds
{slow}...{/slow}        this part slowly
{tone: dry irony}...{/tone}
```

Why braces, and why a letter after the brace:

- Every CriticMarkup opener is `{` followed by a symbol: `{++`, `{--`, `{~~`, `{==`, `{>>`. A speech mark is `{` followed by a letter or `/`. The two can never be confused, and they can sit inside each other (a speech mark inside an added text, or a comment that mentions one).
- Square brackets were rejected: `[pause]` is a Markdown shortcut link. Parentheses were rejected: they are ordinary text in a speech. HTML comments (`<!-- pause -->`) were rejected: invisible in every renderer, long, and lost when text is copied from a rendered page. Symbols such as a pause sign were rejected: hard to type, hard for the speaker to remember, font problems.
- The keywords follow what speakers, directors and the W3C speech standard (SSML: break, prosody rate, prosody volume, emphasis) already call these things, so an AI recognises them without a legend. The export still carries a legend (section 6.3).

### 3.2 Grammar

```
mark      = "{" [ "/" ] name [ " " value ] [ ":" " " text ] "}"
name      = one of the words below, case does not matter
value     = a duration (2s, 1.5s, 1m, 1m30s) or a number (140)
text      = free words, no "{", no "}", no line break
```

A mark is inside one paragraph, as CriticMarkup marks are. Paired marks (`{slow}` ... `{/slow}`) open and close in the same paragraph. An opener with no closer in its paragraph runs to the end of that paragraph; a closer with no opener is ignored. A selection over several paragraphs gets one pair per paragraph.

### 3.3 The vocabulary

The rule behind the list: **what Caret can count has fixed words; what only a human or an AI can interpret is free text.** That keeps the arithmetic honest and the expression open.

| Group | Mark | Meaning | Counted by Caret? |
|---|---|---|---|
| Time | `{beat}` | very short pause, 0.5 s | yes |
| | `{pause}` / `{pause 3s}` | pause, 1 s by default, or the stated length | yes |
| | `{wait}` / `{wait 5s: laugh}` | time left for the audience (laugh, applause, a question); 3 s by default. Same as a pause for the arithmetic, but shown and reported as "audience" | yes |
| Pace | `{slow}...{/slow}` | slower than the baseline, speed x 0.75 | yes |
| | `{fast}...{/fast}` | faster than the baseline, speed x 1.25 | yes |
| Volume | `{loud}...{/loud}`, `{soft}...{/soft}` | louder, quieter | shape only (no time effect) |
| Stress | `{emphasis}...{/emphasis}` | stress this word or phrase | shape only |
| Tone | `{tone: dry irony}...{/tone}` | any tone or intent in the speaker's own words: joke, irony, warm, serious, urgent, humble | not counted; read by people and AI |
| Cue | `{cue: look at the back row}` | anything to do or remember: slide, gesture, prop, sip of water, breathe | not counted; shown in the cue list |
| Settings in the text | `{wpm 140}` | speaking speed in words per minute from here on | yes |
| | `{budget 3m}` | time allowed for this section (the heading it is under) | yes |

Notes on what was left out of the first proposal and why:

- `{whisper}`, `{joke}`, `{smile}`, `{serious}`, `{sarcastic}`, `{breathe}`, `{slide 4}` as separate fixed words. They are many near-synonyms, and every fixed word is one more thing to remember on a stage. `{soft}`, `{tone: ...}` and `{cue: ...}` carry all of them, and the palette (section 5) offers them as one-click presets that fill in the free text. The file stays readable and the speaker can still write `{tone: whisper}`.
- Markdown bold and italic are **not** read as speech marks. They mean whatever the writer meant for slides or handouts; a delivery decision should not depend on how another tool draws bold.
- Speed factors are fixed in phase 1 (0.75, 1.25). A number on the mark (`{slow 60%}`) can be added later without breaking files.

### 3.4 Escaping and unknown marks

- `\{pause\}` is plain text, as for any Markdown character (the editor already treats `\{` as an escaped brace).
- Marks are never read inside code spans or fenced code blocks, and not inside the note of a CriticMarkup comment (`{>>@Ann: put {pause} here<<}` stays a comment). They are read inside added or replaced CriticMarkup text.
- **Only the words in the table are marks.** `{pauze 2s}` (a typo), `{width=50%}` (Pandoc image attributes), `{.red}` or `{{ liquid }}` are left alone as ordinary text, and keep their braces. A known word with a bad value (`{pause fast}`) is also left as text. The panel counts these near-misses (section 5.4) so a typo is found, but nothing changes them.
- Text a speaker types after a closer or inside a free note is never parsed as Markdown by the mark: the note is plain text, like a CriticMarkup comment.

### 3.5 Keywords are always English

The file format must be the same for everyone who exchanges files, so `{pause}` is never `{pauza}` or `{pausa}`. The speech itself can be in any language. The UI (chips, buttons, panel, hints) is translated in all four languages.

### 3.6 Clash check with other syntax

| Syntax | Clash? | Why |
|---|---|---|
| CriticMarkup `{++ -- ~~ == >>` | no | different second character (section 3.1) |
| Markdown, GFM | no | `{}` have no meaning; `\{` escapes |
| Pandoc attributes `{.x} {#id} {key=value}` | no | only the fixed words are marks |
| Kramdown `{: .x}`, Liquid `{{ }}`, `{% %}` | no | second character is not a letter |
| Math `$a_{i}$` | no | math is read first and consumed whole |
| HTML | no | `{}` is not special; tags are read first |
| MDX / JSX | **yes, if the file is fed to MDX** | MDX treats `{pause}` as a JavaScript expression and fails. Speeches are not MDX files; listed as a risk |
| Heading anchors | **yes** | `## Opening {budget 3m}` makes the generated anchor `opening-budget-3m` in sites that build anchors from headings. See risks |

## 4. What Caret computes (rules only)

The input is the document text. Steps, in order:

1. Take the text as if all review changes were accepted (the existing `ReviewMarks.Resolve(text, Accept)`): deleted text is not spoken, added text is, comments are not.
2. Find the marks (section 3.2), ignoring code.
3. Decide what is spoken. **Spoken:** paragraphs, list items, quotes, link text. **Not spoken:** headings (they are labels, with a checkbox "headings are spoken" in the panel), code, images and their alt text, URLs, review comments, the text of `{cue}`/`{tone}` marks, and table cells (left out, section 7).
4. Count words: a run of letters or digits, with inner apostrophes and hyphens ("don't", "well-known") counted as one. A number such as `2026` counts as one word.
5. Time of a stretch of words = words / (baseline wpm x speed factor) x 60 seconds. Nested pace marks multiply (slow inside fast: 0.75 x 1.25).
6. Add the seconds of `{beat}`, `{pause}`, `{wait}`.
7. Group by section: a section is a heading and everything up to the next heading of the same or higher level. Compare with its `{budget}`.

Defaults: baseline 130 words per minute until a `{wpm N}` mark or the panel field says otherwise. Budget light: green up to 100 % of the budget, amber up to 110 %, red above. The light is never colour only: the panel writes "over by 0:20".

**Accuracy, said plainly:** a word count is a rough measure of speaking time. Languages differ (Hungarian and Polish words are longer than English ones, so words per minute is lower), numbers and names take longer than they count, and every speaker is different. Expect within about 15 % once the speaker has set their own baseline; the first estimate before that can be further off. Step 3 improves this by measuring the speaker's own pace in a rehearsal and offering it as the baseline. Languages written without spaces (Chinese, Japanese) are not supported by this word count.

## 5. What the user sees

### 5.1 Speech mode

- A toolbar button (a speech-bubble icon; **not** a microphone, because Caret records no sound) and a View menu entry switch **Speech mode** on and off. The state is a setting, off by default, like Split.
- **Off:** the editor behaves exactly as today. `{pause 2s}` is ordinary text. This is deliberate: documents that never use the mode cannot be affected by it.
- **On:** the marks are recognised and drawn; the Speech card appears.
- Code view always shows the raw text. In Split, the preview follows Speech mode.

### 5.2 Chips and marked text in the View

Never raw braces while the caret is elsewhere; the raw mark shows, in gray, when the caret is inside it (the same behaviour as the review marks, so the text stays editable and the file is never rewritten).

| Mark | Drawn as |
|---|---|
| `{pause 2s}`, `{beat}`, `{wait}` | A small chip with an icon and the length: "pause 2 s", "audience 3 s". The chip is drawn with generated content, so it is not part of the text and does not disturb caret offsets |
| `{slow}` text | letter-spacing widened and a dotted underline |
| `{fast}` text | letter-spacing narrowed and a dashed underline |
| `{loud}` text | larger and heavier |
| `{soft}` text | smaller and lighter |
| `{emphasis}` text | accent colour with a solid underline |
| `{tone: x}` text | a soft coloured band with the tone word as a small label before it |
| `{cue: x}` | a chip with an icon and the first words; the full text in the tooltip |
| `{budget 3m}`, `{wpm 140}` | a quiet chip |

Accessibility: nothing relies on colour alone (every style has a shape, spacing or label as well); chip text meets 4.5:1 contrast in light, dark and Windows high-contrast themes (system colours are used in forced-colors mode); every chip has a text tooltip and an accessible name in the UI language; the palette is fully keyboard-operable.

### 5.3 The palette (the speech-building tool)

The Speech card in the sidebar holds the palette as ordinary buttons that stay on screen (a flyout would close after each insertion). Groups: Time, Pace, Volume and stress, Tone, Cue, Joke. Behaviour:

- **Point marks** (pause, beat, wait, cue) are inserted at the caret.
- **Paired marks** wrap the selection. With nothing selected, `emphasis` wraps the word at the caret and the other pairs wrap the sentence at the caret.
- A selection that cannot be wrapped without cutting other syntax (it touches a link, an image, code or math) is not wrapped; the mark goes at the end of the paragraph instead, as the review comment does.
- Tone presets: joke, irony, warm, serious, urgent, humble. They fill in the free text, which the speaker can change. Cue presets: look at the audience, next slide, gesture, show an object, drink, breathe.
- **Joke** inserts a structure: `{tone: joke}setup {beat} punchline{/tone}{wait 3s: laugh}` around the selection or at the caret, so room after the laugh is the default, not an afterthought.
- Right-click on a selection offers the same marks under "Speech mark", as "Add comment" does. Right-click on a mark offers "Remove mark".
- **Edit > Remove all speech marks**: one step in Undo, leaves the spoken text as it is. This is how a speaker gets a clean text for a handout or an article.
- Shortcuts: a few direct ones for the most used marks, decided in the build after checking the editor's keymap. **Not** Ctrl+Alt+letter: on Polish, Hungarian and French keyboards that combination is AltGr and types characters, including `{`.

### 5.4 Timing panel and the shape of the talk

Part of the Speech card, next to the outline:

- Total estimated time against the total budget, the baseline wpm (editable; the edit writes or updates `{wpm N}` at the top of the document, as one Undo step), and the checkbox "headings are spoken".
- One row per section: title, words, estimated time, budget, light.
- **The shape strip:** the talk from top to bottom, one bar per paragraph, height in proportion to its time. Bar offset is pace against the baseline (left slower, right faster), fill is volume, ticks are pauses and audience time. A glance shows where it is all fast and where the quiet moments are. Clicking a bar scrolls the editor to that paragraph. This is drawn from the same numbers as the table; no extra analysis. (A strip aligned to the editor's scrollbar is possible later; the panel version is robust against the editor redrawing its paragraphs.)
- **Hints:** a short list of plain facts, never style advice: a section over its budget; a `{tone: joke}` with no `{wait}` or `{pause}` right after it; more than a minute of `{fast}` in a row; a pause longer than 10 s (probably a typo); marks the mark list does not recognise (section 3.4).

## 6. Phases

Built the way the review function was built: small pull requests, each with tests, a `CHANGES.md` entry, translations, a check in the real app (the guarded UI script, with the dev data and clipboard backed up and restored), Auto-fix on, and CodeRabbit comments settled before the owner merges.

### Step 1: marks, palette, chips, timing (phase 1, to be confirmed)

| PR | Content | Tests |
|---|---|---|
| 1a | Speech mode setting and View menu item; parser rule and renderer for the marks; chips and styles; labels sent from the host in the UI language | jest for the parser (every mark, round trip text unchanged, code and escapes, inside CriticMarkup, near-misses); the page in a browser; real app: marks typed by hand in Code view appear as chips in View |
| 1b | Palette in the Speech card; wrap/insert page script (paragraph id and offsets, as the review script); right-click menu; Remove all speech marks; shortcuts | .NET tests for the markup builder and for removal; page script in a browser; real app |
| 1c | `SpeechTiming` (plain .NET, compiled into the tests like `ReviewMarks`); timing panel; wpm and budget; traffic light | .NET tests for counting, pace maths, nesting, sections, review text accepted first, locales (comma and dot decimals) |
| 1d | Shape strip; hints; **Copy for AI** (section 6.3) | .NET tests for the shape data, hints and export text; real app |

**Copy for AI is pulled forward into step 1** (it was step 3 in the first proposal): it is only the text, a short legend and the planned numbers, and it is what delivers the owner's main goal. After step 1 the speaker can already hand a marked talk to an AI.

### Step 2: teleprompter

Big text on a dark or light page, scrolling at the planned pace (each paragraph passes in its planned time), marks drawn as large cues, a visible countdown to the next pause, start/stop on Space, speed adjust, and a step-by-paragraph mode that does not scroll by itself (for people who prefer it, and when reduced motion is asked for in Windows). Page Up/Page Down work, because presentation clickers send them. Optional mirror mode for a glass prompter. The first decision of step 2 is a separate window for a second screen or an overlay in the main window; a second screen is the real use, so a separate window is likely. Still no network, no audio.

### Step 3: rehearsal capture and the AI export with numbers

- **Rehearse** is a mode of the teleprompter. The speaker reads aloud and taps a few keys: Right arrow or Page Down = on to the next paragraph (this gives the time per paragraph), Space = a pause started or ended (the real length of the pause taken), Up/Down = "slower"/"faster than planned here", L = "laugh happened", X = "stumbled, did it again".
- Caret records only **what the speaker pressed and when**, and writes planned against actual per paragraph into a plain Markdown file beside the speech, `name.rehearsal.md`, one block per run with the date: times, differences, extra pauses, taps, and the measured words per minute (with a button to adopt it as the baseline).
- **Copy for AI** then adds the planned and actual numbers.
- **Cue cards / speaker script** for printing: large text, marks as printed symbols and words.

What this does and does not give: the speaker is the sensor. The timing is as exact as their taps (about a second or two per paragraph), not per word. It carries the feel of a delivery (where it ran long, where the room laughed, where they stumbled) as text an AI can read, without any audio.

### 6.3 Copy for AI

Plain text put on the clipboard (the speaker pastes it where they want): a one-line header saying it is a speech script with delivery marks; a legend of the marks used in the document, one line each, in English; the baseline, planned total and planned section times; then the document text with the marks as they are in the file. An option writes the marks as words (`(pause, 2 seconds)`) for tools that dislike braces. Nothing is sent anywhere by Caret.

## 7. Deliberately left out, and what cannot work offline with rules

| Idea | Decision | Why |
|---|---|---|
| Microphone, voice analysis of volume or speed, detecting laughs | **Not built, and not planned** | Caret has no audio. A microphone permission also changes what the Store listing and the privacy policy can say, and it is not "rules and arithmetic". The rehearsal taps give the same facts that matter, supplied by the speaker |
| Judging humour, irony, tone or "how it sounds" | **Impossible with rules** | No rule can understand a joke. Caret stores what the speaker says they intend; reading it is for the speaker and, if they paste it, their AI |
| Speech-to-text of a rehearsal | **Not built** | needs a speech model; contradicts the no-AI promise |
| Reading the script aloud with a synthetic voice to hear the plan | Not now; an owner decision | Windows has local voices, so it would work offline, but it is speech technology and a possible conflict with the "no AI" wording of the Store pitch. Humour cannot be carried by it anyway |
| Automatic Speech mode, or suggestions to add marks | **Never** | "Only on request" |
| Table cells in the time count | Left out | tables are rarely spoken as written; the speaker can put the spoken text in a paragraph |
| CJK word counting | Left out | needs a dictionary, not a rule |
| Speed numbers on `{slow}`/`{fast}`, more volume levels | Later if asked | fixed values keep files comparable |
| Slides, PowerPoint speaker notes | Not in this feature | the Convert hub already reads PowerPoint notes into Markdown; marking them up afterwards works with this feature as it is |
| SSML export | Not planned | possible later from the same marks; only useful with a voice engine |

## 8. Risks

| Risk | What could go wrong | What we do |
|---|---|---|
| Clash with other syntax | A mark read where it was not meant, or another tool's `{...}` read as a mark | Fixed word list only; letter after the brace; not read in code; escape `\{`; Speech mode off by default so existing documents are untouched. MDX is the one known tool that fails on `{pause}`; stated in the user documentation |
| Round trip through other editors | An editor that rewrites the text could escape or reflow braces | The marks are plain characters; Caret never rewrites a file on open. Test: save, reopen, and compare in Caret; check a copy through the clipboard and through the Convert hub; note editors that escape `{` |
| Heading anchors and outline | `## Opening {budget 3m}` changes generated anchors and the outline label | The outline label hides marks; the user documentation says to use **Remove all speech marks** before publishing; `{budget}` may sit anywhere in its section, not only in the heading |
| Export reveals the marks | HTML or PDF export of a marked talk shows `{pause 2s}` to readers | Check in step 1a what export does with them; either strip speech marks in exports when Speech mode is on, or warn; decided in 1a with the owner |
| Editor redraws paragraphs | Page elements are lost between a click and a dialog (met in the review work) | Remember the paragraph id and text offsets, never elements; same code pattern as the review script |
| Caret behaviour near marks | Backspace/Delete across a hidden mark; selection that cuts a mark | Same hide/show-when-caret-inside behaviour as the review marks; the palette refuses unsafe selections; tested in the real app |
| Spell check, search, word count | `pause`, `emphasis` flagged in a Polish text; Find matching inside marks; the status-bar word count including marks | Mark text is excluded from spell check; Find finds inside marks (accepted, as for review marks); the status-bar count is checked in 1a and corrected if it counts mark words |
| Wrong time estimates | The speaker trusts an estimate that is 20 % off | The panel says "estimate", asks for the speaker's own baseline, and step 3 measures it |
| Accessibility | Colour only, small chips, mouse only | Section 5.2; keyboard-complete palette; text labels and tooltips; reduced motion honoured in step 2; high-contrast themes checked |
| Localisation | A string missing in one language; a unit or decimal formatted differently | All four `.resw` files in the same PR; `LocalizationTests`; the file format itself is language-neutral (section 3.5). Known: numbers in the UI follow Windows regional settings |
| Performance | Re-timing a long talk on every key | Debounced, one pass over the text, off when Speech mode is off |
| Privacy wording | A feature named "speech" read as recording | No microphone icon, no audio code; the privacy text gets one sentence saying so when step 1 ships |
| Scope growth | The idea has many attractive extras | The phases above; anything else goes to the owner first |

## 9. Decisions for the owner

1. **Syntax:** curly braces with a word, the vocabulary in section 3.3 (small fixed set; tone and cue as free text).
2. **Speech mode:** off by default, manual switch, nothing automatic (section 5.1).
3. **Phase 1:** PRs 1a to 1d as in section 6, with Copy for AI included.
4. **Defaults:** 130 words per minute; headings not spoken; amber at 100 to 110 %.
5. **Export:** how HTML/PDF export treats the marks (decided with the owner in 1a, section 8).
