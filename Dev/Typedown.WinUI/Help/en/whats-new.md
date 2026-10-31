# What's new in Caret

The main changes, newest first. Every feature has its own topic in this menu; this page only tells you where to look.

## Version 2.10: scans, screenshots and a clearer sidebar

- **Edit** > **Paste text from picture (OCR)** reads a screenshot or a picture on the clipboard (Win+Shift+S makes one) with the OCR of Windows, on your PC, and pastes its text at the caret. With no document on screen the text opens as a new note; if you change document while the picture is being read, the text is copied to the clipboard instead.
- On the **Documents and scans** page, switch on **Read pictures in folders too (OCR)** and a folder you pick or drop gives its pictures (PNG, JPG, BMP, TIFF) to the OCR as well as its documents. It is off at first, and a folder with 30 pictures or more asks before it starts. The OCR options now come first on the page, and the line under the drop zone names scans and pictures.
- The sidebar is in three groups: **Convert** (**Documents and scans**, which was called *Convert to Markdown*, **Outlook emails** and **Invoices to one table**), **This document** (**Review**, **Speech marks** and, for an XML file, **XML tools**) and **Library**. **Mask personal data...** is also in the **Review** panel, and the card at the bottom shows a tip.

## Version 2.9: Brazil and Portugal, XML tools, scans

- More electronic documents in plain words: Brazil's national NFS-e (service invoice), CT-e (transport document) and MDF-e (manifest), with their events, and Portugal's SAF-T (PT) audit file, with checks of the totals and the taxes. A folder of invoices (*Invoices to one table*) can mix them: a CT-e replaced by a CT-e of substitution is listed but not added to the totals. See the topic *Convert to Markdown*.
- XML files: the structure in the *Outline* list and on the document map; a piece of XML with several elements side by side is read and formatted as a fragment; **Edit** > **Paste as XML** puts the clipboard into the note as a code block and opens it as an XML document; a click on a value of the readable version selects it in the code.
- **Edit** > **Mask personal data...** hides email addresses, phone numbers, IBANs, card numbers, ID numbers and the names you list, in any note or XML file: in a copy, or in the note itself (one Undo brings it back).
- PDF: convert only some pages (the *PDF pages* box of the Convert page); read scans and pictures of pages with the OCR of Windows, on your PC; and add a watermark, a header, a footer and page numbers when you export (**File** > **Export** > **PDF...**).

## Version 2.7: XML files, in plain words

- Read an XML file like a document. Open it, drop it on the window or choose it on the Convert page. Caret knows the electronic invoices of Colombia (DIAN), Mexico (CFDI), Brazil (NF-e and NFC-e), Peru (SUNAT) and Chile (DTE) and other UBL invoices, and explains their codes in words (in English, Spanish or Portuguese). Any other XML becomes an outline. See the topic *Convert to Markdown*.
- A folder of invoices as one table (Convert page, *Invoices to one table*): Excel, CSV or Markdown, with credit notes subtracted and duplicates counted once.
- Edit XML files. They open as code with the readable version beside them. A strip says in plain words whether the XML is well-formed and where it is not; **Edit** > **Format XML** tidies the white space only; **Edit** > **Check against schema...** compares the file with an XSD. The file is saved the way it was written. See *Editing XML files* in the same topic.
- **Go to File** (`Ctrl+K`) lists XML files, and Windows offers Caret in *Open with* for .xml, .xsd, .xsl and .xslt files.
- Pictures travel with the note: **File** > **Export** > **Markdown with pictures (.zip)...**, collect the pictures next to the note, and **Paragraph** > **Chart from table...**

## Version 2.6: the window comes to you

- **Command Palette** (`Ctrl+Shift+P`, **View** > **Command Palette**): type a few letters of what you want to do, a command, a setting or a note, and press Enter.
- **Inspector pane** (`Ctrl+Shift+I`, **View** > **Inspector pane**): a column beside the note with its outline, its review marks, the timing of the talk, every match of the search, and the **History** of the note, the earlier saved versions that you can compare and restore. The copies of saved notes stay on your PC.
- **Document map** (**View** > **Document map**): a thin strip along the right edge that shows where in the note you are and where the headings, comments and changes are.
- A status bar that says what matters: when the file was saved, words and reading time, the selection, the review marks. A segment with nothing to say is not shown.
- A small toolbar over the selected words, with the common formats, and a button that does **Copy as WhatsApp text**.
- Drop files anywhere on the window (Word, PDF, Excel, PowerPoint, CSV, emails or a folder) to convert them to Markdown. A tray shows the jobs, with Open, Show in folder, Compare and Retry.
- Appearance cards (**Settings** > **Appearance**): see the colour scheme in a live preview, find what you changed, and go back to the scheme in one click.
- Keyboard first: press `Alt` to see which key reaches each button, and `Ctrl+Shift+/` for the list of every shortcut (**Keyboard shortcuts** in the Help menu).
- Home as a working page: the files you were working on, what you can start, and your folders, in three columns.
- Fixes: the colours of the whole window follow a change of theme at once, and the key tips go away when the window scrolls.

## Version 2.5: send it as a Word file

**File** > **Export** > **Word Document (.docx)** writes a real Word file on your PC, with headings, lists, tables, pictures, footnotes, a table of contents, tracked changes and comments. Word does not have to be installed. See the topic *Export to Word*.

## Version 2.0: write it, track it, say it

- **Track changes** while you edit (**Review**), comments, accept or reject, and compare a file with an earlier version.
- **Speech marks** and the **Teleprompter** for talks, with a clock and a rehearsal.
- Spell check, **Copy as WhatsApp text**, a Help menu, and Brazilian Portuguese, Polish and Spanish for every Spanish-speaking country.

## Version 1.6: files from Explorer

Right-click Convert to Markdown in File Explorer, a tab can be dropped on another Caret window, a right-click menu in the editor, and much better conversion of real PDF, Word, Excel, PowerPoint and email files.

## Version 1.5

A tab can be dragged out into its own window, and the sidebar scrolls and collapses. Masking of personal data in emails also finds the names in greetings and sign-offs.

## Version 1.4: tabs and a new look

Several documents in one window, a Settings page, colour schemes, layouts and better contrast.

## Version 1.3: Outlook emails

**Outlook emails** become clean Markdown, one file for a whole conversation, with the personal data masked if you want it.

## Version 1.2: Convert to Markdown

**Convert to Markdown** reads Word, PDF, Excel and PowerPoint files, French and Spanish were added, and accented letters are right in files that are not UTF-8.

## Version 1.0

The editor itself: a Markdown editor that shows the text as it will look, with tables, links and pictures, favourites and recent files, and nothing uploaded anywhere.
