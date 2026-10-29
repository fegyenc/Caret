# Event of a CT-e: Cancelamento

> File: br-cte-evento-cancelamento.xml

- **Access key:** 35261011222333000181570010000004561000000019
- **Type of event:** 110111
- **Date of the event:** 2026-10-21T08:00:00-03:00
- **Sequence number:** 1
- **Environment:** 1 — Produção
- **Tax ID (CNPJ / CPF):** 11222333000181
- **Description:** Cancelamento
- **Protocol:** 135260000000456
- **Justification:** Servico cancelado a pedido do cliente antes da coleta

## Answer of the tax authority

- **Result:** 135: Evento registrado e vinculado a CT-e
- **Event:** Cancelamento
- **Protocol:** 135260000000789
- **Registered at:** 2026-10-21T08:00:30-03:00

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **procEventoCTe** (versao=4.00)
  - **eventoCTe** (versao=4.00)
    - **infEvento** (Id=ID11011135261011222333000181570010000004561000000019001)
      - **cOrgao**: 35
      - **tpAmb**: 1
      - **CNPJ**: 11222333000181
      - **chCTe**: 35261011222333000181570010000004561000000019
      - **dhEvento**: 2026-10-21T08:00:00-03:00
      - **tpEvento**: 110111
      - **nSeqEvento**: 1
      - **detEvento** (versaoEvento=4.00)
        - **evCancCTe**
          - **descEvento**: Cancelamento
          - **nProt**: 135260000000456
          - **xJust**: Servico cancelado a pedido do cliente antes da coleta
    - **Signature**: digital signature (not printed)
  - **retEventoCTe** (versao=4.00)
    - **infEvento**
      - **tpAmb**: 1
      - **verAplic**: SVRS202610
      - **cOrgao**: 35
      - **cStat**: 135
      - **xMotivo**: Evento registrado e vinculado a CT-e
      - **chCTe**: 35261011222333000181570010000004561000000019
      - **tpEvento**: 110111
      - **xEvento**: Cancelamento
      - **nSeqEvento**: 1
      - **dhRegEvento**: 2026-10-21T08:00:30-03:00
      - **nProt**: 135260000000789
