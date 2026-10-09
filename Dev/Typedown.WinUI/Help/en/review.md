# Review: comments and changes

The review function works like the review tools of a word processor, but everything is written into the document as plain text, so a colleague, another editor and an AI assistant can read it too. The marks use a public convention called CriticMarkup:

| You see | Written in the file as |
|---|---|
| green added text | `{++added++}` |
| red deleted text | `{--deleted--}` |
| a replacement | `{~~old~>new~~}` |
| a yellow comment | `{==the text==}{>>@Name 2026-10-07: the note<<}` |

Nothing is stored anywhere else, and the review never starts by itself: typing and deleting do not create marks.

## Add a comment

Select text, right-click, **Add comment** (or **Review > Add comment**). Write the note and check your name (it starts as your Windows user name). The text becomes a yellow highlight with the note beside it. It is typed into the document, so **Undo** works.

## Compare with another file

**Review > Compare with another file...** asks for an earlier version of the document (for example the copy you sent) and who made the changes. Caret writes the differences between that version and the document on screen as a review *in a new tab*; your document is not changed. Use it to see what a colleague changed in the file they returned. If tabs are turned off (**Settings > Tabs and windows**), the review takes the place of your document in the window instead, so save your document first.

## Track changes as you edit

**Review > Track changes** (also a button in the Review panel) remembers the document as it is now. While you edit, a moment after you stop typing, Caret shows what you changed: the right-hand pane of the **Split** view draws the document with additions in green and deletions in red, each with your name and the day, and the Review panel lists the changes and counts them (also in the status bar). Nothing is written into the file. **Stop tracking** forgets the starting version and leaves the document as it is. Accepting and rejecting single changes, the Visual view and writing the changes into the file as a review are the next steps; this topic will describe them as they arrive.

## Accept and reject

- *Right-click a change* (green, red or a replacement): **Accept change** keeps what it says (additions stay, deletions go); **Reject change** puts the old text back.
- *Right-click a comment:* **Delete comment**.
- **Review > Accept all changes**, **Reject all changes** and **Delete all comments** do it for the whole document, as one step of Undo.

## Hide the marks

**Settings > Editor > Show review marks** turns the colours off; the marks then show as the plain text they are.

## Good to know

- The marks are text, so a file with a review can be saved, sent and opened in any editor.
- A returned file with a colleague's marks is read the same way: their changes show in colour and you accept or reject them.