# Event of an MDF-e: Encerramento

> File: br-mdfe-evento-encerramento.xml

- **Access key:** 35261011222333000181580010000001231000000073
- **Type of event:** 110112
- **Date of the event:** 2026-10-21T18:00:00-03:00
- **Sequence number:** 1
- **Environment:** 1 — Produção
- **Tax ID (CNPJ / CPF):** 11222333000181
- **Description:** Encerramento
- **Protocol:** 935260000000123
- **Closed on:** 2026-10-21
- **Closed in (municipality · state):** 3304557 · 33

## Answer of the tax authority

- **Result:** 135: Evento registrado e vinculado a MDF-e
- **Event:** Encerramento
- **Protocol:** 935260000000456
- **Registered at:** 2026-10-21T18:00:20-03:00

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **procEventoMDFe** (versao=3.00)
  - **eventoMDFe** (versao=3.00)
    - **infEvento** (Id=ID1101123526101122233300018158001000000123100000007301)
      - **cOrgao**: 35
      - **tpAmb**: 1
      - **CNPJ**: 11222333000181
      - **chMDFe**: 35261011222333000181580010000001231000000073
      - **dhEvento**: 2026-10-21T18:00:00-03:00
      - **tpEvento**: 110112
      - **nSeqEvento**: 1
      - **detEvento** (versaoEvento=3.00)
        - **evEncMDFe**
          - **descEvento**: Encerramento
          - **nProt**: 935260000000123
          - **dtEnc**: 2026-10-21
          - **cUF**: 33
          - **cMun**: 3304557
    - **Signature**: digital signature (not printed)
  - **retEventoMDFe** (versao=3.00)
    - **infEvento**
      - **tpAmb**: 1
      - **verAplic**: SVRS202610
      - **cOrgao**: 35
      - **cStat**: 135
      - **xMotivo**: Evento registrado e vinculado a MDF-e
      - **chMDFe**: 35261011222333000181580010000001231000000073
      - **tpEvento**: 110112
      - **xEvento**: Encerramento
      - **nSeqEvento**: 1
      - **dhRegEvento**: 2026-10-21T18:00:20-03:00
      - **nProt**: 935260000000456
