# Electronic credit note NCSE990000001

> File: co-nota-credito.xml
> UBL 2.1 · DIAN 2.1: Nota Crédito de Factura Electrónica de Venta

- **Issue date:** 2026-03-10 09:00:00-05:00
- **Currency:** COP
- **Unique code (CUFE / CUDE):** fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210 (CUDE-SHA384)
- **Environment:** Test

- **Related invoice:** SETP990000001 · 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
- **Reason:** 2: Anulación de la factura electrónica (texto de ejemplo)

## Issuer

- **Name:** Comercializadora Ejemplo S.A.S.
- **Tax ID:** 900000001-1 (31)

## Customer

- **Name:** Persona de Prueba
- **Tax ID:** 1000000000 (13)

## Lines

| # | Description | Quantity | Unit | Unit price | Line total |
| --- | --- | --- | --- | --- | --- |
| 1 | Devolución de una silla de oficina | 1.000000 | EA | 100000.00 COP | 100000.00 COP |

## Taxes

| Tax | Taxable amount | Rate | Amount |
| --- | --- | --- | --- |
| 01 · IVA | 100000.00 COP | 19.00 % | 19000.00 COP |

## Totals

- **Total before taxes:** 100000.00 COP
- **Taxable base:** 100000.00 COP
- **Total with taxes:** 119000.00 COP
- **Total payable:** 119000.00 COP

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **CreditNote**
  - **UBLVersionID**: UBL 2.1
  - **CustomizationID**: 20
  - **ProfileID**: DIAN 2.1: Nota Crédito de Factura Electrónica de Venta
  - **ProfileExecutionID**: 2
  - **ID**: NCSE990000001
  - **UUID** (schemeID=2, schemeName=CUDE-SHA384): fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210
  - **IssueDate**: 2026-03-10
  - **IssueTime**: 09:00:00-05:00
  - **CreditNoteTypeCode**: 91
  - **DocumentCurrencyCode**: COP
  - **LineCountNumeric**: 1
  - **DiscrepancyResponse**
    - **ReferenceID**: SETP990000001
    - **ResponseCode**: 2
    - **Description**: Anulación de la factura electrónica (texto de ejemplo)
  - **BillingReference**
    - **InvoiceDocumentReference**
      - **ID**: SETP990000001
      - **UUID** (schemeName=CUFE-SHA384): 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
      - **IssueDate**: 2026-03-04
  - **AccountingSupplierParty**
    - **Party**
      - **PartyTaxScheme**
        - **RegistrationName**: Comercializadora Ejemplo S.A.S.
        - **CompanyID** (schemeID=1, schemeName=31): 900000001
        - **TaxScheme**
          - **ID**: 01
          - **Name**: IVA
  - **AccountingCustomerParty**
    - **Party**
      - **PartyTaxScheme**
        - **RegistrationName**: Persona de Prueba
        - **CompanyID** (schemeName=13): 1000000000
        - **TaxScheme**
          - **ID**: ZZ
          - **Name**: No aplica
  - **TaxTotal**
    - **TaxAmount** (currencyID=COP): 19000.00
    - **TaxSubtotal**
      - **TaxableAmount** (currencyID=COP): 100000.00
      - **TaxAmount** (currencyID=COP): 19000.00
      - **TaxCategory**
        - **Percent**: 19.00
        - **TaxScheme**
          - **ID**: 01
          - **Name**: IVA
  - **LegalMonetaryTotal**
    - **LineExtensionAmount** (currencyID=COP): 100000.00
    - **TaxExclusiveAmount** (currencyID=COP): 100000.00
    - **TaxInclusiveAmount** (currencyID=COP): 119000.00
    - **PayableAmount** (currencyID=COP): 119000.00
  - **CreditNoteLine**
    - **ID**: 1
    - **CreditedQuantity** (unitCode=EA): 1.000000
    - **LineExtensionAmount** (currencyID=COP): 100000.00
    - **Item**
      - **Description**: Devolución de una silla de oficina
    - **Price**
      - **PriceAmount** (currencyID=COP): 100000.00
      - **BaseQuantity** (unitCode=EA): 1.000000
