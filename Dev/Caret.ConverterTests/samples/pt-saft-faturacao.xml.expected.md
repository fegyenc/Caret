# SAF-T (PT) · Empresa de Exemplo, Lda. · 2026

> File: pt-saft-faturacao.xml · 1.04_01 · F — Invoicing

## The company

- **Name:** Empresa de Exemplo, Lda.
- **Business name:** Exemplo
- **NIF:** 500000018
- **Address:** Rua de Teste 1, 1000-001 Lisboa, PT
- **Email:** geral@exemplo.test

## The file

- **Kind of file:** F — Invoicing
- **SAF-T version:** 1.04_01
- **Fiscal year:** 2026
- **Period:** 2026-02-01 → 2026-02-28
- **Created:** 2026-03-03
- **Currency:** EUR
- **Tax entity:** Global
- **Software:** Faturador Exemplo/Software de Teste, Lda. 1.0
- **Software certificate number:** 0000
- **Software producer NIF:** 500000034

## What the file contains

| What | How many |
| --- | --- |
| Customers | 3 |
| Suppliers | 1 |
| Products and services | 3 |
| Tax table entries | 3 |
| Sales documents | 7 |

## Sales documents

| Type | Documents | Voided | Total before VAT | VAT | Total with VAT |
| --- | --- | --- | --- | --- | --- |
| FS — Simplified invoice | 2 | 1 | 10.00 | 2.30 | 12.30 |
| FT — Invoice | 4 |  | 440.00 | 71.40 | 513.40 |
| NC — Credit note | 1 |  | 50.00 | 11.50 | 61.50 |
| Total (credit notes and returns subtracted, voided left out) | 7 | 1 | 400.00 | 62.20 | 464.20 |

- **Documents to the final consumer (NIF 999999990):** 1 · 12.30

### By VAT rate

| Tax | Rate | Base (credit notes subtracted) | Calculated tax |
| --- | --- | --- | --- |
| IVA PT NOR | 23 % | 260.00 | 59.80 |
| IVA PT ISE | 0 % | 100.00 | 0.00 |
| IVA PT RED | 6 % | 40.00 | 2.40 |

The tax is the base times the rate, calculated from the lines; the totals of the documents can differ by rounding.

### Biggest customers

| Customer | NIF | Documents | Total with VAT |
| --- | --- | --- | --- |
| Cliente Exemplo, S.A. | 500000026 | 3 | 226.90 |
| C009 |  | 1 | 125.00 |
| Cliente Inventado, Unipessoal Lda. | 200000013 | 1 | 100.00 |
| Consumidor final | 999999990 | 1 | 12.30 |

## Things to look at

- Customers with a Portuguese address and an invalid NIF check digit (1): 200000013 (Cliente Inventado, Unipessoal Lda.).
- The header of the sales documents says 8 entries; the file has 7 documents (the header counts every one, the voided ones too).
- Documents whose total with VAT is not the total before VAT plus the VAT (1): FT 2026A/5: 125.00 ≠ 100.00 + 23.00.
- Documents dated outside the period of the file (2026-02-01 to 2026-02-28) (1): FT 2026A/5 (2026-03-02).
- Numbers missing in the numbering of a series (1): FT 2026A: 3.
- Documents dated 2023 or later with no ATCUD, or an ATCUD of 0 (1): the first is FT 2026A/5.
- Customer codes used in documents but not in the customer list (1): C009.
- Lines with 0 % VAT and no exemption code or reason (1).

What is checked: the counts and totals in the headers against the lines, the total of each sales document that is not voided, the dates against the period, the check digit of Portuguese tax numbers, gaps and repeats in the numbering, the ATCUD of sales documents dated 2023 or later, the customers against the customer list, 0 % VAT lines without an exemption code or reason, the currency, and the balance of ledger entries. These are simple rules, not the validator of the Autoridade Tributária: a file with nothing here can still be refused, and the schema (XSD) is checked with Edit > Check against schema.
