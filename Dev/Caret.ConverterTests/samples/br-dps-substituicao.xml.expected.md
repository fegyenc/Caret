# Service provision declaration (DPS) 123 · Empresa de Servicos Exemplo Ltda

> File: br-dps-substituicao.xml · National NFS-e standard 1.01 · Declaration only: not yet an NFS-e

## The document

- **Series and DPS number:** 1 / 123
- **Issued on:** 2026-10-27T09:15:00-03:00
- **Competence date:** 2026-10-27
- **Issuing municipality:** IBGE 3550308
- **Place of the service:** IBGE 3550308
- **Environment:** 2 — Homologação
- **DPS issued by:** 1 — Prestador
- **Application version:** EmissorExemplo_1.0
- **Replaces the NFS-e:** 35503082211222333000181000000000012226109876543215
- **Reason of the substitution:** 99 — Outros · Correcao do valor do servico informado

## Provider

- **Name:** Empresa de Servicos Exemplo Ltda
- **CNPJ:** 11222333000181
- **Municipal registration (IM):** 123456
- **Address:** Avenida de Teste 1000, Sala 12, Centro · IBGE 3550308 · CEP 01310100
- **Phone:** 1130000000
- **Email:** contato@exemplo.test
- **Simples Nacional:** 3 — Optante - Microempresa ou Empresa de Pequeno Porte (ME/EPP)
- **Tax computation under the Simples Nacional:** 1 — Regime de apuração dos tributos federais e municipal pelo SN
- **Special tax regime:** 0 — Nenhum

## Customer

- **Name:** Cliente Inventado S.A.
- **CNPJ:** 45997418000153
- **Municipal registration (IM):** 123456
- **Address:** Avenida de Teste 1000, Sala 12, Centro · IBGE 3550308 · CEP 01310100
- **Phone:** 1130000000
- **Email:** contato@exemplo.test

## The service

- **National tax code (cTribNac):** 010101
- **Description:** Analise e desenvolvimento de sistemas: servico de exemplo para testes
- **Additional information:** Contrato de exemplo 2026/01

## Values and ISSQN

- **Value of the services:** R$ 1500.00
- **Unconditional discount:** R$ 100.00
- **ISSQN taxation:** 1 — Operação tributável
- **ISSQN withholding:** 2 — Retido pelo Tomador
- **ISSQN rate (declared):** 2.00 %

## Other taxes

- **Withheld: income tax (IRRF):** R$ 21.00

## IBS and CBS (tax reform)

- **Purpose:** 0 — NFS-e regular
- **Personal use or consumption:** 0 — Não
- **Operation indicator (cIndOp):** 100301
- **Recipient:** 0 — o destinatário é o próprio tomador/adquirente identificado na NFS-e (tomador = adquirente = destinatário)
- **Situation (CST) and classification of the IBS/CBS:** 000 · 000001

## All fields of the file

The summary above shows what a reader needs; this list has every field of the XML file, in order, so nothing is hidden.

- **DPS** (versao=1.01)
  - **infDPS** (Id=DPS355030821122233300018100001000000000000123)
    - **tpAmb**: 2
    - **dhEmi**: 2026-10-27T09:15:00-03:00
    - **verAplic**: EmissorExemplo_1.0
    - **serie**: 1
    - **nDPS**: 123
    - **dCompet**: 2026-10-27
    - **tpEmit**: 1
    - **cLocEmi**: 3550308
    - **subst**
      - **chSubstda**: 35503082211222333000181000000000012226109876543215
      - **cMotivo**: 99
      - **xMotivo**: Correcao do valor do servico informado
    - **prest**
      - **CNPJ**: 11222333000181
      - **IM**: 123456
      - **xNome**: Empresa de Servicos Exemplo Ltda
      - **end**
        - **endNac**
          - **cMun**: 3550308
          - **CEP**: 01310100
        - **xLgr**: Avenida de Teste
        - **nro**: 1000
        - **xCpl**: Sala 12
        - **xBairro**: Centro
      - **fone**: 1130000000
      - **email**: contato@exemplo.test
      - **regTrib**
        - **opSimpNac**: 3
        - **regApTribSN**: 1
        - **regEspTrib**: 0
    - **toma**
      - **CNPJ**: 45997418000153
      - **IM**: 123456
      - **xNome**: Cliente Inventado S.A.
      - **end**
        - **endNac**
          - **cMun**: 3550308
          - **CEP**: 01310100
        - **xLgr**: Avenida de Teste
        - **nro**: 1000
        - **xCpl**: Sala 12
        - **xBairro**: Centro
      - **fone**: 1130000000
      - **email**: contato@exemplo.test
    - **serv**
      - **locPrest**
        - **cLocPrestacao**: 3550308
      - **cServ**
        - **cTribNac**: 010101
        - **xDescServ**: Analise e desenvolvimento de sistemas: servico de exemplo para testes
      - **infoCompl**
        - **xInfComp**: Contrato de exemplo 2026/01
    - **valores**
      - **vServPrest**
        - **vServ**: 1500.00
      - **vDescCondIncond**
        - **vDescIncond**: 100.00
      - **trib**
        - **tribMun**
          - **tribISSQN**: 1
          - **tpRetISSQN**: 2
          - **pAliq**: 2.00
        - **tribFed**
          - **vRetIRRF**: 21.00
        - **totTrib**
          - **indTotTrib**: 0
    - **IBSCBS**
      - **finNFSe**: 0
      - **indFinal**: 0
      - **cIndOp**: 100301
      - **indDest**: 0
      - **valores**
        - **trib**
          - **gIBSCBS**
            - **CST**: 000
            - **cClassTrib**: 000001
