import { parseCallout, decorateCallouts, isCalloutKind } from './callout'

describe('parseCallout', () => {
  it('reads the five kinds of GitHub, in any case, with or without a title', () => {
    expect(parseCallout('[!NOTE]')).toEqual({ kind: 'note', title: '' })
    expect(parseCallout('[!tip]')).toEqual({ kind: 'tip', title: '' })
    expect(parseCallout('[!Important]')).toEqual({ kind: 'important', title: '' })
    expect(parseCallout('[!WARNING] Breaking change')).toEqual({ kind: 'warning', title: 'Breaking change' })
    expect(parseCallout('[!CAUTION]\nthe rest of the paragraph')).toEqual({ kind: 'caution', title: '' })
  })

  it('reads the names of Obsidian as the kind they mean, and its fold markers', () => {
    expect(parseCallout('[!info]').kind).toBe('note')
    expect(parseCallout('[!success]').kind).toBe('tip')
    expect(parseCallout('[!danger]').kind).toBe('caution')
    expect(parseCallout('[!bug]- Known issue')).toEqual({ kind: 'caution', title: 'Known issue' })
  })

  it('is not a callout when the line is not a marker of a known kind', () => {
    expect(parseCallout('A quote')).toBeNull()
    expect(parseCallout('[!SOMETHING]')).toBeNull()
    expect(parseCallout('see [!NOTE] later')).toBeNull()
    expect(parseCallout('[NOTE]')).toBeNull()
    expect(parseCallout(null)).toBeNull()
    expect(parseCallout('[!constructor]')).toBeNull()
    expect(parseCallout('[!toString]')).toBeNull()
  })

  it('knows only the five kinds as kinds', () => {
    expect(isCalloutKind('warning')).toBe(true)
    expect(isCalloutKind('constructor')).toBe(false)
    expect(isCalloutKind(undefined)).toBe(false)
  })
})

describe('decorateCallouts', () => {
  const render = html => {
    const container = document.createElement('div')
    container.innerHTML = html
    decorateCallouts(container)
    return container
  }

  it('turns the marker line into a title and keeps the rest of the paragraph', () => {
    const box = render('<blockquote>\n<p>[!WARNING]\nThe cache is cleared.</p>\n</blockquote>').querySelector('blockquote')
    expect(box.className).toBe('markdown-alert markdown-alert-warning')
    expect(box.querySelector('.markdown-alert-title').textContent).toBe('Warning')
    expect(box.querySelectorAll('p')[1].textContent).toBe('The cache is cleared.')
  })

  it('uses the title written after the marker, and drops a paragraph that only held the marker', () => {
    const box = render('<blockquote><p>[!tip] Nota bene</p><p>Body</p></blockquote>').querySelector('blockquote')
    expect(box.querySelector('.markdown-alert-title').textContent).toBe('Nota bene')
    expect([...box.querySelectorAll('p')].map(p => p.textContent)).toEqual(['Nota bene', 'Body'])
  })

  it('keeps the formatting of the title, across its inline elements', () => {
    const box = render('<blockquote><p>[!NOTE] A <strong>bold</strong> title\nBody</p></blockquote>').querySelector('blockquote')
    const title = box.querySelector('.markdown-alert-title')
    expect(title.textContent).toBe('A bold title')
    expect(title.querySelector('strong').textContent).toBe('bold')
    expect(box.querySelectorAll('p')[1].textContent).toBe('Body')
  })

  it('ends the title at a line break inside an element, which stays whole in the body', () => {
    const box = render('<blockquote><p>[!NOTE] <strong>Heading\nBody</strong></p></blockquote>').querySelector('blockquote')
    expect(box.querySelector('.markdown-alert-title').textContent).toBe('Note')
    expect(box.querySelectorAll('p')[1].textContent).toBe('Heading\nBody')
  })

  it('does not read a marker in code, nor a name every object has', () => {
    const container = render('<blockquote><p><code>[!NOTE]</code> text</p></blockquote><blockquote><p>[!constructor]</p></blockquote>')
    for (const quote of container.querySelectorAll('blockquote')) expect(quote.className).toBe('')
  })

  it('leaves an ordinary quote alone', () => {
    const container = render('<blockquote><p>Just a quote</p></blockquote>')
    expect(container.querySelector('blockquote').className).toBe('')
    expect(container.querySelector('.markdown-alert-title')).toBeNull()
  })
})
