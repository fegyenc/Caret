# Electronic tax documents (EnvioDTE)

> File: cl-factura.xml

- **Issuer RUT:** 76000001-1
- **RUT that sends:** 11111111-1
- **RUT that receives:** 60803000-K
- **Resolution date:** 2026-01-05
- **Resolution number:** 0
- **Signed at:** 2026-03-04T10:15:30
- **Documents of type 33:** 1
- **Documents of type 34:** 1

## Documents (2)

### Electronic invoice 1234

- **Type:** 33 — Factura Electrónica
- **Folio:** 1234
- **Issue date:** 2026-03-04
- **Due date:** 2026-04-03
- **Currency:** CLP (Chilean pesos)
- **Form of payment:** 2 — Pago Crédito
- **Format version:** 1.0

#### Issuer

- **Name:** Muestra Proveedora SpA
- **RUT:** 76000001-1
- **Line of business:** Venta de articulos de muestra
- **Economic activity code:** 479100
- **Address:** Calle de Prueba 100, Providencia, Santiago
- **Phone:** +56 2 2000 0000
- **Email:** facturas@ejemplo.invalid

#### Receiver

- **Name:** Cliente de Ejemplo Limitada
- **RUT:** 76000002-K
- **Line of business:** Servicios de ejemplo
- **Address:** Avenida Ficticia 200, Las Condes, Santiago

#### Items

| # | Code | Name | Description | Quantity | Unit | Unit price | Discount | Exempt | Amount |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | INT1 CUA-01 | Cuaderno de muestra |  | 10 | UN | 8000 | 8000 (10 %) |  | 72000 |
| 2 |  | Servicio de ejemplo | Servicio exento de IVA | 1 |  | 20000 |  | 1 — El Producto o Servicio NO ESTÁ Afecto a IVA | 20000 |
| 3 |  | Producto de ejemplo |  | 2 |  | 14000 |  |  | 28000 |

#### Discounts and surcharges of the document

| # | Movement | Description | Value | Exemption |
| --- | --- | --- | --- | --- |
| 1 | Discount | Descuento de ejemplo | 0 |  |

#### Totals

- **Net amount:** 100000
- **Exempt amount:** 20000
- **VAT rate:** 19.00 %
- **VAT:** 19000
- **Additional tax or withholding 24 — Impuesto Art. 42 a) Licores, Pisco, Destilados:** 31.5 % → 1000
- **Total:** 140000

#### Referenced documents

| # | Document type | Folio | Date | Use | Reason |
| --- | --- | --- | --- | --- | --- |
| 1 | 801 | OC-555 | 2026-03-01 |  |  |

#### Electronic stamp

- **Stamp issued at:** 2026-03-04T10:15:29
- **Folios authorized:** 1200 — 1300
- **Folios authorized on:** 2026-01-05
- **Signature of the stamp:** digital signature (not printed)
- **Signature of the SII over the folios:** digital signature (not printed)
- **Document signed at:** 2026-03-04T10:15:30

### Electronic invoice, not subject to VAT or exempt 77

- **Type:** 34 — Factura No Afecta o Exenta Electrónica
- **Folio:** 77
- **Issue date:** 2026-03-04
- **Currency:** CLP (Chilean pesos)
- **Means of payment:** EF — Efectivo
- **Format version:** 1.0

#### Issuer

- **Name:** Muestra Proveedora SpA
- **RUT:** 76000001-1
- **Line of business:** Venta de articulos de muestra
- **Economic activity code:** 479100

#### Receiver

- **Name:** Otro Cliente Exento SpA
- **RUT:** 76000003-8

#### Items

| # | Code | Name | Amount |
| --- | --- | --- | --- |
| 1 |  | Servicio exento de ejemplo | 50000 |

#### Totals

- **Exempt amount:** 50000
- **Total:** 50000

#### Electronic stamp

