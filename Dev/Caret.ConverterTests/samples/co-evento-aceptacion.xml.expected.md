# Event of an invoice: Express acceptance

> File: co-evento-aceptacion.xml
> UBL 2.1 · DIAN 2.1: ApplicationResponse de la Factura Electrónica de Venta

- **Issue date:** 2026-03-12 10:30:00-05:00
- **Registered by:** Comprador Inventado S.A.S.
- **Unique code (CUFE / CUDE):** 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
- **Result:** 033: Aceptación expresa
- **Reference:** SETP990000001

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **ApplicationResponse**
  - **UBLVersionID**: UBL 2.1
  - **CustomizationID**: 1
  - **ProfileID**: DIAN 2.1: ApplicationResponse de la Factura Electrónica de Venta
  - **ProfileExecutionID**: 2
  - **ID**: EV990000033
  - **UUID** (schemeID=2, schemeName=CUDE-SHA384): 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
  - **IssueDate**: 2026-03-12
  - **IssueTime**: 10:30:00-05:00
  - **SenderParty**
    - **PartyTaxScheme**
      - **RegistrationName**: Comprador Inventado S.A.S.
      - **CompanyID** (schemeID=7, schemeName=31): 900000002
      - **TaxScheme**
        - **ID**: 01
        - **Name**: IVA
  - **ReceiverParty**
    - **PartyTaxScheme**
      - **RegistrationName**: Vendedor de Ejemplo S.A.S.
      - **CompanyID** (schemeID=3, schemeName=31): 900000001
      - **TaxScheme**
        - **ID**: 01
        - **Name**: IVA
  - **DocumentResponse**
    - **Response**
      - **ResponseCode**: 033
      - **Description**: Aceptación expresa
    - **DocumentReference**
      - **ID**: SETP990000001
      - **UUID** (schemeName=CUFE-SHA384): abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789
      - **DocumentTypeCode**: 01
