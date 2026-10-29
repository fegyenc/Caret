import { findSpans, pickPlace, resolveJump, targetOf } from './xmlJump'

const source = [
  '<order id="A1">',
  '  <line><sku>X-1</sku><qty>1</qty></line>',
  '  <line><sku>Y-9</sku><qty>1</qty><note>Fish &amp; chips</note></line>',
  '</order>',
  '<order id="B2"><line><sku>X-1</sku><qty>10</qty></line></order>',
].join('\n')

const text = (span) => source.slice(span.from, span.to)

describe('finding the value of the readable view in the code', () => {
  test('the whole content of an element, not a part of a longer value', () => {
    const spans = findSpans(source, { kind: 'value', text: '1' })
    expect(spans.map(text)).toEqual(['1', '1'])   // the two <qty>1</qty>, not the 1 of X-1, A1 or 10
    expect(findSpans(source, { kind: 'value', text: '10' }).map(text)).toEqual(['10'])
  })

  test('an attribute value, and the characters that the source writes as entities', () => {
    expect(findSpans(source, { kind: 'value', text: 'A1' }).map(text)).toEqual(['A1'])
    expect(findSpans(source, { kind: 'value', text: 'Fish & chips' }).map(text)).toEqual(['Fish &amp; chips'])
  })

  test('a part of a value is found as a word of its own, never inside a tag name', () => {
    expect(findSpans('<date>2026-10-20T10:30:00-03:00</date>', { kind: 'value', text: '2026-10-20' }).map((s) => s.to - s.from)).toEqual([10])
    expect(findSpans('<sku>X-1</sku><a>3</a>', { kind: 'value', text: 'sku' })).toEqual([])
    expect(findSpans('<a>X-12</a>', { kind: 'value', text: 'X-1' })).toEqual([])
  })

  test('a CDATA section holds & and < as they are', () => {
    expect(findSpans('<m><![CDATA[Fish & chips <b>]]></m>', { kind: 'value', text: 'Fish & chips <b>' })).toHaveLength(1)
    expect(findSpans('<m>Fish &amp; chips</m>', { kind: 'value', text: 'Fish & chips' })).toHaveLength(1)
  })

  test('white space in the view is any white space in the source', () => {
    expect(findSpans('<m>one\n   two</m>', { kind: 'value', text: 'one two' })).toHaveLength(1)
  })

  test('a name is a start tag, with or without a prefix', () => {
    expect(findSpans(source, { kind: 'name', text: 'line' })).toHaveLength(3)
    expect(findSpans('<ns:order a="1"/><orders/>', { kind: 'name', text: 'order' }).map((s) => s.from)).toEqual([4])
  })

  test('text that is not in the source is not found, and the regular expression characters of the text are not special', () => {
    expect(findSpans(source, { kind: 'value', text: 'Z-0' })).toEqual([])
    expect(findSpans('<a>1+1</a><b>111</b>', { kind: 'value', text: '1+1' })).toHaveLength(1)
    expect(findSpans(source, { kind: 'value', text: '' })).toEqual([])
  })
})

describe('which of the places an item goes to', () => {
  test('the k-th of the items goes to the k-th of the places', () => {
    expect(pickPlace(1, 2, 2)).toBe(0)
    expect(pickPlace(2, 2, 2)).toBe(1)
    expect(pickPlace(3, 3, 5)).toBe(2)
  })

  test('a summary that repeats what the list of all fields says: the places are used again in turn', () => {
    // 2 places in the source, listed twice in the view (4 items): items 3 and 4 are the list of all the fields
    expect(pickPlace(3, 4, 2)).toBe(0)
    expect(pickPlace(4, 4, 2)).toBe(1)
    expect(pickPlace(2, 2, 1)).toBe(0)
  })

  test('nothing to go to', () => {
    expect(pickPlace(1, 1, 0)).toBe(-1)
    expect(resolveJump(source, { kind: 'value', text: 'nope' }, 1, 1)).toBeNull()
  })

  test('the second X-1 of the view is the second X-1 of the code', () => {
    const span = resolveJump(source, { kind: 'value', text: 'X-1' }, 2, 2)
    expect(source.slice(0, span.from).split('\n').length).toBe(5)
  })
})

describe('the target of an element of the readable view', () => {
  test('a table cell is a value', () => {
    expect(targetOf('td', ' Pedagio ', '')).toEqual({ kind: 'value', text: 'Pedagio' })
  })

  test('"name: value" in a list is the value; a name with something after it is an element that holds others', () => {
    expect(targetOf('li', 'sku: X-1', 'sku')).toEqual({ kind: 'value', text: 'X-1' })
    expect(targetOf('li', 'order (id=A1)', 'order')).toEqual({ kind: 'name', text: 'order' })
    expect(targetOf('li', 'plain words', '')).toEqual({ kind: 'value', text: 'plain words' })
  })

  test('the colon inside the bold word (the summaries of the formats) is the same as after it', () => {
    expect(targetOf('li', 'Number: 456', 'Number:')).toEqual({ kind: 'value', text: '456' })
    expect(targetOf('li', 'Type: 0 — CT-e Normal', 'Type:')).toEqual({ kind: 'value', text: '0 — CT-e Normal' })
  })

  test('a code printed with its name is looked for alone when it is written once, and not at all when it is written all over the file', () => {
    const cte = '<ide><tpCTe>3</tpCTe><tpEmis>1</tpEmis><tpImp>1</tpImp></ide>'
    const unique = resolveJump(cte, { kind: 'value', text: '3 — Destinatário' }, 1, 1)
    expect(cte.slice(unique.from, unique.to)).toBe('3')
    expect(resolveJump(cte, { kind: 'value', text: '1 — Normal' }, 1, 1)).toBeNull()
  })

  test('a heading is not a target (its text is a label of the view), and an empty element has none either', () => {
    expect(targetOf('h1', 'order', '')).toBeNull()
    expect(targetOf('li', '   ', '')).toBeNull()
    expect(targetOf('li', 'x '.repeat(200), '')).toBeNull()   // an item that holds a whole table
  })
})
