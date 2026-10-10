---
title: Showcase of the Word export
---

# Showcase of the Word export

This document holds every construct the first phase of the Word export writes. It is read by the tests and, once per phase, opened in the real Word and looked at.

## Text

A paragraph with **bold**, *italic*, ***both***, ~~struck~~, `inline code`, H~2~O, x^2^, <u>underlined</u> text and a [link to a page](https://example.com/page). A bare address: https://example.org/path and a mail <ana@example.com>.

A hard break after this line  
goes on in the same paragraph. A single line break
in the source is a space.

Accents and scripts: Zażółć gęślą jaźń (Polish), ça va à l’été, où sont les œufs ? (French), ¿Qué tal, señor? ¡Muy bien! (Spanish), coração e ação (Portuguese).

## Lists

- First bullet
- Second bullet with **bold**
    - Nested bullet
    - Another nested one
        - Third level
- Back at the first level

1. First step
2. Second step
    1. A sub step
    2. Another sub step
3. Third step

- [x] A task that is done
- [ ] A task still open

An item with two paragraphs:

- The first paragraph of the item.

  The second paragraph, lined up under the text.
- The next item.

## Quote and code

> A quotation that is long enough to show how the bar in the margin and the gray text look in Word, with a second line that goes on.
>
> > A quote inside the quote moves in once more.

```csharp
public static int Add(int a, int b)
{
    return a + b;   // one paragraph per line
}
```

## Table

| Product | Quantity | Price |
| :------ | :------: | ----: |
| Apples | 3 | 1.20 |
| Pears | 12 | 14.40 |
| **Total** | **15** | **15.60** |

---

## Picture

![The track changes screen of Caret](docs/store/screenshots/en/1-track-changes.png)

<img src="docs/store/screenshots/en/2-teleprompter.png" alt="The teleprompter of Caret" style="zoom:40%;">

## Marks left as text

A change {++added++} and {--removed--} and a comment on {==this part==}{>>@Ana 2026-10-10: check this<<}, and a speech mark {pause 2s} in the middle.
