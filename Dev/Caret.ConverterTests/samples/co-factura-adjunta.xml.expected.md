# Container of electronic documents SETP990000001

> File: co-factura-adjunta.xml
> UBL 2.1 · Factura Electrónica de Venta

This file wraps an electronic document and the answer of the tax authority in one XML file.

- **Reference:** 990000001
- **Issue date:** 2026-03-04 10:16:00-05:00
- **Issuer:** Comercializadora Ejemplo S.A.S.
- **Customer:** Cliente de Prueba Ltda.

## Electronic sales invoice SETP990000001

- **Issue date:** 2026-03-04 10:15:00-05:00
- **Due date:** 2026-04-03
- **Currency:** COP
- **Unique code (CUFE / CUDE):** 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef (CUFE-SHA384)
- **Environment:** Test

**Notes**

- Factura de ejemplo con datos ficticios.

### Issuer

- **Name:** Comercializadora Ejemplo S.A.S.
- **Tax ID:** 900000001-1 (31)
- **Address:** Calle 1 # 2-3, Bogotá, D.C., Bogotá, Colombia
- **Email:** facturacion@ejemplo.test
- **Phone:** 6010000000
- **Fiscal responsibilities:** O-13;O-15

### Customer

- **Name:** Cliente de Prueba Ltda.
- **Tax ID:** 900000002-2 (31)
- **Address:** Carrera 4 # 5-6, Medellín, Antioquia, Colombia
- **Email:** compras@cliente.test
- **Fiscal responsibilities:** R-99-PN

### Payment

- **Form of payment:** Credit
- **Payment means (code):** ZZZ

### Lines

| # | Description | Quantity | Unit | Unit price | Line total |
| --- | --- | --- | --- | --- | --- |
| 1 | Resma de papel carta (caja x 10) | 10.000000 | EA | 50000.00 COP | 500000.00 COP |
| 2 | Silla de oficina \| modelo "Ejemplo" \*ergonómica\* | 2.000000 | EA | 250000.00 COP | 500000.00 COP |

### Taxes

| Tax | Taxable amount | Rate | Amount |
| --- | --- | --- | --- |
| 01 · IVA | 1000000.00 COP | 19.00 % | 190000.00 COP |

### Totals

- **Total before taxes:** 1000000.00 COP
- **Taxable base:** 1000000.00 COP
- **Total with taxes:** 1190000.00 COP
- **Total payable:** 1190000.00 COP

## Answer of the tax authority

- **Reference:** SETP990000001
- **Unique code (CUFE / CUDE):** 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
- **Result:** 02
- **Issue date:** 2026-03-04 10:15:30-05:00

- **Provider:** Dirección de Impuestos y Aduanas Nacionales
- **Result:** 02: Documento validado por la DIAN (texto de ejemplo)

### Answers

