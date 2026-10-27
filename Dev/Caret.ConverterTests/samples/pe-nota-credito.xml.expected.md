# Electronic credit note FC01-7

> File: pe-nota-credito.xml
> 2.1

- **Issue date:** 2026-03-10 09:15:00
- **Currency:** PEN

**Notes**

- CIENTO DIECIOCHO CON 00/100 SOLES (Amount in words)

- **Related invoice:** F001-123 (Electronic invoice)
- **Reason:** 01: Cancellation of the operation · Anulación de la operación

## Issuer

- **Name:** Muestra Proveedora S.A.C.
- **Tax ID:** 20000000001 (RUC)

## Customer

- **Name:** Persona de Ejemplo
- **Tax ID:** 00000001 (DNI)

## Lines

| # | Description | Quantity | Unit | Unit price | Line total |
| --- | --- | --- | --- | --- | --- |
| 1 | Anulación de ejemplo | 1 | NIU | 100.00 PEN | 100.00 PEN |

## Taxes

| Tax | Taxable amount | Rate | Amount |
| --- | --- | --- | --- |
| 1000 · IGV | 100.00 PEN |  | 18.00 PEN |

## Totals

- **Total before taxes:** 100.00 PEN
- **Total payable:** 118.00 PEN

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **CreditNote**
  - **UBLVersionID**: 2.1
  - **CustomizationID**: 2.0
  - **ID**: FC01-7
  - **IssueDate**: 2026-03-10
  - **IssueTime**: 09:15:00
  - **Note** (languageLocaleID=1000): CIENTO DIECIOCHO CON 00/100 SOLES
  - **DocumentCurrencyCode**: PEN
  - **DiscrepancyResponse**
    - **ReferenceID**: F001-123
    - **ResponseCode** (listAgencyName=PE:SUNAT, listName=SUNAT:Codigo de Tipo de Nota de Credito Electronica, listURI=urn:pe:gob:sunat:cpe:see:gem:catalogos:catalogo09): 01
    - **Description**: Anulación de la operación
  - **BillingReference**
    - **InvoiceDocumentReference**
      - **ID**: F001-123
      - **DocumentTypeCode** (listAgencyName=PE:SUNAT, listName=SUNAT:Identificador de Tipo de Documento, listURI=urn:pe:gob:sunat:cpe:see:gem:catalogos:catalogo01): 01
  - **AccountingSupplierParty**
    - **Party**
      - **PartyIdentification**
        - **ID** (schemeID=6, schemeAgencyName=PE:SUNAT): 20000000001
      - **PartyLegalEntity**
        - **RegistrationName**: Muestra Proveedora S.A.C.
  - **AccountingCustomerParty**
    - **Party**
      - **PartyIdentification**
        - **ID** (schemeID=1, schemeAgencyName=PE:SUNAT): 00000001
      - **PartyLegalEntity**
        - **RegistrationName**: Persona de Ejemplo
  - **TaxTotal**
    - **TaxAmount** (currencyID=PEN): 18.00
    - **TaxSubtotal**
      - **TaxableAmount** (currencyID=PEN): 100.00
      - **TaxAmount** (currencyID=PEN): 18.00
      - **TaxCategory**
        - **TaxScheme**
          - **ID**: 1000
          - **Name**: IGV
          - **TaxTypeCode**: VAT
  - **LegalMonetaryTotal**
    - **LineExtensionAmount** (currencyID=PEN): 100.00
    - **PayableAmount** (currencyID=PEN): 118.00
  - **CreditNoteLine**
    - **ID**: 1
    - **CreditedQuantity** (unitCode=NIU): 1
    - **LineExtensionAmount** (currencyID=PEN): 100.00
    - **Item**
      - **Description**: Anulación de ejemplo
    - **Price**
      - **PriceAmount** (currencyID=PEN): 100.00
