# Converter tests

Regression tests for Caret's document converters. The converters are plain .NET (no WinUI), so this project
compiles their source files (`Dev/Typedown.WinUI/Services/Conversion`) as they are and runs them with xunit.

```
dotnet test Dev/Caret.ConverterTests
```

It needs only the .NET 8 SDK, and runs in a second. GitHub runs it (and the email plugin's Python tests) on every
pull request and every push to `main` (`.github/workflows/tests.yml`).

## The samples

`samples/` holds made-up documents: `word-report.docx`, `word-french.docx`, `excel-budget.xlsx`,
`powerpoint-review.pptx`, `pdf-article.pdf`, `pdf-layout.pdf` (the layouts that used to go wrong: a margin stamp, columns above a
full-width block, ruled tables, links, code), `csv-semicolon.csv`, `csv-comma.csv`, `email-thread.eml`,
`email-attachment.eml`, `email-outlook.msg`. Next to each is `<file>.expected.md`, what Caret must produce from it.
Each is converted in English, French and Hungarian regional settings, so a conversion can't depend on the PC's locale.

When a test fails, the difference is either a bug or an intended change:

- **A bug:** fix the converter.
- **An intended change:** run `CARET_UPDATE_SAMPLES=1 dotnet test Dev/Caret.ConverterTests`, then read the diff of
  the `.expected.md` files (`git diff`) and commit them with the change. The diff is what a reviewer looks at.

## Adding a document

Copy it into `samples/` and run once with `CARET_UPDATE_SAMPLES=1`; read the expected Markdown it wrote (that is the
real check: is this what the document should become?) and commit both. **Never add a real document with real names,
numbers or addresses.** Everything in the repository is public.

## Making the sample files

The Office files, the PDF and the `.msg` are written by `SampleFactory.cs` (Open XML SDK, hand-written PDF syntax,
OpenMcdf). The committed files are what the tests read; the factory only runs on purpose, to create a sample or to
give one a new feature:

```
CARET_GENERATE_SAMPLES=1 dotnet test Dev/Caret.ConverterTests --filter Generate_samples
CARET_GENERATE_SAMPLES=word-report.docx dotnet test Dev/Caret.ConverterTests --filter Generate_samples
```

The first writes them all, the second one file. Word, Excel and PowerPoint files are validated against the Open XML
schema as they are written. Regenerating changes the zip bytes of the Office files (timestamps), so regenerate only
what needs it, then update the expected Markdown as above.

## Other tests

`BehaviourTests.cs` covers what isn't the Markdown itself: image extraction and links, slide-heading and notes
options, the `NoData` and `PdfHasNoText` warnings, email redaction and its switches, attachments, damaged files
(they must fail with an error, not hang), a document that is open in Word, and file names with spaces and accents.