| Code | Message |
| --- | --- |
| FAD00 | Mensaje de ejemplo de una regla de validación |

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **AttachedDocument**
  - **UBLExtensions**
    - **UBLExtension**
      - **ExtensionContent**
        - **Signature**: digital signature (not printed)
  - **UBLVersionID**: UBL 2.1
  - **CustomizationID**: Documentos adjuntos
  - **ProfileID**: Factura Electrónica de Venta
  - **ProfileExecutionID**: 2
  - **ID**: 990000001
  - **IssueDate**: 2026-03-04
  - **IssueTime**: 10:16:00-05:00
  - **DocumentType**: Contenedor de Factura Electrónica
  - **ParentDocumentID**: SETP990000001
  - **SenderParty**
    - **PartyTaxScheme**
      - **RegistrationName**: Comercializadora Ejemplo S.A.S.
      - **CompanyID** (schemeID=1, schemeName=31): 900000001
      - **TaxScheme**
        - **ID**: 01
        - **Name**: IVA
  - **ReceiverParty**
    - **PartyTaxScheme**
      - **RegistrationName**: Cliente de Prueba Ltda.
      - **CompanyID** (schemeID=2, schemeName=31): 900000002
      - **TaxScheme**
        - **ID**: 01
        - **Name**: IVA
  - **Attachment**
    - **ExternalReference**
      - **MimeCode**: text/xml
      - **EncodingCode**: UTF-8
      - **Description**: an XML document stored as text
        - **Invoice**
          - **UBLExtensions**
            - **UBLExtension**
              - **ExtensionContent**
                - **Signature**: digital signature (not printed)
          - **UBLVersionID**: UBL 2.1
          - **CustomizationID**: 10
          - **ProfileID**: DIAN 2.1: Factura Electrónica de Venta
          - **ProfileExecutionID**: 2
          - **ID**: SETP990000001
          - **UUID** (schemeID=2, schemeName=CUFE-SHA384): 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
          - **IssueDate**: 2026-03-04
          - **IssueTime**: 10:15:00-05:00
          - **DueDate**: 2026-04-03
          - **InvoiceTypeCode**: 01
          - **Note**: Factura de ejemplo con datos ficticios.
          - **DocumentCurrencyCode**: COP
          - **LineCountNumeric**: 2
          - **AccountingSupplierParty**
            - **AdditionalAccountID**: 1
            - **Party**
              - **PartyName**
                - **Name**: Comercializadora Ejemplo
              - **PartyTaxScheme**
                - **RegistrationName**: Comercializadora Ejemplo S.A.S.
                - **CompanyID** (schemeAgencyID=195, schemeID=1, schemeName=31): 900000001
                - **TaxLevelCode** (listName=48): O-13;O-15
                - **RegistrationAddress**
                  - **ID**: 11001
                  - **CityName**: Bogotá, D.C.
                  - **CountrySubentity**: Bogotá
                  - **AddressLine**
                    - **Line**: Calle 1 # 2-3
                  - **Country**
                    - **IdentificationCode**: CO
                    - **Name** (languageID=es): Colombia
                - **TaxScheme**
                  - **ID**: 01
                  - **Name**: IVA
              - **Contact**
                - **Telephone**: 6010000000
                - **ElectronicMail**: facturacion@ejemplo.test
          - **AccountingCustomerParty**
            - **AdditionalAccountID**: 1
            - **Party**
              - **PartyTaxScheme**
                - **RegistrationName**: Cliente de Prueba Ltda.
                - **CompanyID** (schemeAgencyID=195, schemeID=2, schemeName=31): 900000002
                - **TaxLevelCode** (listName=48): R-99-PN
                - **RegistrationAddress**
                  - **CityName**: Medellín
                  - **CountrySubentity**: Antioquia
                  - **AddressLine**
                    - **Line**: Carrera 4 # 5-6
                  - **Country**
                    - **IdentificationCode**: CO
                    - **Name** (languageID=es): Colombia
                - **TaxScheme**
                  - **ID**: ZZ
                  - **Name**: No aplica
              - **Contact**
                - **ElectronicMail**: compras@cliente.test
          - **PaymentMeans**
            - **ID**: 2
            - **PaymentMeansCode**: ZZZ
            - **PaymentDueDate**: 2026-04-03
          - **TaxTotal**
            - **TaxAmount** (currencyID=COP): 190000.00
            - **TaxSubtotal**
              - **TaxableAmount** (currencyID=COP): 1000000.00
              - **TaxAmount** (currencyID=COP): 190000.00
              - **TaxCategory**
                - **Percent**: 19.00
                - **TaxScheme**
                  - **ID**: 01
                  - **Name**: IVA
          - **LegalMonetaryTotal**
            - **LineExtensionAmount** (currencyID=COP): 1000000.00
            - **TaxExclusiveAmount** (currencyID=COP): 1000000.00
            - **TaxInclusiveAmount** (currencyID=COP): 1190000.00
            - **PayableAmount** (currencyID=COP): 1190000.00
          - **InvoiceLine**
            - **ID**: 1
            - **InvoicedQuantity** (unitCode=EA): 10.000000
            - **LineExtensionAmount** (currencyID=COP): 500000.00
            - **Item**
              - **Description**: Resma de papel carta (caja x 10)
            - **Price**
              - **PriceAmount** (currencyID=COP): 50000.00
              - **BaseQuantity** (unitCode=EA): 1.000000
          - **InvoiceLine**
            - **ID**: 2
            - **InvoicedQuantity** (unitCode=EA): 2.000000
            - **LineExtensionAmount** (currencyID=COP): 500000.00
            - **Item**
              - **Description**: Silla de oficina | modelo "Ejemplo" \*ergonómica\*
            - **Price**
              - **PriceAmount** (currencyID=COP): 250000.00
              - **BaseQuantity** (unitCode=EA): 1.000000
  - **ParentDocumentLineReference**
    - **LineID**: 1
    - **DocumentReference**
      - **ID**: SETP990000001
      - **UUID** (schemeName=CUFE-SHA384): 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
      - **IssueDate**: 2026-03-04
      - **DocumentType**: ApplicationResponse
      - **Attachment**
        - **ExternalReference**
          - **MimeCode**: text/xml
          - **EncodingCode**: UTF-8
          - **Description**: an XML document stored as text
            - **ApplicationResponse**
              - **UBLVersionID**: UBL 2.1
              - **ID**: 990000001
              - **IssueDate**: 2026-03-04
              - **IssueTime**: 10:15:30-05:00
              - **SenderParty**
                - **PartyTaxScheme**
                  - **RegistrationName**: Dirección de Impuestos y Aduanas Nacionales
                  - **CompanyID** (schemeID=4, schemeName=31): 800197268
              - **DocumentResponse**
                - **Response**
                  - **ResponseCode**: 02
                  - **Description**: Documento validado por la DIAN (texto de ejemplo)
                - **DocumentReference**
                  - **ID**: SETP990000001
                - **LineResponse**
                  - **Response**
                    - **ResponseCode**: FAD00
                    - **Description**: Mensaje de ejemplo de una regla de validación
      - **ResultOfVerification**
        - **ValidatorID**: Dirección de Impuestos y Aduanas Nacionales
        - **ValidationResultCode**: 02
        - **ValidationDate**: 2026-03-04
        - **ValidationTime**: 10:15:30-05:00
