# Reading native XML files in Caret: design

Status: 2026-10-27. The first slice is built (this document and `Services/Conversion/XmlConverter.cs`, `XmlUbl.cs`, `XmlLabels.cs`); everything after it is a proposal for the owner to confirm.

## 1. What the owner decided

- **Native XML only.** The XML that authorities and ERP systems produce (electronic invoices, tax returns, bank files). Not Word, Excel or PowerPoint files: those have their own converters and stay as they are.
- **Colombia first** (the DIAN electronic invoice), then Mexico (CFDI), Poland (KSeF invoices, JPK), Brazil (NF-e), Peru (SUNAT, on the UBL pack) and Chile (DTE of the SII). The user base in Colombia and Mexico is the reason for the order.
- **Humans first.** The goal is that a person can read what a machine-made XML file says, in the way Caret already lets people read and write Markdown. Conversion between formats comes later.
- No contacts with accountants in Colombia yet: nothing here has been seen by a real user.

## 2. The idea in one paragraph

An XML file is read into a Markdown document: a **summary** that a person can read (who issued it, to whom, the lines, the taxes, the totals, the answer of the tax authority), followed by **every field of the file** in order, so nothing is hidden. The Markdown opens in Caret like any note: it can be read, searched, copied, edited, saved, or pasted into an AI assistant (with far fewer tokens than the XML). This needs no new editor: the Convert page, the conversion tray and the folder conversion already exist, and `.xml` is now one more kind of file they accept.

## 3. Principles (they decide the hard cases)

1. **A code is explained only when it is certain.** Otherwise it is printed as it is. A wrong label on a fiscal document is worse than no label. The reader has a short list of codes that were checked (and `XmlLabels.cs` says so); the full tables are added from the authority's own files (section 6).
2. **Nothing is hidden.** The summary is a lens; the list of all fields follows it, and an XML document stored as text inside another (the DIAN container stores the invoice in a CDATA part) is read and its fields are listed too. Digital signatures are named, not printed.
3. **Nothing is reformatted.** Amounts, dates and numbers are printed as the file has them, so the result never depends on the PC's regional settings (tested in three cultures).
4. **The reader does not judge.** It does not say whether an invoice is valid, accepted or cancelled. It shows what the file says, including the answer the authority wrote. Whether a document is valid is the authority's and the user's accounting system's work.
5. **Safe by construction.** DTDs are prohibited and no external resource is resolved, so a file from outside cannot make Caret read another file or call out (tested). A file that is not well-formed is refused with the line and position of the error. A file over 60 MB is refused for now (section 7).
6. **Offline, no AI.** Rules only, as everywhere in Caret.

## 4. How it is built

```
.xml file ──► XmlConverter (parse safely)
                │
                ├─ a format pack knows the root and namespace ──► summary (XmlUbl.cs: UBL 2.1 as the DIAN defines it)
                │                                                  + "All fields of the file"
                └─ no pack knows it ──► generic outline (nested list; repeated records become a table)
```

- A **format pack** is a renderer chosen by the root element and its namespace (an `Invoice` that is not in the UBL namespace is not taken for one). The UBL pack reads the structure that Colombia, Peru and other countries use; the Colombian words and codes are added only where the codes are Colombian.
- **Words** are in `XmlLabels` (English and Spanish; another language falls back to English until it has its own set). The language is the interface language (`ConversionOptions.Language`).
- The code is plain .NET (no WinUI), tested with `Dev/Caret.ConverterTests`: three synthetic samples in `samples/` (the invoice inside its container, a credit note, an unknown XML file), each with the expected Markdown, plus tests for malformed files, the DTD attack, the unknown code and the regional settings.

## 5. The Colombian pack: what it reads