- **Document signed at:** 2026-03-04T10:16:00

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **EnvioDTE** (version=1.0)
  - **SetDTE** (ID=SetDoc)
    - **Caratula** (version=1.0)
      - **RutEmisor**: 76000001-1
      - **RutEnvia**: 11111111-1
      - **RutReceptor**: 60803000-K
      - **FchResol**: 2026-01-05
      - **NroResol**: 0
      - **TmstFirmaEnv**: 2026-03-04T10:15:30
      - **SubTotDTE**
        - **TpoDTE**: 33
        - **NroDTE**: 1
      - **SubTotDTE**
        - **TpoDTE**: 34
        - **NroDTE**: 1
    - **DTE** (version=1.0)
      - **Documento** (ID=F1234T33)
        - **Encabezado**
          - **IdDoc**
            - **TipoDTE**: 33
            - **Folio**: 1234
            - **FchEmis**: 2026-03-04
            - **FmaPago**: 2
            - **FchVenc**: 2026-04-03
          - **Emisor**
            - **RUTEmisor**: 76000001-1
            - **RznSoc**: Muestra Proveedora SpA
            - **GiroEmis**: Venta de articulos de muestra
            - **Telefono**: +56 2 2000 0000
            - **CorreoEmisor**: facturas@ejemplo.invalid
            - **Acteco**: 479100
            - **DirOrigen**: Calle de Prueba 100
            - **CmnaOrigen**: Providencia
            - **CiudadOrigen**: Santiago
          - **Receptor**
            - **RUTRecep**: 76000002-K
            - **RznSocRecep**: Cliente de Ejemplo Limitada
            - **GiroRecep**: Servicios de ejemplo
            - **DirRecep**: Avenida Ficticia 200
            - **CmnaRecep**: Las Condes
            - **CiudadRecep**: Santiago
          - **Totales**
            - **MntNeto**: 100000
            - **MntExe**: 20000
            - **TasaIVA**: 19.00
            - **IVA**: 19000
            - **ImptoReten**
              - **TipoImp**: 24
              - **TasaImp**: 31.5
              - **MontoImp**: 1000
            - **MntTotal**: 140000
        - **Detalle**
          - **NroLinDet**: 1
          - **CdgItem**
            - **TpoCodigo**: INT1
            - **VlrCodigo**: CUA-01
          - **NmbItem**: Cuaderno de muestra
          - **QtyItem**: 10
          - **UnmdItem**: UN
          - **PrcItem**: 8000
          - **DescuentoPct**: 10
          - **DescuentoMonto**: 8000
          - **MontoItem**: 72000
        - **Detalle**
          - **NroLinDet**: 2
          - **NmbItem**: Servicio de ejemplo
          - **DscItem**: Servicio exento de IVA
          - **QtyItem**: 1
          - **PrcItem**: 20000
          - **IndExe**: 1
          - **MontoItem**: 20000
        - **Detalle**
          - **NroLinDet**: 3
          - **NmbItem**: Producto de ejemplo
          - **QtyItem**: 2
          - **PrcItem**: 14000
          - **MontoItem**: 28000
        - **DscRcgGlobal**
          - **NroLinDR**: 1
          - **TpoMov**: D
          - **GlosaDR**: Descuento de ejemplo
          - **TpoValor**: $
          - **ValorDR**: 0
        - **Referencia**
          - **NroLinRef**: 1
          - **TpoDocRef**: 801
          - **FolioRef**: OC-555
          - **FchRef**: 2026-03-01
        - **TED** (version=1.0)
          - **DD**
            - **RE**: 76000001-1
            - **TD**: 33
            - **F**: 1234
            - **FE**: 2026-03-04
            - **RR**: 76000002-K
            - **RSR**: Cliente de Ejemplo Limitada
            - **MNT**: 140000
            - **IT1**: Cuaderno de muestra
            - **CAF** (version=1.0)
              - **DA**
                - **RE**: 76000001-1
                - **RS**: Muestra Proveedora SpA
                - **TD**: 33
                - **RNG**
                  - **D**: 1200
                  - **H**: 1300
                - **FA**: 2026-01-05
                - **RSAPK**
                  - **M**: SAMPLEMODULUSAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
                  - **E**: Aw==
                - **IDK**: 100
              - **FRMA**: digital signature (not printed)
            - **TSTED**: 2026-03-04T10:15:29
          - **FRMT**: digital signature (not printed)
        - **TmstFirma**: 2026-03-04T10:15:30
      - **Signature**: digital signature (not printed)
    - **DTE** (version=1.0)
      - **Documento** (ID=F77T34)
        - **Encabezado**
          - **IdDoc**
            - **TipoDTE**: 34
            - **Folio**: 77
            - **FchEmis**: 2026-03-04
            - **MedioPago**: EF
          - **Emisor**
            - **RUTEmisor**: 76000001-1
            - **RznSoc**: Muestra Proveedora SpA
            - **GiroEmis**: Venta de articulos de muestra
            - **Acteco**: 479100
          - **Receptor**
            - **RUTRecep**: 76000003-8
            - **RznSocRecep**: Otro Cliente Exento SpA
          - **Totales**
            - **MntExe**: 50000
            - **MntTotal**: 50000
        - **Detalle**
          - **NroLinDet**: 1
          - **NmbItem**: Servicio exento de ejemplo
          - **MontoItem**: 50000
        - **TmstFirma**: 2026-03-04T10:16:00
  - **Signature**: digital signature (not printed)
