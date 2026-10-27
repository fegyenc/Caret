# CFDI of expense NC 7

> File: mx-cfdi40-egreso.xml
> CFDI 4.0

- **Issue date:** 2026-03-10T09:00:00
- **Series:** NC
- **Folio:** 7
- **Currency:** MXN
- **Place of issue (postal code):** 06600
- **Form of payment:** 99 — Por definir
- **Method of payment:** PUE — Pago en una sola exhibición
- **Export:** 04

## Related CFDI

- **Type of relation:** 01 — Nota de crédito de los documentos relacionados
- **UUID:** 6128396f-c09b-4ec6-8699-43c5f7e3b230

## Issuer

- **Name:** ESCUELA KEMPER URGATE
- **RFC:** EKU9003173C9
- **Tax regime:** 601 — General de Ley Personas Morales

## Receiver

- **Name:** UNIVERSIDAD ROBOTICA ESPAÑOLA
- **RFC:** URE180429TM6
- **Tax regime:** 601 — General de Ley Personas Morales
- **Fiscal postal code:** 65000
- **Use of the CFDI:** G02 — Devoluciones, descuentos o bonificaciones

## Lines

| # | Product/service key | Description | Quantity | Unit | Unit price | Discount | Amount | Tax object |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 84111506 | Devolución parcial del servicio | 1 | ACT | 100.00 |  | 100.00 | 02 |

## Taxes

- **Total transferred taxes:** 16.00

**Transferred**

| Tax | Base | Factor | Rate | Amount |
| --- | --- | --- | --- | --- |
| 002 — IVA | 100.00 | Tasa | 0.160000 | 16.00 |

## Totals

- **Subtotal:** 100.00
- **Total:** 116.00

## Digital stamp (Timbre Fiscal Digital)

- **UUID:** 0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d
- **Stamp date:** 2026-03-10T09:00:05
- **Certification provider (RFC):** AAA010101AAA
- **SAT certificate number:** 30001000000500003456

## Complements

These complements are in the file; their fields are in the list of all fields below.

- Detalle

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **Comprobante** (schemaLocation=http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd, Version=4.0, Serie=NC, Folio=7, Fecha=2026-03-10T09:00:00, Sello=digital seal of 180 characters (not printed), NoCertificado=30001000000500003416, Certificado=digital seal of 180 characters (not printed), SubTotal=100.00, Moneda=MXN, Total=116.00, TipoDeComprobante=E, Exportacion=04, MetodoPago=PUE, FormaPago=99, LugarExpedicion=06600)
  - **CfdiRelacionados** (TipoRelacion=01)
    - **CfdiRelacionado** (UUID=6128396f-c09b-4ec6-8699-43c5f7e3b230)
  - **Emisor** (Rfc=EKU9003173C9, Nombre=ESCUELA KEMPER URGATE, RegimenFiscal=601)
  - **Receptor** (Rfc=URE180429TM6, Nombre=UNIVERSIDAD ROBOTICA ESPAÑOLA, DomicilioFiscalReceptor=65000, RegimenFiscalReceptor=601, UsoCFDI=G02)
  - **Conceptos**
    - **Concepto** (ClaveProdServ=84111506, Cantidad=1, ClaveUnidad=ACT, Descripcion=Devolución parcial del servicio, ValorUnitario=100.00, Importe=100.00, ObjetoImp=02)
      - **Impuestos**
        - **Traslados**
          - **Traslado** (Base=100.00, Impuesto=002, TipoFactor=Tasa, TasaOCuota=0.160000, Importe=16.00)
  - **Impuestos** (TotalImpuestosTrasladados=16.00)
    - **Traslados**
      - **Traslado** (Base=100.00, Impuesto=002, TipoFactor=Tasa, TasaOCuota=0.160000, Importe=16.00)
  - **Complemento**
    - **TimbreFiscalDigital** (Version=1.1, UUID=0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d, FechaTimbrado=2026-03-10T09:00:05, RfcProvCertif=AAA010101AAA, SelloCFD=digital seal of 180 characters (not printed), NoCertificadoSAT=30001000000500003456, SelloSAT=digital seal of 180 characters (not printed))
    - **Detalle** (Nota=complemento inventado)
