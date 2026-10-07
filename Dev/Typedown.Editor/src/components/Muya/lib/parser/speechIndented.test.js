// config and utils import each other; loading config first is the order the app gets from its bundler
import '../config'
import { proseLines, collectDocumentDefinitions, stripMarkdown } from './speech'

const prose = text => proseLines(text.split('\n'))

describe('indented code is not prose', () => {
  test('four spaces after a blank line open a code block that goes on while lines are indented', () => {
    expect(prose('text\n\n    {pause} code\n    more\n\nnext')).toEqual([true, true, false, false, true, true])
  })

  test('a tab does the same, and a code block may hold blank lines', () => {
    expect(prose('text\n\n\tcode\n\n\tmore code\n\nnext')).toEqual([true, true, false, true, false, true, true])
  })

  test('at the very start of a document too', () => {
    expect(prose('    code first\ntext')).toEqual([false, true])
  })

  test('an indented line that goes on with a paragraph is part of it', () => {
    expect(prose('a paragraph\n    carried on here\nmore')).toEqual([true, true, true])
  })

  test('under a list item an indented line is its own text, not code', () => {
    expect(prose('- item\n\n    continued {pause} here\n\nnext paragraph')).toEqual([true, true, true, true, true])
    expect(prose('1. item\n\n    more\n\n    and more')).toEqual([true, true, true, true, true])
  })

  test('after the list, a new paragraph and then code is code again', () => {
    expect(prose('- item\n\nplain paragraph\n\n    real code')).toEqual([true, true, true, true, false])
  })

  test('an indented line after a fenced block is code only when it follows a blank line', () => {
    expect(prose('```\nx\n```\n\n    code')).toEqual([false, false, false, true, false])
  })

  test('a line with three spaces is still prose', () => {
    expect(prose('text\n\n   not code')).toEqual([true, true, true])
  })
})

describe('definitions only from prose', () => {
  test('an example in a fenced block or in indented code is not a definition of the document', () => {
    const doc = ['```', '{define whisper span: example}', '```', '', '    {define sotto span: also an example}', '', '{define wave note: real}'].join('\n')
    expect([...collectDocumentDefinitions(doc).defs.keys()]).toEqual(['wave'])
  })

  test('with Windows line endings too', () => {
    expect([...collectDocumentDefinitions('{define wave note: real}\r\n\r\ntext').defs.keys()]).toEqual(['wave'])
  })
})

describe('Remove all speech marks leaves indented code alone', () => {
  test('marks in prose go, marks in indented code stay', () => {
    expect(stripMarkdown('Good {pause} evening.\n\n    {pause} stays\n\nEnd {beat}.')).toBe('Good evening.\n\n    {pause} stays\n\nEnd.')
  })
})

describe('the space before a mark that is followed by punctuation goes with it', () => {
  test.each([
    ['End {beat}.', 'End.'],
    ['Wait {pause 2s}, then go', 'Wait, then go'],
    ['Really {slow}so{/slow}? Yes', 'Really so? Yes'],
    ['one {pause} two', 'one two']
  ])('%s', (text, expected) => {
    expect(stripMarkdown(text)).toBe(expected)
  })
})
