# CT-e (electronic transport document) 456 — series 1

> File: br-cte-autorizada.xml
> CT-e 4.00

- **Access key:** 35261011222333000181570010000004561000000019
- **Nature of the operation:** PRESTACAO DE SERVICO DE TRANSPORTE
- **CFOP:** 6353
- **Issue date:** 2026-10-20T10:30:00-03:00
- **Model:** 57 — CT-e
- **Series:** 1
- **Number:** 456
- **Type:** 0 — CT-e Normal
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
- **Trade name:** Transporte Exemplo
- **Tax ID (CNPJ / CPF):** 11222333000181
- **State registration (IE):** 111222333444
- **Tax regime (CRT):** 3 — Regime Normal
- **Address:** Rua das Rodovias, 100, Centro — SAO PAULO/SP — 01001000
- **Phone:** 1130000000

## Sender

- **Name:** Papelaria Inventada S.A.
- **Trade name:** Papelaria
- **Tax ID (CNPJ / CPF):** 45997418000153
- **State registration (IE):** 555666777888
- **Address:** Avenida das Industrias, 2500, Distrito Industrial — CAMPINAS/SP — 13000000 — BRASIL

## Recipient

- **Name:** Loja Cliente Exemplo Ltda
- **Tax ID (CNPJ / CPF):** 07526557000100
- **State registration (IE):** 123456789
- **Address:** Rua do Comercio, 45, Copacabana — RIO DE JANEIRO/RJ — 22000000

## Value of the service

- **Total value of the service:** 1200.00
- **Amount to receive:** 1200.00

| Component | Amount |
| --- | --- |
| Frete valor | 1000.00 |
| Pedagio | 200.00 |

## Taxes

- **Tax situation (CST):** 00 — Tributação normal do ICMS
- **ICMS base:** 1200.00
- **Rate (%):** 12.00
- **ICMS:** 144.00

## Cargo

- **Value of the cargo:** 15000.00
- **Main product:** Material de escritorio

| Measure | Unit | Quantity |
| --- | --- | --- |
| PESO BRUTO | 01 — KG | 850.0000 |

## Documents carried

| Document | Key or number | Value |
| --- | --- | --- |
| NF-e | 35261012345678000195550010000001231000000019 |  |

## Road transport

- **RNTRC (registry of road carriers):** 12345678

## Observations

- **Complementary:** Entrega agendada com o recebedor.
- **Pedido:** PED-2026-0042

## Answer of the tax authority

- **Result:** 100: Autorizado o uso do CT-e
- **Protocol:** 135260000000456
- **Received at:** 2026-10-20T10:31:12-03:00
- **Environment:** 1 — Produção
- **Version of the application:** SVRS202610
- **Access key:** 35261011222333000181570010000004561000000019

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **cteProc** (versao=4.00)
  - **CTe**
    - **infCte** (Id=CTe35261011222333000181570010000004561000000019, versao=4.00)
      - **ide**
        - **cUF**: 35
        - **cCT**: 00000001
        - **CFOP**: 6353
        - **natOp**: PRESTACAO DE SERVICO DE TRANSPORTE
        - **mod**: 57
        - **serie**: 1
        - **nCT**: 456
        - **dhEmi**: 2026-10-20T10:30:00-03:00
        - **tpImp**: 1
        - **tpEmis**: 1
        - **cDV**: 9
        - **tpAmb**: 1
        - **tpCTe**: 0
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
      - **compl**
        - **xObs**: Entrega agendada com o recebedor.
        - **ObsCont** (xCampo=Pedido)
          - **xTexto**: PED-2026-0042
      - **emit**
        - **CNPJ**: 11222333000181
        - **IE**: 111222333444
        - **xNome**: Transportadora Exemplo Ltda
        - **xFant**: Transporte Exemplo
        - **enderEmit**
          - **xLgr**: Rua das Rodovias
          - **nro**: 100
          - **xBairro**: Centro
          - **cMun**: 3550308
          - **xMun**: SAO PAULO
          - **CEP**: 01001000
          - **UF**: SP
          - **fone**: 1130000000
        - **CRT**: 3
      - **rem**
        - **CNPJ**: 45997418000153
        - **IE**: 555666777888
        - **xNome**: Papelaria Inventada S.A.
        - **xFant**: Papelaria
        - **enderReme**
          - **xLgr**: Avenida das Industrias
          - **nro**: 2500
          - **xBairro**: Distrito Industrial
          - **cMun**: 3509502
          - **xMun**: CAMPINAS
          - **CEP**: 13000000
          - **UF**: SP
          - **cPais**: 1058
          - **xPais**: BRASIL
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
        - **vTPrest**: 1200.00
        - **vRec**: 1200.00
        - **Comp**
          - **xNome**: Frete valor
          - **vComp**: 1000.00
        - **Comp**
          - **xNome**: Pedagio
          - **vComp**: 200.00
      - **imp**
        - **ICMS**
          - **ICMS00**
            - **CST**: 00
            - **vBC**: 1200.00
            - **pICMS**: 12.00
            - **vICMS**: 144.00
      - **infCTeNorm**
        - **infCarga**
          - **vCarga**: 15000.00
          - **proPred**: Material de escritorio
          - **infQ**
            - **cUnid**: 01
            - **tpMed**: PESO BRUTO
            - **qCarga**: 850.0000
        - **infDoc**
          - **infNFe**
            - **chave**: 35261012345678000195550010000001231000000019
        - **infModal** (versaoModal=4.00)
          - **rodo**
            - **RNTRC**: 12345678
    - **Signature**: digital signature (not printed)
  - **protCTe** (versao=4.00)
    - **infProt**
      - **tpAmb**: 1
      - **verAplic**: SVRS202610
      - **chCTe**: 35261011222333000181570010000004561000000019
      - **dhRecbto**: 2026-10-20T10:31:12-03:00
      - **nProt**: 135260000000456
      - **digVal**: aW52ZW50YWRvaW52ZW50YWRvaW52ZW50YWQ=
      - **cStat**: 100
      - **xMotivo**: Autorizado o uso do CT-e