Source: Resolución 000165 of 1 November 2023, **Anexo técnico de factura electrónica de venta, version 1.9** (635 pages; the DIAN's own PDF is at `dian.gov.co`, a copy is at `cdn.actualicese.com`). Used so far:

| Part of the annex | What the reader does with it |
|---|---|
| 6.4 `AttachedDocument` (container) | Reads the container: references, sender and receiver, **the invoice or event stored as text in `Attachment/ExternalReference/Description`**, and for each referenced document the identifier, the CUFE/CUDE, the `ApplicationResponse` (the DIAN's answer, also stored as text) and the `ResultOfVerification` |
| `Invoice`, `CreditNote`, `DebitNote` (UBL 2.1) | Number, type, dates, currency, CUFE/CUDE and its algorithm name, environment (1 production, 2 test), notes, issuer and customer (name, tax ID with check digit and its kind, address, contact, fiscal responsibilities), form of payment (1 cash, 2 credit), lines, taxes and withholdings by scheme, totals; for notes also the invoice they correct and the reason |
| `ApplicationResponse` | Date, sender, result code and the text the authority wrote, and the table of rule messages |
| 11.1.2 CUFE | Not computed. The CUFE is SHA-384 of a concatenation of the invoice's fields (number, date, amounts, taxes, both tax IDs) and the *technical key of the invoicing range* (`ClTec`) and the environment. The annex does not put that key in the invoice, so the CUFE cannot be recomputed from the file alone (to be confirmed with a real file). It is shown, not checked |

Codes explained today: invoice types 01, 02, 03 (issuer contingency), 04 (DIAN contingency); tax schemes 01 to 07 and ZZ; identification kinds 13, 22, 31, 41, 42; form of payment 1 and 2; environment 1 and 2. The tax scheme's own name in the file is shown first.

**What is missing and cannot be guessed:** the annex says its code tables (document types, operation types, fiscal responsibilities, payment means, units, municipalities, taxes and their rates) are in the DIAN's **"Caja de herramientas"** (`Caja_de_herramientas_Factura_Electronica_Validacion_Previa.zip`, folder *Anexo Tecnico / Tablas Referenciadas*, Excel files). They are not in the PDF. To add them: the owner downloads that file from the DIAN's electronic invoicing site (`micrositios.dian.gov.co`), and the tables are turned into a data file with a small script that records the source file and its date, so the reader's labels have a provenance and can be refreshed when the DIAN publishes a new version.

## 6. Proposed next slices (to confirm)

1. **Code tables from the DIAN toolbox** (needs the download above), including the 17-odd tax schemes, payment means, units, fiscal responsibilities and operation types. Then payroll (`NominaIndividual`), the *documento soporte* and the RADIAN events (receipt, acceptance).
2. **A folder of invoices as one table** (built: Convert page, card *Invoices to one table*; `InvoiceTable.cs`). Excel, CSV and Markdown with a row per document (date, type, number, issuer and customer with tax IDs, currency, base, taxes, withholdings, totals, CUFE/CUDE, the answer of the DIAN, file). The same unique code twice counts once; files that are not in the table are listed with the reason; totals per currency with credit notes subtracted. Open: a progress bar for very big folders, the same table for the other packs (CFDI, KSeF) when they exist, and a real invoice to compare with (the samples are invented).
3. **Masking before sharing with an AI**: tax IDs (NIT, cédula), names and addresses replaced by placeholders, as for emails (the Latin American ID rules exist).
4. **Opening `.xml` in the editor itself**: the Code view with highlighting, an outline tree beside it, and search; later careful editing of values (text-level edits, so the file's formatting survives; a signed file gets a warning before an edit because it breaks the signature).
5. **Validation against the DIAN's XSD**, with the messages in plain language. Only if the schemas can be shipped and kept current (they change with each annex).
6. **Other packs**: Mexico (CFDI 4.0 and its complements, SAT catalogs), Poland (KSeF FA, JPK), Brazil (NF-e).

## 7. Open questions and risks

- **Which Colombian documents matter first** to the people who would use this (sales invoices received from suppliers, payroll, support documents, events)? The owner has no contacts there yet; the first useful step is to put the synthetic samples and the result in front of one accountant.
- **Scope of `.xml`.** Dropping a *folder* now converts every `.xml` in it (project files, configuration files, sitemaps...). Probably it should become a choice (a switch or "only known formats") before release.
- **Big files.** The file is read as a tree in memory (limit 60 MB). Tax files such as JPK can be much bigger; reading them needs a streaming reader that builds the summary and a bounded outline.
- **Specification changes.** The annex is revised (1.8 to 1.9, with an adoption date that moved). A pack names the version it was read from, and a new version needs the samples and tables to be checked again.
- **Claims.** Caret reads and summarises; it does not certify. Texts in the Store listing must say that, and must not promise that a document is valid.
- **Languages.** The words exist in English and Spanish; French, Polish and Portuguese follow with the packs that need them (Poland, Brazil).
- **Samples are synthetic.** Never put real invoices, tax returns or any real person's data in the repository or in `samples/`. The annex's own examples and invented data are the source.
