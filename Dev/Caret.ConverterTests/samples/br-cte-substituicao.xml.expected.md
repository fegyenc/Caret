# CT-e (electronic transport document) 457 — series 1

> File: br-cte-substituicao.xml
> CT-e 4.00

- **Access key:** 35261011222333000181570010000004571000000026
- **Nature of the operation:** PRESTACAO DE SERVICO DE TRANSPORTE
- **CFOP:** 6353
- **Issue date:** 2026-10-22T09:00:00-03:00
- **Model:** 57 — CT-e
- **Series:** 1
- **Number:** 457
- **Type:** 3 — CT-e de Substituição
- **Type of service:** 0 — Normal
- **Mode of transport:** 01 — Rodoviário
- **Environment:** 1 — Produção
- **Form of issue:** 1 — Normal
- **Sent from:** SAO PAULO/SP
- **The service starts in:** SAO PAULO/SP
- **The service ends in:** RIO DE JANEIRO/RJ
- **Who pays (taker of the service):** 3 — Destinatário
- **Indicator of the state registration:** 1 — Contribuinte ICMS
- **The recipient collects the cargo:** 1 — Não
- **Version of the issuing software:** Exemplo 1.0

## Issuer

- **Name:** Transportadora Exemplo Ltda
- **Tax ID (CNPJ / CPF):** 11222333000181
- **State registration (IE):** 111222333444
- **Tax regime (CRT):** 3 — Regime Normal
- **Address:** Rua das Rodovias, 100, Centro — SAO PAULO/SP — 01001000

## Recipient

- **Name:** Loja Cliente Exemplo Ltda
- **Tax ID (CNPJ / CPF):** 07526557000100
- **State registration (IE):** 123456789
- **Address:** Rua do Comercio, 45, Copacabana — RIO DE JANEIRO/RJ — 22000000

## Value of the service

- **Total value of the service:** 1250.00
- **Amount to receive:** 1250.00

## Taxes

- **Tax situation (CST):** 00 — Tributação normal do ICMS
- **ICMS base:** 1250.00
- **Rate (%):** 12.00
- **ICMS:** 150.00

## Cargo

- **Value of the cargo:** 15000.00
- **Main product:** Material de escritorio

| Measure | Unit | Quantity |
| --- | --- | --- |
| PESO BRUTO | 01 — KG | 850.0000 |

## Road transport

- **RNTRC (registry of road carriers):** 12345678

## Related CT-e

- **Replaces the CT-e:** 35261011222333000181570010000004561000000019

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **CTe**
  - **infCte** (Id=CTe35261011222333000181570010000004571000000026, versao=4.00)
    - **ide**
      - **cUF**: 35
      - **cCT**: 00000002
      - **CFOP**: 6353
      - **natOp**: PRESTACAO DE SERVICO DE TRANSPORTE
      - **mod**: 57
      - **serie**: 1
      - **nCT**: 457
      - **dhEmi**: 2026-10-22T09:00:00-03:00
      - **tpImp**: 1
      - **tpEmis**: 1
      - **cDV**: 6
      - **tpAmb**: 1
      - **tpCTe**: 3
      - **procEmi**: 0
      - **verProc**: Exemplo 1.0
      - **cMunEnv**: 3550308
      - **xMunEnv**: SAO PAULO
      - **UFEnv**: SP
      - **modal**: 01
      - **tpServ**: 0
      - **cMunIni**: 3550308
      - **xMunIni**: SAO PAULO
      - **UFIni**: SP
      - **cMunFim**: 3304557
      - **xMunFim**: RIO DE JANEIRO
      - **UFFim**: RJ
      - **retira**: 1
      - **indIEToma**: 1
      - **toma3**
        - **toma**: 3
    - **emit**
      - **CNPJ**: 11222333000181
      - **IE**: 111222333444
      - **xNome**: Transportadora Exemplo Ltda
      - **enderEmit**
        - **xLgr**: Rua das Rodovias
        - **nro**: 100
        - **xBairro**: Centro
        - **cMun**: 3550308
        - **xMun**: SAO PAULO
        - **CEP**: 01001000
        - **UF**: SP
      - **CRT**: 3
    - **dest**
      - **CNPJ**: 07526557000100
      - **IE**: 123456789
      - **xNome**: Loja Cliente Exemplo Ltda
      - **enderDest**
        - **xLgr**: Rua do Comercio
        - **nro**: 45
        - **xBairro**: Copacabana
        - **cMun**: 3304557
        - **xMun**: RIO DE JANEIRO
        - **CEP**: 22000000
        - **UF**: RJ
    - **vPrest**
      - **vTPrest**: 1250.00
      - **vRec**: 1250.00
    - **imp**
      - **ICMS**
        - **ICMS00**
          - **CST**: 00
          - **vBC**: 1250.00
          - **pICMS**: 12.00
          - **vICMS**: 150.00
    - **infCTeNorm**
      - **infCarga**
        - **vCarga**: 15000.00
        - **proPred**: Material de escritorio
        - **infQ**
          - **cUnid**: 01
          - **tpMed**: PESO BRUTO
          - **qCarga**: 850.0000
      - **infModal** (versaoModal=4.00)
        - **rodo**
          - **RNTRC**: 12345678
      - **infCteSub**
        - **chCte**: 35261011222333000181570010000004561000000019
  - **Signature**: digital signature (not printed)
